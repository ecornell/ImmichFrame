#!/usr/bin/env bash

set -Eeuo pipefail

PROD_URL="${PROD_URL:-http://192.168.1.24:8080/}"
base_url="${PROD_URL%/}"
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

for command in curl python3; do
	command -v "$command" >/dev/null || {
		echo "Required command not found: $command" >&2
		exit 1
	}
done

curl_args=(--fail --silent --show-error --retry 5 --retry-delay 2 --max-time 10)
auth_args=()
if [[ -n "${AUTHENTICATION_SECRET:-}" ]]; then
	auth_args=(-H "Authorization: Bearer $AUTHENTICATION_SECRET")
fi

echo "Checking frame shell..."
curl "${curl_args[@]}" "$base_url/" -o "$tmp_dir/index.html"

app_path="$(sed -nE 's@.*import\("([^"]*/entry/app\.[^"]+\.js)"\).*@\1@p' "$tmp_dir/index.html" | head -n 1)"
if [[ -z "$app_path" ]]; then
	echo "Unable to locate the Svelte application entry point." >&2
	exit 1
fi

curl "${curl_args[@]}" "$base_url$app_path" -o "$tmp_dir/app.js"
if ! grep -Fq '"/settings":[' "$tmp_dir/app.js"; then
	echo "The deployed frontend manifest does not contain the /settings route." >&2
	exit 1
fi

echo "Checking settings page and API..."
curl "${curl_args[@]}" "$base_url/settings" -o /dev/null
curl "${curl_args[@]}" "${auth_args[@]}" \
	"$base_url/api/AdminSettings" -o "$tmp_dir/admin-settings.json"

python3 - "$tmp_dir/admin-settings.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as response:
    settings = json.load(response)

required = {"version", "canPersist", "warnings", "general", "accounts"}
missing = sorted(required - settings.keys())
if missing:
    raise SystemExit(f"Admin settings response is missing: {', '.join(missing)}")
if not settings["canPersist"]:
    raise SystemExit("The production settings file is not writable")
if settings["warnings"]:
    raise SystemExit("Admin settings reported warnings: " + "; ".join(settings["warnings"]))

print(f"Smoke checks passed: settings version {settings['version']}, "
      f"{len(settings['accounts'])} account(s), writable configuration")
PY
