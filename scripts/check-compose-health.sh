#!/usr/bin/env bash
# Opt-in host-side health check. This script does not install or schedule itself.
set -Eeuo pipefail

STACK_DIR="${STACK_DIR:-/opt/stacks/immich}"
COMPOSE_SERVICE="${COMPOSE_SERVICE:-immichframe}"
CONTAINER_NAME="${CONTAINER_NAME:-immichframe}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/}"
ALERT_WEBHOOK_URL="${ALERT_WEBHOOK_URL:-}"

failure=""
if ! cd "$STACK_DIR"; then
	failure="Compose stack directory is absent: $STACK_DIR"
elif ! docker compose config --services | grep -Fx "$COMPOSE_SERVICE" >/dev/null; then
	failure="Compose service is absent: $COMPOSE_SERVICE"
elif ! docker inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
	failure="Container is absent: $CONTAINER_NAME"
elif [[ "$(docker inspect "$CONTAINER_NAME" --format '{{.State.Running}}')" != true ]]; then
	failure="Container is not running: $CONTAINER_NAME"
elif ! curl -fsS --max-time 10 -o /dev/null "$HEALTH_URL"; then
	failure="Health URL is unreachable: $HEALTH_URL"
fi

if [[ -z "$failure" ]]; then
	echo "ImmichFrame Compose state is healthy."
	exit 0
fi

echo "$failure" >&2
if [[ -n "$ALERT_WEBHOOK_URL" ]]; then
	python3 - "$failure" <<'PY' |
import json, sys
print(json.dumps({"text": "ImmichFrame health alert: " + sys.argv[1]}))
PY
		curl -fsS --max-time 10 -H 'Content-Type: application/json' --data-binary @- "$ALERT_WEBHOOK_URL" >/dev/null || true
fi
exit 1
