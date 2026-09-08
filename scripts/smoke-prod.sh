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
	if [[ "$AUTHENTICATION_SECRET" == *$'\n'* || "$AUTHENTICATION_SECRET" == *$'\r'* ]]; then
		echo "AUTHENTICATION_SECRET must not contain newlines." >&2
		exit 1
	fi
	auth_config="$tmp_dir/auth.curlrc"
	escaped_secret="${AUTHENTICATION_SECRET//\\/\\\\}"
	escaped_secret="${escaped_secret//\"/\\\"}"
	(umask 077; printf 'header = "Authorization: Bearer %s"\n' "$escaped_secret" >"$auth_config")
	auth_args=(--config "$auth_config")
	unset AUTHENTICATION_SECRET escaped_secret
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

print(f"Settings check passed: version {settings['version']}, "
      f"{len(settings['accounts'])} account(s), writable configuration")
PY

echo "Checking asset selection and image proxy..."
asset_id=""
asset_type=""
for attempt in $(seq 1 "${ASSET_BATCH_ATTEMPTS:-5}"); do
	assets_path="$tmp_dir/assets-$attempt.json"
	curl "${curl_args[@]}" "${auth_args[@]}" \
		"$base_url/api/Asset" -o "$assets_path"

	candidate="$(python3 - "$assets_path" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as response:
    assets = json.load(response)
if not isinstance(assets, list):
    raise SystemExit("Asset response is not a list")
image = next((asset for asset in assets if asset.get("type") == 0 and asset.get("id")), None)
if image is not None:
    print(image["id"], image["type"])
PY
)"
	if [[ -n "$candidate" ]]; then
		read -r asset_id asset_type <<<"$candidate"
		break
	fi
done

if [[ -z "$asset_id" ]]; then
	echo "Asset responses contained no image to smoke test after ${ASSET_BATCH_ATTEMPTS:-5} attempts." >&2
	exit 1
fi

curl "${curl_args[@]}" "${auth_args[@]}" -D "$tmp_dir/image.headers" \
	"$base_url/api/Asset/$asset_id/Asset?assetType=$asset_type" -o "$tmp_dir/image"

if [[ ! -s "$tmp_dir/image" ]]; then
	echo "Asset image response was empty." >&2
	exit 1
fi

content_type="$(sed -nE 's/^[Cc]ontent-[Tt]ype:[[:space:]]*([^;[:space:]]+).*/\1/p' "$tmp_dir/image.headers" | tr -d '\r' | tail -n 1)"
case "$content_type" in
	image/jpeg|image/webp) ;;
	*)
		echo "Asset image returned unsupported Content-Type '$content_type'." >&2
		exit 1
		;;
esac

echo "Smoke checks passed: selected image $asset_id and downloaded a nonempty $content_type response."
