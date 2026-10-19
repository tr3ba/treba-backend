#!/usr/bin/env bash
# Read-only inspection for the existing AWS SSM deployment channel.
# Prints public Telegram settings and secret presence only. Never enable shell tracing.
set -euo pipefail

printf 'HOST '; hostname
printf 'CONTAINERS\n'
docker ps --format '{{.Names}} {{.Image}} {{.Ports}}'

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

printf 'LISTENING_PORTS\n'
ss -ltn | head -n 20
