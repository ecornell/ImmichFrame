export const AUTH_SECRET_PARAM = 'authsecret';

/**
 * The query string and hash of `href` without its `authsecret` parameter (other parameters,
 * such as `client`, are kept), or null when there is nothing to strip. The secret is saved to
 * localStorage on load; leaving it in the address bar exposes it to history and onlookers.
 */
export function searchWithoutAuthSecret(href: string): string | null {
	const url = new URL(href);
	if (!url.searchParams.has(AUTH_SECRET_PARAM)) return null;
	url.searchParams.delete(AUTH_SECRET_PARAM);
	return url.search + url.hash;
}
