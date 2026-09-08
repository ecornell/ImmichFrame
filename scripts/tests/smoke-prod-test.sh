#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp_dir="$(mktemp -d)"
server_pid=""
cleanup() {
	[[ -z "$server_pid" ]] || kill "$server_pid" >/dev/null 2>&1 || true
	rm -rf "$tmp_dir"
}
trap cleanup EXIT

cat >"$tmp_dir/server.py" <<'PY'
from http.server import BaseHTTPRequestHandler, HTTPServer
import json, os

class Handler(BaseHTTPRequestHandler):
    asset_requests = 0

    def do_GET(self):
        if self.path == "/":
            return self.send(200, "text/html", b'<script>import("/_app/entry/app.test.js")</script>')
        if self.path == "/_app/entry/app.test.js":
            return self.send(200, "application/javascript", b'const routes={"/settings":[]}')
        if self.path == "/settings":
            return self.send(200, "text/html", b"settings")
        if self.path == "/api/AdminSettings":
            body = {"version": 1, "canPersist": True, "warnings": [], "general": {}, "accounts": [{}]}
            return self.send(200, "application/json", json.dumps(body).encode())
        if self.path == "/api/Asset":
            Handler.asset_requests += 1
            if os.environ.get("VIDEO_FIRST") == "1" and Handler.asset_requests == 1:
                return self.send(200, "application/json", b'[{"id":"22222222-2222-2222-2222-222222222222","type":1}]')
            return self.send(200, "application/json", b'[{"id":"11111111-1111-1111-1111-111111111111","type":0}]')
        if self.path.startswith("/api/Asset/11111111-1111-1111-1111-111111111111/Asset"):
            if os.environ.get("BROKEN_IMAGE") == "1":
                return self.send(502, "text/plain", b"upstream failed")
            return self.send(200, "image/jpeg", b"\xff\xd8\xff\xd9")
        return self.send(404, "text/plain", b"not found")

    def send(self, status, content_type, body):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass

HTTPServer(("127.0.0.1", int(os.environ["PORT"])), Handler).serve_forever()
PY

start_server() {
	local broken="$1"
	local video_first="${3:-0}"
	PORT="$2" BROKEN_IMAGE="$broken" VIDEO_FIRST="$video_first" python3 "$tmp_dir/server.py" &
	server_pid=$!
	for _ in {1..20}; do
		curl -fsS "http://127.0.0.1:$2/" >/dev/null 2>&1 && return
		sleep 0.05
	done
	return 1
}

start_server 0 18765
PROD_URL=http://127.0.0.1:18765/ "$repo_root/scripts/smoke-prod.sh" >"$tmp_dir/healthy.log"
grep -Fq "downloaded a nonempty image/jpeg" "$tmp_dir/healthy.log"
kill "$server_pid"; wait "$server_pid" 2>/dev/null || true; server_pid=""

start_server 1 18766
if PROD_URL=http://127.0.0.1:18766/ "$repo_root/scripts/smoke-prod.sh" >"$tmp_dir/broken.log" 2>&1; then
	echo "Expected smoke check to reject a failed asset download" >&2
	exit 1
fi
kill "$server_pid"; wait "$server_pid" 2>/dev/null || true; server_pid=""

start_server 0 18767 1
PROD_URL=http://127.0.0.1:18767/ "$repo_root/scripts/smoke-prod.sh" >"$tmp_dir/video-first.log"
grep -Fq "selected image 11111111-1111-1111-1111-111111111111" "$tmp_dir/video-first.log"
kill "$server_pid"; wait "$server_pid" 2>/dev/null || true; server_pid=""

real_curl="$(command -v curl)"
mkdir -p "$tmp_dir/bin"
cat >"$tmp_dir/bin/curl" <<SH
#!/usr/bin/env bash
printf '%s\\n' "\$*" >>"\$CURL_ARGS_LOG"
if [[ -n "\${AUTHENTICATION_SECRET:-}" ]]; then
	echo "secret leaked into curl environment" >&2
	exit 89
fi
if [[ "\$*" == *"\$LEAK_SECRET"* ]]; then
	echo "secret leaked into curl arguments" >&2
	exit 90
fi
for ((i = 1; i <= \$#; i++)); do
	if [[ "\${!i}" == "--config" ]]; then
		j=\$((i + 1))
		[[ "\$(stat -c %a "\${!j}")" == 600 ]] || exit 91
	fi
done
exec "$real_curl" "\$@"
SH
chmod +x "$tmp_dir/bin/curl"
start_server 0 18768
CURL_ARGS_LOG="$tmp_dir/curl-args.log" LEAK_SECRET='not-on-command-line' \
	AUTHENTICATION_SECRET='not-on-command-line' PATH="$tmp_dir/bin:$PATH" \
	PROD_URL=http://127.0.0.1:18768/ \
	"$repo_root/scripts/smoke-prod.sh" >"$tmp_dir/auth.log"
grep -Fq -- "--config " "$tmp_dir/curl-args.log"
if grep -Fq "not-on-command-line" "$tmp_dir/curl-args.log"; then
	echo "Authentication secret appeared in curl arguments" >&2
	exit 1
fi

echo "smoke-prod mocked regressions passed"
