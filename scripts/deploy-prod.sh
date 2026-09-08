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
BOOTSTRAP="${BOOTSTRAP:-0}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
smoke_script="${SMOKE_SCRIPT:-$repo_root/scripts/smoke-prod.sh}"
stamp="$(date +%Y%m%d-%H%M%S)"
remote_build_dir="/tmp/immichframe-build-${stamp}"
candidate_image="immichframe:deploy-${stamp}"
rollback_image="immichframe:rollback-${stamp}"
deployment_started=0
rollback_available=0
deployment_complete=0
rollback_in_progress=0
deployment_lock_acquired=0
deployment_lock_dir="${DEPLOY_LOCK_DIR:-/var/lock/immichframe-deploy.lock}"

release_deployment_lock() {
	if [[ "$deployment_lock_acquired" == 1 ]]; then
		ssh "$PROD_HOST" "rmdir '$deployment_lock_dir'" >/dev/null 2>&1 || true
		deployment_lock_acquired=0
	fi
}
trap release_deployment_lock EXIT

for command in curl dotnet git npm ssh; do
	command -v "$command" >/dev/null || {
		echo "Required command not found: $command" >&2
		exit 1
	}
done

source_revision="$(git -C "$repo_root" rev-parse --short=12 HEAD)"
if [[ -n "$(git -C "$repo_root" status --porcelain)" ]]; then
	echo "Refusing to deploy an uncommitted working tree." >&2
	echo "Commit or stash all tracked and untracked changes first." >&2
	exit 1
fi

if [[ "$BOOTSTRAP" != 0 && "$BOOTSTRAP" != 1 ]]; then
	echo "BOOTSTRAP must be 0 or 1." >&2
	exit 1
fi

# Validate desired state before spending time on tests or a build.
ssh "$PROD_HOST" "
	set -e
	cd '$REMOTE_STACK_DIR'
	docker compose config --services | grep -Fx '$COMPOSE_SERVICE' >/dev/null
"

if ! ssh "$PROD_HOST" "mkdir '$deployment_lock_dir'"; then
	echo "Another ImmichFrame deployment is active (lock: $deployment_lock_dir)." >&2
	exit 1
fi
deployment_lock_acquired=1

container_exists=0
if ssh "$PROD_HOST" "docker inspect '$CONTAINER_NAME' >/dev/null 2>&1"; then
	container_exists=1
fi

stable_image_exists=0
if ssh "$PROD_HOST" "docker image inspect '$IMAGE_NAME' >/dev/null 2>&1"; then
	stable_image_exists=1
fi

if [[ "$BOOTSTRAP" == 1 ]]; then
	if [[ "$container_exists" == 1 ]]; then
		echo "BOOTSTRAP=1 is only for an absent container; use normal update mode." >&2
		exit 1
	fi
	if [[ -z "${VERSION:-}" ]]; then
		echo "Bootstrap requires an explicit four-part VERSION." >&2
		exit 1
	fi
else
	if [[ "$container_exists" == 0 ]]; then
		echo "Container '$CONTAINER_NAME' is absent. Refusing an update without explicit bootstrap." >&2
		echo "Verify the pinned Compose configuration, then run BOOTSTRAP=1 VERSION=x.y.z.w $0." >&2
		exit 1
	fi
	ssh "$PROD_HOST" "
		set -e
		running_image_id=\$(docker inspect '$CONTAINER_NAME' --format '{{.Image}}')
		docker image inspect \"\$running_image_id\" >/dev/null
		docker image inspect '$IMAGE_NAME' >/dev/null
	"
fi

current_version=""
if [[ "$container_exists" == 1 ]]; then
	current_version="$({
		ssh "$PROD_HOST" \
			"docker inspect '$CONTAINER_NAME' --format '{{range .Config.Env}}{{println .}}{{end}}'" \
				| sed -n 's/^APP_VERSION=//p' \
				| head -n 1
	} || true)"
fi

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

rollback() {
	[[ "$rollback_available" == 1 && "$rollback_in_progress" == 0 ]] || return 0
	rollback_in_progress=1
	echo "Deployment did not complete; restoring $rollback_image..." >&2
	if ! ssh "$PROD_HOST" "
		set -e
		expected_image_id=\$(docker image inspect '$rollback_image' --format '{{.Id}}')
		docker tag '$rollback_image' '$IMAGE_NAME'
		cd '$REMOTE_STACK_DIR'
		docker compose up -d --no-deps --force-recreate '$COMPOSE_SERVICE'
		for attempt in \$(seq 1 '$READY_ATTEMPTS'); do
			if curl -fsS -o /dev/null http://127.0.0.1:8080/; then
				actual_image_id=\$(docker inspect '$CONTAINER_NAME' --format '{{.Image}}')
				test \"\$actual_image_id\" = \"\$expected_image_id\"
				exit 0
			fi
			sleep 2
		done
		docker logs --tail 100 '$CONTAINER_NAME' >&2
		exit 1
	" >&2; then
		echo "ROLLBACK FAILED: inspect $CONTAINER_NAME immediately." >&2
		return 1
	fi

	curl -fsS --retry 5 --retry-delay 2 --max-time 10 -o /dev/null "$PROD_URL" || {
		echo "ROLLBACK FAILED external reachability check for $PROD_URL." >&2
		return 1
	}
	echo "Rollback verified: $rollback_image is running and reachable." >&2
}

finish() {
	status=$?
	trap - EXIT INT TERM
	if [[ "$deployment_started" == 1 && "$deployment_complete" == 0 ]]; then
		if [[ "$rollback_available" == 1 ]]; then
			rollback || status=1
		else
			echo "Bootstrap did not complete. Candidate '$candidate_image' was retained for diagnosis/resume." >&2
		fi
	fi
	ssh "$PROD_HOST" "rm -rf '$remote_build_dir'" >/dev/null 2>&1 || true
	release_deployment_lock
	exit "$status"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

echo "Running backend tests..."
(cd "$repo_root" && dotnet test)

echo "Running frontend checks and build..."
(cd "$repo_root/immichFrame.Web" && npm run check && npm test && npm run build)

echo "Uploading committed revision $source_revision to $PROD_HOST..."
ssh "$PROD_HOST" "rm -rf '$remote_build_dir' && mkdir -p '$remote_build_dir'"
git -C "$repo_root" archive --format=tar HEAD \
	| ssh "$PROD_HOST" "tar -xf - -C '$remote_build_dir'"

echo "Building $candidate_image (version $VERSION)..."
ssh "$PROD_HOST" \
	"cd '$remote_build_dir' && docker build --build-arg VERSION='$VERSION' \
		--label 'org.opencontainers.image.revision=$source_revision' \
		--label 'immichframe.working-tree.dirty=false' \
		-t '$candidate_image' ."

deployment_started=1
if [[ "$BOOTSTRAP" == 0 ]]; then
	echo "Creating rollback image $rollback_image from the running container..."
	ssh "$PROD_HOST" "
		set -e
		running_image_id=\$(docker inspect '$CONTAINER_NAME' --format '{{.Image}}')
		docker image inspect \"\$running_image_id\" >/dev/null
		docker tag \"\$running_image_id\" '$rollback_image'
	"
	rollback_available=1
elif [[ "$stable_image_exists" == 1 ]]; then
	echo "Creating rollback image $rollback_image from the existing stable tag..."
	ssh "$PROD_HOST" "docker tag '$IMAGE_NAME' '$rollback_image'"
	rollback_available=1
fi

echo "Recreating $COMPOSE_SERVICE..."
ssh "$PROD_HOST" "
	set -e
	docker tag '$candidate_image' '$IMAGE_NAME'
	cd '$REMOTE_STACK_DIR'
	docker compose up -d --no-deps --force-recreate '$COMPOSE_SERVICE'
"

echo "Waiting for the service to become ready..."
ssh "$PROD_HOST" "
	for attempt in \$(seq 1 '$READY_ATTEMPTS'); do
		if curl -fsS -o /dev/null http://127.0.0.1:8080/; then
			exit 0
		fi
		sleep 2
	done
	docker logs --tail 100 '$CONTAINER_NAME' >&2
	exit 1
"

PROD_URL="$PROD_URL" "$smoke_script"

deployed_image_id="$(ssh "$PROD_HOST" "
	set -e
	expected_image_id=\$(docker image inspect '$candidate_image' --format '{{.Id}}')
	actual_image_id=\$(docker inspect '$CONTAINER_NAME' --format '{{.Image}}')
	test \"\$actual_image_id\" = \"\$expected_image_id\"
	printf '%s\\n' \"\$actual_image_id\"
")"
deployment_complete=1
echo "Deployment complete:"
echo "  URL:      $PROD_URL"
echo "  Version:  $VERSION"
echo "  Revision: $source_revision"
echo "  Image ID: $deployed_image_id"
if [[ "$rollback_available" == 1 ]]; then
	echo "  Rollback: $rollback_image"
else
	echo "  Mode:     bootstrap (no prior image existed)"
fi
