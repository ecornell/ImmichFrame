#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
mkdir -p "$tmp_dir/bin"

cat >"$tmp_dir/bin/git" <<'SH'
#!/usr/bin/env bash
case " $* " in
  *" rev-parse "*) echo abcdef123456 ;;
  *" status "*) ;;
  *" archive "*) printf archive ;;
esac
SH
cat >"$tmp_dir/bin/ssh" <<'SH'
#!/usr/bin/env bash
command_text="${*:2}"
printf '%s\n' "$command_text" >>"$MOCK_LOG"
if [[ "$command_text" == *"mkdir '/var/lock/immichframe-deploy.lock'"* && "${MOCK_LOCKED:-0}" == 1 ]]; then
  exit 1
fi
if [[ "$command_text" == *"docker inspect 'immichframe' >/dev/null 2>&1"* && "$MOCK_CONTAINER" != 1 ]]; then
  exit 1
fi
stable_image="${MOCK_STABLE_IMAGE:-${MOCK_CONTAINER:-0}}"
if [[ "$command_text" == *"docker image inspect 'immichframe:custom' >/dev/null 2>&1"* && "$stable_image" != 1 ]]; then
  exit 1
fi
if [[ "$command_text" == *"{{range .Config.Env}}"* ]]; then
  echo APP_VERSION=1.2.3.4
fi
if [[ "$command_text" == *"docker tag 'immichframe:deploy-"* && "$command_text" == *"docker compose up"* ]]; then
  if [[ "${MOCK_INTERRUPT:-0}" == 1 ]]; then
    kill -TERM "$PPID"
    sleep 0.1
  fi
  [[ "${MOCK_FAIL_RECREATE:-0}" != 1 ]] || exit 1
fi
if [[ "$command_text" == *"expected_image_id="* && "${MOCK_FINAL_MISMATCH:-0}" == 1 ]]; then
  exit 1
fi
if [[ "$command_text" == *"--format '{{.Image}}'"* ]]; then
  echo sha256:test
fi
exit 0
SH
for command in dotnet npm curl; do
	cat >"$tmp_dir/bin/$command" <<'SH'
#!/usr/bin/env bash
exit 0
SH
 done
cat >"$tmp_dir/smoke" <<'SH'
#!/usr/bin/env bash
exit 0
SH
chmod +x "$tmp_dir/bin/"* "$tmp_dir/smoke"

run_deploy() {
	MOCK_LOG="$tmp_dir/ssh.log" PATH="$tmp_dir/bin:$PATH" PROD_HOST=test PROD_URL=http://test/ \
		SMOKE_SCRIPT="$tmp_dir/smoke" READY_ATTEMPTS=1 "$repo_root/scripts/deploy-prod.sh"
}

: >"$tmp_dir/ssh.log"
if MOCK_CONTAINER=0 run_deploy >"$tmp_dir/absent.out" 2>&1; then
	echo "Expected normal update to reject an absent container" >&2
	exit 1
fi
grep -Fq "Refusing an update without explicit bootstrap" "$tmp_dir/absent.out"
if grep -Fq "docker build" "$tmp_dir/ssh.log"; then
	echo "Absent-container preflight happened after build" >&2
	exit 1
fi

: >"$tmp_dir/ssh.log"
MOCK_CONTAINER=0 BOOTSTRAP=1 VERSION=1.2.3.4 run_deploy >"$tmp_dir/bootstrap.out" 2>&1 || {
	cat "$tmp_dir/bootstrap.out" >&2
	exit 1
}
grep -Fq "bootstrap (no prior image existed)" "$tmp_dir/bootstrap.out"
if grep -Fq "immichframe:rollback-" "$tmp_dir/ssh.log"; then
	echo "Bootstrap unexpectedly created a rollback image" >&2
	exit 1
fi

: >"$tmp_dir/ssh.log"
MOCK_CONTAINER=0 MOCK_STABLE_IMAGE=1 BOOTSTRAP=1 VERSION=1.2.3.4 run_deploy >"$tmp_dir/bootstrap-existing.out" 2>&1 || {
	cat "$tmp_dir/bootstrap-existing.out" >&2
	exit 1
}
grep -Fq "Creating rollback image" "$tmp_dir/bootstrap-existing.out"
grep -Fq "docker tag 'immichframe:custom' 'immichframe:rollback-" "$tmp_dir/ssh.log"

: >"$tmp_dir/ssh.log"
if MOCK_CONTAINER=1 MOCK_LOCKED=1 run_deploy >"$tmp_dir/locked.out" 2>&1; then
	echo "Expected a concurrent deployment lock to stop deployment" >&2
	exit 1
fi
grep -Fq "Another ImmichFrame deployment is active" "$tmp_dir/locked.out"
if grep -Fq "docker build" "$tmp_dir/ssh.log"; then
	echo "Lock refusal happened after build" >&2
	exit 1
fi

: >"$tmp_dir/ssh.log"
if MOCK_CONTAINER=1 MOCK_FINAL_MISMATCH=1 run_deploy >"$tmp_dir/mismatch.out" 2>&1; then
	echo "Expected final candidate identity mismatch to fail deployment" >&2
	exit 1
fi
grep -Fq "restoring immichframe:rollback-" "$tmp_dir/mismatch.out"

: >"$tmp_dir/ssh.log"
if MOCK_CONTAINER=1 MOCK_FAIL_RECREATE=1 run_deploy >"$tmp_dir/rollback.out" 2>&1; then
	echo "Expected failed recreation to fail deployment" >&2
	exit 1
fi
grep -Fq "restoring immichframe:rollback-" "$tmp_dir/rollback.out"
grep -Fq "docker tag 'immichframe:rollback-" "$tmp_dir/ssh.log"

: >"$tmp_dir/ssh.log"
if MOCK_CONTAINER=1 MOCK_INTERRUPT=1 run_deploy >"$tmp_dir/interrupted.out" 2>&1; then
	echo "Expected interrupted recreation to fail deployment" >&2
	exit 1
fi
grep -Fq "restoring immichframe:rollback-" "$tmp_dir/interrupted.out"
grep -Fq "docker tag 'immichframe:rollback-" "$tmp_dir/ssh.log"

echo "deploy-prod mocked regressions passed"
