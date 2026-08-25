#!/usr/bin/env bash
# Remote commands intentionally expand the validated deployment settings locally.
# shellcheck disable=SC2029

set -Eeuo pipefail

PROD_HOST="${PROD_HOST:-root@192.168.1.24}"
PROD_URL="${PROD_URL:-http://192.168.1.24:8080/}"
REMOTE_STACK_DIR="${REMOTE_STACK_DIR:-/opt/stacks/immich}"
COMPOSE_SERVICE="${COMPOSE_SERVICE:-immichframe}"
CONTAINER_NAME="${CONTAINER_NAME:-immichframe}"
IMAGE_NAME="${IMAGE_NAME:-immichframe:custom}"
READY_ATTEMPTS="${READY_ATTEMPTS:-30}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stamp="$(date +%Y%m%d-%H%M%S)"
remote_build_dir="/tmp/immichframe-build-${stamp}"
candidate_image="immichframe:deploy-${stamp}"
rollback_image="immichframe:rollback-${stamp}"

for command in curl npm ssh tar; do
	command -v "$command" >/dev/null || {
		echo "Required command not found: $command" >&2
		exit 1
	}
done

current_version="$({
	ssh "$PROD_HOST" \
		"docker inspect '$CONTAINER_NAME' --format '{{range .Config.Env}}{{println .}}{{end}}'" \
			| sed -n 's/^APP_VERSION=//p' \
			| head -n 1
} || true)"

if [[ -z "${VERSION:-}" ]]; then
	if [[ "$current_version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
		VERSION="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.${BASH_REMATCH[3]}.$((BASH_REMATCH[4] + 1))"
	else
		echo "Unable to derive the next version from '$current_version'. Set VERSION explicitly." >&2
		exit 1
	fi
fi

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
	echo "VERSION must contain four numeric components (for example, 1.0.25.2)." >&2
	exit 1
fi

cleanup() {
	ssh "$PROD_HOST" "rm -rf '$remote_build_dir'" >/dev/null 2>&1 || true
}
trap cleanup EXIT

rollback() {
	echo "Deployment failed; restoring $rollback_image..." >&2
	ssh "$PROD_HOST" "
		set -e
		docker tag '$rollback_image' '$IMAGE_NAME'
		cd '$REMOTE_STACK_DIR'
		docker compose up -d --no-deps --force-recreate '$COMPOSE_SERVICE'
	" >&2
}

echo "Running frontend checks..."
(cd "$repo_root/immichFrame.Web" && npm run check)

echo "Uploading the working tree to $PROD_HOST..."
ssh "$PROD_HOST" "rm -rf '$remote_build_dir' && mkdir -p '$remote_build_dir'"
tar -C "$repo_root" -czf - \
	--exclude=.git \
	--exclude=.claude \
	--exclude='**/bin' \
	--exclude='**/obj' \
	--exclude='**/node_modules' \
	. | ssh "$PROD_HOST" "tar -xzf - -C '$remote_build_dir'"

echo "Building $candidate_image (version $VERSION)..."
ssh "$PROD_HOST" \
	"cd '$remote_build_dir' && docker build --build-arg VERSION='$VERSION' -t '$candidate_image' ."

echo "Creating rollback image $rollback_image..."
ssh "$PROD_HOST" "docker image inspect '$IMAGE_NAME' >/dev/null && docker tag '$IMAGE_NAME' '$rollback_image'"

echo "Recreating $COMPOSE_SERVICE..."
if ! ssh "$PROD_HOST" "
	set -e
	docker tag '$candidate_image' '$IMAGE_NAME'
	cd '$REMOTE_STACK_DIR'
	docker compose up -d --no-deps --force-recreate '$COMPOSE_SERVICE'
"; then
	rollback
	exit 1
fi

echo "Waiting for the service to become ready..."
if ! ssh "$PROD_HOST" "
	for attempt in \$(seq 1 '$READY_ATTEMPTS'); do
		if curl -fsS -o /dev/null http://127.0.0.1:8080/; then
			exit 0
		fi
		sleep 2
	done
	docker logs --tail 100 '$CONTAINER_NAME' >&2
	exit 1
"; then
	rollback
	exit 1
fi

if ! curl -fsS --retry 5 --retry-delay 2 --max-time 10 -o /dev/null "$PROD_URL"; then
	rollback
	exit 1
fi

deployed_image_id="$(ssh "$PROD_HOST" "docker inspect '$CONTAINER_NAME' --format '{{.Image}}'")"
echo "Deployment complete:"
echo "  URL:      $PROD_URL"
echo "  Version:  $VERSION"
echo "  Image ID: $deployed_image_id"
echo "  Rollback: $rollback_image"
