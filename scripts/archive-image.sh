#!/usr/bin/env bash
# Creates or restores an off-host-copyable image archive. Nothing is uploaded automatically.
set -Eeuo pipefail

usage() {
	echo "Usage: $0 save IMAGE OUTPUT.tar.gz | restore OUTPUT.tar.gz" >&2
	exit 2
}

case "${1:-}" in
	save)
		[[ $# == 3 ]] || usage
		image="$2"
		output="$3"
		docker image inspect "$image" >/dev/null
		mkdir -p "$(dirname "$output")"
		tmp="$output.tmp"
		trap 'rm -f "$tmp"' EXIT
		docker image save "$image" | gzip -9 >"$tmp"
		mv "$tmp" "$output"
		(
			cd "$(dirname "$output")"
			sha256sum "$(basename "$output")"
		) >"$output.sha256"
		echo "Saved $image to $output; copy the archive and checksum off-host."
		;;
	restore)
		[[ $# == 2 ]] || usage
		archive="$2"
		[[ -f "$archive" ]] || { echo "Archive not found: $archive" >&2; exit 1; }
		[[ -f "$archive.sha256" ]] || { echo "Checksum not found: $archive.sha256" >&2; exit 1; }
		(
			cd "$(dirname "$archive")"
			sha256sum -c "$(basename "$archive").sha256"
		)
		gzip -dc "$archive" | docker image load
		;;
	*) usage ;;
esac
