#!/usr/bin/env bash
# Invoked by the normal AWS SSM deployment workflow. Never enable shell tracing.
set -euo pipefail
image="${1:?Immutable ECR image reference is required}"
revision="${2:?Git revision is required}"
[[ "$revision" =~ ^[0-9a-f]{40}$ ]] || { echo 'Invalid Git revision'; exit 1; }
[[ "$image" == "019066823315.dkr.ecr.eu-west-1.amazonaws.com/treba-backend:$revision" ]] || { echo 'Unexpected image reference'; exit 1; }

env_file=/home/ubuntu/treba-backend.env
caddy_file=/opt/treba-caddy/Caddyfile
test -f "$env_file"
test -d /opt/treba-backend/uploads
test -f "$caddy_file"
docker container inspect treba-backend >/dev/null
docker container inspect treba-caddy >/dev/null
mounted_caddy=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/etc/caddy/Caddyfile"}}{{.Source}}{{end}}{{end}}' treba-caddy)
[[ "$mounted_caddy" == "$caddy_file" ]] || { echo 'Unexpected Caddy configuration mount'; exit 1; }

deployment_id="${revision:0:12}-$(date -u +%Y%m%d%H%M%S)"
candidate="treba-backend-check-$deployment_id"
previous="treba-backend-previous-$deployment_id"
caddy_backup="${caddy_file}.before-${deployment_id}"
switched=0
old_stopped=0
proxy_changed=0
completed=0
admin_switched=0
admin_previous="treba-admin-previous-$deployment_id"

cleanup() {
    result=$?
    trap - EXIT
    docker rm -f "$candidate" >/dev/null 2>&1 || true
    if [[ "$completed" != 1 ]]; then
        echo 'Deployment did not complete; restoring the previous running configuration.'
        if [[ "$admin_switched" == 1 ]]; then
            docker rm -f treba-admin >/dev/null 2>&1 || true
            if docker container inspect "$admin_previous" >/dev/null 2>&1; then
                docker rename "$admin_previous" treba-admin
                docker start treba-admin >/dev/null
            fi
        fi
        if [[ "$proxy_changed" == 1 ]]; then
            # Write in place: replacing the inode would leave the bind mount on the old file.
            cat "$caddy_backup" > "$caddy_file"
            docker exec treba-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || true
        fi
        if [[ "$switched" == 1 ]]; then
            if docker container inspect treba-backend >/dev/null 2>&1; then
                echo 'Telegram worker diagnostics from the failed candidate:'
                docker logs --tail 200 treba-backend 2>&1 | grep -E 'Telegram worker paused|Telegram Bot API|Telegram bot username' || true
                docker stop treba-backend >/dev/null 2>&1 || true
                docker rename treba-backend "treba-backend-failed-$deployment_id"
                echo "Failed container retained: treba-backend-failed-$deployment_id"
            fi
            docker rename "$previous" treba-backend
            docker start treba-backend >/dev/null
        elif [[ "$old_stopped" == 1 ]]; then
            docker start treba-backend >/dev/null
        fi
    fi
    exit "$result"
}
trap cleanup EXIT

aws ecr get-login-password --region eu-west-1 | docker login --username AWS --password-stdin 019066823315.dkr.ecr.eu-west-1.amazonaws.com >/dev/null
docker pull "$image"

# Apply only the reviewed, backwards-compatible operational migrations before switching HTTP.
# The command checks schema ownership first and leaves the old container running on failure.
docker run --rm --env-file "$env_file" --network host "$image" --migrate-admin-operations --Logging:LogLevel:Microsoft.EntityFrameworkCore=Critical

# Checks the actual server DB and token, without opening HTTP or consuming bot updates.
docker run --rm --env-file "$env_file" --network host "$image" --check-telegram-deployment --Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning

# Test the new HTTP app on an automatically assigned loopback port before stopping the old app.
docker run -d --name "$candidate" --env-file "$env_file" \
    -e ApplyMigrations=false -e Telegram__LongPollingEnabled=false \
    -p 127.0.0.1::5000 -v /opt/treba-backend/uploads:/app/wwwroot/uploads "$image" >/dev/null
candidate_address=$(docker port "$candidate" 5000/tcp)

wait_for_api() {
    local origin="$1"
    for attempt in $(seq 1 45); do
        if curl -fsS --max-time 3 "$origin/ping" >/dev/null 2>&1; then return 0; fi
        sleep 1
    done
    echo 'The new API did not become ready.'
    return 1
}

verify_mini_app() {
    local origin="$1"
    echo "Checking Mini App HTML: $origin/telegram/"
    curl -fsS --max-time 10 "$origin/telegram/" | grep 'app-shell' >/dev/null
    echo "Checking Mini App JavaScript: $origin/telegram/app.js"
    curl -fsS --max-time 10 "$origin/telegram/app.js" | grep 'Telegram' >/dev/null
    local module content_type
    for module in /telegram/app.js /telegram/ui.js /js/editor-drafts.js; do
        content_type=$(curl -fsS --max-time 10 -o /dev/null -w '%{content_type}' "$origin$module")
        [[ "$content_type" =~ ^(application|text)/javascript($|\;) ]] || { echo "Mini App module returned an invalid MIME type: $module ($content_type)"; return 1; }
    done
    echo "Checking Telegram configuration: $origin/api/telegram/configuration"
    curl -fsS --max-time 10 "$origin/api/telegram/configuration" | python3 -c '
import json, sys
config = json.load(sys.stdin)
if config.get("configured") is not True or config.get("botUsername") != "treba_marketplace_bot" or config.get("miniAppUrl") != "https://treba.duckdns.org/telegram/":
    raise SystemExit("Unexpected public Telegram configuration")
'
}

verify_operations() {
    local origin="$1"
    local path code
    curl -fsS --max-time 10 "$origin/health" >/dev/null
    # Exercise real catalog query binding before replacing the serving container.
    curl -fsS --max-time 15 "$origin/api/v1/catalog?size=1&sort=priceasc" | python3 -c '
import json, sys
page = json.load(sys.stdin)
if not isinstance(page.get("content"), list) or page.get("page") != 1 or "totalElements" not in page:
    raise SystemExit("Public catalog returned an invalid page")
'
    for path in /api/v1/admin/orders /api/v1/support /api/v1/admin/operations/customers /api/v1/admin/promotions; do
        code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$origin$path")
        [[ "$code" == 401 ]] || { echo "Operational route unavailable: $path (HTTP $code)"; return 1; }
    done
    echo 'Operational routes exist and require authentication.'
}

wait_for_api "http://$candidate_address"
verify_mini_app "http://$candidate_address"
verify_operations "http://$candidate_address"
docker rm -f "$candidate" >/dev/null
docker exec treba-caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null

docker stop treba-backend >/dev/null
old_stopped=1
docker rename treba-backend "$previous"
switched=1
docker run -d --name treba-backend --restart unless-stopped --env-file "$env_file" \
    -e ApplyMigrations=false --label "org.opencontainers.image.revision=$revision" \
    -p 5000:5000 -v /opt/treba-backend/uploads:/app/wwwroot/uploads "$image" >/dev/null
wait_for_api http://127.0.0.1:5000
verify_mini_app http://127.0.0.1:5000
verify_operations http://127.0.0.1:5000

cp -p "$caddy_file" "$caddy_backup"
proxy_changed=1

# AdminPanel shares the reviewed image but runs separately, with no published host port.
docker network inspect treba-admin-web >/dev/null 2>&1 || docker network create treba-admin-web >/dev/null
if ! docker inspect --format '{{json .NetworkSettings.Networks}}' treba-caddy | python3 -c 'import json,sys; sys.exit(0 if "treba-admin-web" in json.load(sys.stdin) else 1)'; then
    docker network connect treba-admin-web treba-caddy
fi
docker volume create treba-admin-keys >/dev/null
if docker container inspect treba-admin >/dev/null 2>&1; then
    docker stop treba-admin >/dev/null
    docker rename treba-admin "$admin_previous"
fi
admin_switched=1
docker run -d --name treba-admin --restart unless-stopped --network treba-admin-web \
    --add-host host.docker.internal:host-gateway -v treba-admin-keys:/keys \
    --label "org.opencontainers.image.revision=$revision" \
    -e ASPNETCORE_URLS=http://+:5100 -e ApiBaseUrl=http://host.docker.internal:5000/ -e PublicApiBaseUrl=https://treba.duckdns.org/ \
    -e WebsiteLogin__Enabled=true -e WebsiteLogin__StoreOrigin=https://treba.duckdns.org \
    -e WebsiteLogin__AdminOrigin=https://admin.treba.duckdns.org -e DataProtectionKeysPath=/keys \
    --entrypoint dotnet "$image" /app/admin/AdminPanel.dll --contentRoot=/app/admin >/dev/null
admin_ready=0
for attempt in $(seq 1 45); do
    if docker exec treba-admin curl -fsS --max-time 3 http://localhost:5100/health >/dev/null 2>&1; then admin_ready=1; break; fi
    sleep 1
done
[[ "$admin_ready" == 1 ]] || { echo 'AdminPanel did not become ready.'; exit 1; }
python3 - "$caddy_file" <<'PY'
from pathlib import Path
import re, sys
path = Path(sys.argv[1])
if path.resolve() != Path('/opt/treba-caddy/Caddyfile'):
    raise SystemExit('Unexpected Caddy file path')
source = path.read_text()
if re.search(r'(?<!\S)/telegram/\*(?!\S)', source) and re.search(r'(?<!\S)/telegram(?!\S)', source):
    print('Caddy already routes Telegram.')
elif source.count('/api/*') == 1:
    path.write_text(source.replace('/api/*', '/telegram /telegram/* /api/*', 1))
    print('Added Telegram to the existing API path matcher.')
else:
    raise SystemExit('Caddy matcher differs from the inspected configuration; automatic edit cancelled.')
source = path.read_text()
if not re.search(r'(?<!\S)/js/editor-drafts\.js(?!\S)', source):
    if source.count('/api/*') != 1:
        raise SystemExit('Cannot identify the existing backend matcher for the shared Mini App module.')
    source = source.replace('/api/*', '/js/editor-drafts.js /api/*', 1)
    path.write_text(source)
    print('Added the shared Mini App draft module to the backend matcher.')
admin = '''\n# TREBA managed admin entry\nadmin.treba.duckdns.org {\n    reverse_proxy treba-admin:5100\n}\n'''
if 'admin.treba.duckdns.org' not in source:
    path.write_text(source + admin)
elif not re.search(r'admin\.treba\.duckdns\.org\s*\{\s*reverse_proxy treba-admin:5100\s*\}', source):
    raise SystemExit('The existing admin host differs from the reviewed configuration.')
PY
docker exec treba-caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null
docker exec treba-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null

verify_mini_app https://treba.duckdns.org
# The public proxy may intentionally expose /health only to internal callers.
for path in /api/v1/admin/orders /api/v1/support /api/v1/admin/operations/customers /api/v1/admin/promotions; do
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://treba.duckdns.org$path")
    [[ "$code" == 401 ]] || { echo "Public operational route unavailable: $path (HTTP $code)"; exit 1; }
done
curl -fsS --max-time 15 https://treba.duckdns.org/ >/dev/null
admin_public_ready=0
for attempt in $(seq 1 90); do
    if curl -fsS --max-time 3 https://admin.treba.duckdns.org/health >/dev/null 2>&1; then admin_public_ready=1; break; fi
    sleep 2
done
[[ "$admin_public_ready" == 1 ]] || { echo 'Public AdminPanel TLS or health check failed.'; exit 1; }
curl -fsS --max-time 10 https://admin.treba.duckdns.org/ | grep 'blazor.web.js' >/dev/null
curl -fsS --max-time 10 https://admin.treba.duckdns.org/js/site-login.js | grep 'trebaSiteLogin' >/dev/null
echo "Public AdminPanel passed. Revision: $revision"
menu_ready=0
for attempt in $(seq 1 12); do
    if docker exec treba-backend dotnet WebApi.dll --check-telegram-deployment --verify-telegram-menu=true --Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Warning; then
        menu_ready=1
        break
    fi
    sleep 5
done
[[ "$menu_ready" == 1 ]] || { echo 'The bot menu was not configured.'; exit 1; }
completed=1
echo "Telegram deployment passed. Revision: $revision"
echo "Rollback container retained: $previous"
echo "Previous Caddy configuration retained: $caddy_backup"
