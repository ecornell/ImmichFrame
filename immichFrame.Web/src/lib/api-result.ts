/**
 * The generated client returns `{ status, data }` without throwing on HTTP errors, so a failed
 * request's `data` is a ProblemDetails object or an error string. For optional decorations
 * (faces, albums, people) treat anything but a 200 with an array as "none".
 */
export function okArray<T>(response: { status: number; data?: unknown }): T[] {
	return response.status === 200 && Array.isArray(response.data) ? (response.data as T[]) : [];
}
