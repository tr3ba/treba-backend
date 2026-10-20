#!/usr/bin/env bash
# Read-only inspection for the existing AWS SSM deployment channel.
# Prints public Telegram settings and secret presence only. Never enable shell tracing.
set -euo pipefail

printf 'HOST '; hostname
printf 'HOST_RESOURCES\n'
free -m
df -h / /var/lib/docker
df -i /var/lib/docker
docker system df
printf 'ALL_CONTAINERS\n'
docker ps -a --format '{{.Names}} {{.Image}} {{.Status}}'
printf 'IMAGE_INVENTORY\n'
docker image ls --format '{{.ID}} {{.Repository}}:{{.Tag}} {{.Size}}'
printf 'SSM_RESOURCE_ERRORS\n'
grep -Eai 'no space|disk full|ipc messaging|panic|fatal|failed to create|failed to start|out of memory' /var/log/amazon/ssm/errors.log 2>/dev/null | tail -n 12 | cut -c 1-500 || true
printf 'RECENT_RESOURCE_ERRORS\n'
journalctl -k --since '-1 hour' --no-pager 2>/dev/null | grep -Ei 'out of memory|oom-kill|killed process|no space left|I/O error' | tail -n 15 || true
printf 'FRONTEND_IMAGES\n'
docker image ls --filter reference=019066823315.dkr.ecr.eu-west-1.amazonaws.com/treba-frontend --format '{{.ID}} {{.Tag}} {{.Size}}'
printf 'CONTAINERS\n'
docker ps --format '{{.Names}} {{.Image}} {{.Ports}}'

if docker container inspect treba-frontend >/dev/null 2>&1; then
    printf 'FRONTEND_IMAGE '
    docker inspect --format '{{.Image}}' treba-frontend
    printf 'FRONTEND_REVISION '
    docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' treba-frontend
    docker exec treba-frontend node -e '
const fs = require("node:fs");
const paths = JSON.parse(fs.readFileSync("/app/.next/server/app-paths-manifest.json", "utf8"));
console.log("FRONTEND_ADMIN_ENTRY=" + Boolean(paths["/shop-api/admin-entry/route"]));
console.log("FRONTEND_BUILD=" + fs.readFileSync("/app/.next/BUILD_ID", "utf8").trim());
'
    for path in / /shop-api/session /shop-api/admin-entry; do
        printf 'DIRECT_FRONTEND %s ' "$path"
        curl -s -o /dev/null -w '%{http_code}\n' --max-time 5 "http://127.0.0.1$path" || true
    done
fi

if [[ -n "${TREBA_INSPECT_ACCOUNT:-}" ]]; then
    # No hashes, tokens, factor secrets or connection strings are printed.
    # PostgreSQL itself enforces read-only queries for this diagnostic connection.
    python3 - <<'PY'
import json, os, re, subprocess
email = os.environ['TREBA_INSPECT_ACCOUNT']
if not re.fullmatch(r'[A-Za-z0-9._+@-]{3,320}', email):
    raise SystemExit('Invalid diagnostic account email')
container = json.loads(subprocess.check_output(['docker', 'inspect', 'treba-backend']))[0]
values = dict(item.split('=', 1) for item in container['Config']['Env'] if '=' in item)
connection = values.get('ConnectionStrings__DefaultConnection', '')
if not connection or '"' in connection or "'" in connection:
    raise SystemExit('Diagnostic connection format is not supported; no credentials were printed')
fields = dict((key.strip().lower(), value.strip()) for part in connection.split(';') if '=' in part for key, value in [part.split('=', 1)])
environment = dict(os.environ, PGHOST=fields['host'], PGPORT=fields.get('port', '5432'),
    PGDATABASE=fields['database'], PGUSER=fields.get('username', fields.get('user id', '')),
    PGPASSWORD=fields['password'], PGSSLMODE='require', PGCONNECT_TIMEOUT='10',
    PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=10000')
sql = '''SELECT u."Email", r."Name" AS "Role", u."Status", u."IsDeleted",
u."AuthenticatorEnabled", u."EmailTwoFactorEnabled",
EXISTS(SELECT 1 FROM telegram_bindings b WHERE b."UserId"=u."Id") AS "TelegramGuardLinked",
(SELECT b."Username" FROM telegram_bindings b WHERE b."UserId"=u."Id") AS "TelegramUsername",
(SELECT b."LinkedAt" FROM telegram_bindings b WHERE b."UserId"=u."Id") AS "TelegramLinkedAt",
(SELECT right(b."TelegramUserId"::text,4) FROM telegram_bindings b WHERE b."UserId"=u."Id") AS "TelegramIdSuffix"
FROM users u JOIN roles r ON r."Id"=u."RoleId" WHERE lower(u."Email")=lower(:'account_email');'''
command = ['docker', 'run', '--rm', '-i', '--network', 'host']
for key in ('PGHOST', 'PGPORT', 'PGDATABASE', 'PGUSER', 'PGPASSWORD', 'PGSSLMODE', 'PGCONNECT_TIMEOUT', 'PGOPTIONS'):
    command += ['-e', key]
command += ['postgres:18', 'psql', '-X', '--no-psqlrc', '-v', 'ON_ERROR_STOP=1', '-v', 'account_email=' + email]
result = subprocess.run(command, input=sql, text=True, env=environment, capture_output=True, timeout=35)
if result.returncode:
    raise SystemExit('Read-only account inspection failed; no database credentials were printed')
print('ACCOUNT_ACCESS_CHECK\n' + result.stdout)
try:
    import urllib.request
    request = urllib.request.Request('https://api.telegram.org/bot' + values['Telegram__BotToken'] + '/getMe',
        data=b'{}', headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(request, timeout=15) as response:
        bot = json.load(response)
    print('ACTUAL_BOT_USERNAME=' + bot.get('result', {}).get('username', '<missing>'))
except Exception as error:
    print('BOT_IDENTITY_CHECK_FAILED=' + type(error).__name__)
PY
fi

if docker container inspect treba-backend >/dev/null 2>&1; then
    printf 'BACKEND_IMAGE '
    docker inspect --format '{{.Image}}' treba-backend
    printf 'BACKEND_REVISION '
    docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' treba-backend
    printf 'BACKEND_TELEGRAM_SETTINGS\n'
    docker inspect --format '{{json .Config.Env}}' treba-backend | python3 -c '
import json, sys
values = dict(item.split("=", 1) for item in json.load(sys.stdin) if "=" in item)
for name in ("Telegram__Enabled", "Telegram__LongPollingEnabled", "Telegram__BotUsername", "Telegram__MiniAppUrl", "Telegram__EnforceSellerCategories"):
    print(name + "=" + values.get(name, "<missing>"))
print("Telegram__BotToken_present=" + str(bool(values.get("Telegram__BotToken", "").strip())).lower())
for name in ("ASPNETCORE_ENVIRONMENT", "ASPNETCORE_WEBROOT", "ASPNETCORE_CONTENTROOT", "ASPNETCORE_HTTPS_PORT"):
    print(name + "=" + values.get(name, "<missing>"))
'
fi

printf 'DEPLOYMENT_ENV_FILE\n'
python3 - <<'PY'
from pathlib import Path
path = Path('/home/ubuntu/treba-backend.env')
if not path.is_file():
    print('repository_env_file_present=false')
else:
    print('repository_env_file_present=true')
    values = {}
    for line in path.read_text().splitlines():
        if line.strip() and not line.lstrip().startswith('#') and '=' in line:
            key, value = line.split('=', 1)
            values[key.strip()] = value.strip()
    for name in ('Telegram__Enabled', 'Telegram__LongPollingEnabled', 'Telegram__BotUsername', 'Telegram__MiniAppUrl', 'Telegram__EnforceSellerCategories'):
        print(name + '=' + values.get(name, '<missing>'))
    print('Telegram__BotToken_present=' + str(bool(values.get('Telegram__BotToken', '').strip())).lower())
PY

for path in /ping /telegram/ /api/telegram/configuration; do
    printf 'DIRECT_API %s ' "$path"
    curl -s -o /dev/null -w '%{http_code}\n' --max-time 5 "http://127.0.0.1:5000$path" || true
done

filter_routes() {
    grep -E '^# configuration file|^[[:space:]]*(listen|server_name|location|proxy_pass|root|include|return)[[:space:]]' |
        sed -E 's#(https?://)[^/[:space:]@]+@#\1[redacted]@#g' || true
}

printf 'HOST_NGINX_ROUTES\n'
if command -v nginx >/dev/null 2>&1; then nginx -T 2>&1 | filter_routes || true; fi
printf 'CONTAINER_PROXY_ROUTES\n'
while read -r name image; do
    if [[ "$name $image" =~ nginx|proxy|gateway ]]; then
        printf 'PROXY_CONTAINER %s\n' "$name"
        docker inspect --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}' "$name"
        docker exec "$name" nginx -T 2>&1 | filter_routes || true
    fi
done < <(docker ps --format '{{.Names}} {{.Image}}')

printf 'CADDY_ROUTES\n'
if docker container inspect treba-caddy >/dev/null 2>&1; then
    docker inspect --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}' treba-caddy
    docker inspect --format '{{range $name, $network := .NetworkSettings.Networks}}{{println $name}}{{end}}' treba-caddy
    docker exec treba-caddy caddy adapt --config /etc/caddy/Caddyfile --adapter caddyfile 2>/dev/null | python3 -c '
import json, sys
config = json.load(sys.stdin)
def routes(items, indent=""):
    for route in items:
        for match in route.get("match", []):
            print(indent + "MATCH " + json.dumps({key: value for key, value in match.items() if key in ("host", "path", "method")}))
        for handler in route.get("handle", []):
            print(indent + "HANDLER " + handler.get("handler", ""))
            for upstream in handler.get("upstreams", []):
                print(indent + "UPSTREAM " + upstream.get("dial", ""))
            routes(handler.get("routes", []), indent + "  ")
for name, server in config.get("apps", {}).get("http", {}).get("servers", {}).items():
    print("SERVER " + name + " LISTEN " + json.dumps(server.get("listen", [])))
    routes(server.get("routes", []))
' || true
fi

printf 'LISTENING_PORTS\n'
ss -ltn | head -n 20

printf 'PUBLISHED_TELEGRAM_ASSETS\n'
while read -r repository tag; do
    if [[ "$tag" =~ ^[0-9a-f]{40}$ ]]; then
        printf 'IMAGE %s:%s\n' "$repository" "$tag"
        docker run --rm --read-only --network none --entrypoint /bin/sh "$repository:$tag" -c '
            ls -ld /app/wwwroot /app/wwwroot/telegram 2>/dev/null || true
            find /app/wwwroot/telegram -maxdepth 1 -type f -printf "%f %s bytes\n" 2>/dev/null || true
        '
    fi
done < <(docker image ls --filter reference=019066823315.dkr.ecr.eu-west-1.amazonaws.com/treba-backend --format '{{.Repository}} {{.Tag}}' | head -n 3)
