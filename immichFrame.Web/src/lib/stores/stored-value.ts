/**
 * Decodes a value persisted as JSON. A corrupt or foreign value (hand-edited, written by an
 * older build) falls back to the default instead of throwing at module load, which used to
 * leave the frame on a blank page.
 */
export function parseStoredValue(raw: string | null | undefined, fallback: string | null) {
	if (!raw) return fallback;
	try {
		const parsed: unknown = JSON.parse(raw);
		return typeof parsed === 'string' || parsed === null ? parsed : fallback;
	} catch {
		return fallback;
	}
}
