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

cleanup() {
    result=$?
    trap - EXIT
    docker rm -f "$candidate" >/dev/null 2>&1 || true
    if [[ "$completed" != 1 ]]; then
        echo 'Deployment did not complete; restoring the previous running configuration.'
        if [[ "$proxy_changed" == 1 ]]; then
            # Write in place: replacing the inode would leave the bind mount on the old file.
            cat "$caddy_backup" > "$caddy_file"
            docker exec treba-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || true
        fi
        if [[ "$switched" == 1 ]]; then
            docker rm -f treba-backend >/dev/null 2>&1 || true
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

# Checks the actual server DB and token, without opening HTTP or consuming bot updates.
docker run --rm --env-file "$env_file" --network host "$image" --check-telegram-deployment

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
    curl -fsS --max-time 10 "$origin/telegram/" | grep 'app-shell' >/dev/null
    curl -fsS --max-time 10 "$origin/telegram/app.js" | grep 'Telegram' >/dev/null
    curl -fsS --max-time 10 "$origin/api/telegram/configuration" | python3 -c '
import json, sys
config = json.load(sys.stdin)
if config.get("configured") is not True or config.get("botUsername") != "treba_marketplace_bot" or config.get("miniAppUrl") != "https://treba.duckdns.org/telegram/":
    raise SystemExit("Unexpected public Telegram configuration")
'
}

wait_for_api "http://$candidate_address"
verify_mini_app "http://$candidate_address"
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

cp -p "$caddy_file" "$caddy_backup"
proxy_changed=1
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
PY
docker exec treba-caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null
docker exec treba-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null

verify_mini_app https://treba.duckdns.org
curl -fsS --max-time 15 https://treba.duckdns.org/ >/dev/null
menu_ready=0
for attempt in $(seq 1 3); do
    if docker exec treba-backend dotnet WebApi.dll --check-telegram-deployment --verify-telegram-menu=true; then
        menu_ready=1
        break
    fi
    sleep 2
done
[[ "$menu_ready" == 1 ]] || { echo 'The bot menu was not configured.'; exit 1; }
completed=1
echo "Telegram deployment passed. Revision: $revision"
echo "Rollback container retained: $previous"
echo "Previous Caddy configuration retained: $caddy_backup"
