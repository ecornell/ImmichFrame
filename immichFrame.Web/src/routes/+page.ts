import * as api from '$lib/index';
import { ConfigLoadError, loadClientConfig } from '$lib/config-loader';
import { authSecretStore, clientIdentifierStore } from '$lib/stores/persist.store';
import { get } from 'svelte/store';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ url }) => {
	const clientParam = url.searchParams.get('client');
	if (clientParam) clientIdentifierStore.set(clientParam);

	const authSecret = url.searchParams.get('authsecret');
	if (authSecret && authSecret !== get(authSecretStore)) {
		authSecretStore.set(authSecret);
		api.init();
	}

	try {
		await loadClientConfig();
		return { configLoaded: true };
	} catch (error) {
		// Keep the route mounted so the page can recover without a document reload.
		return {
			configLoaded: false,
			configAuthError: error instanceof ConfigLoadError && error.status === 401
		};
	}
};
