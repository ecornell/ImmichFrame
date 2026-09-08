import * as api from '$lib/index';
import { configStore } from '$lib/stores/config.store';
import { clientIdentifierStore } from '$lib/stores/persist.store';
import { get } from 'svelte/store';

export class ConfigLoadError extends Error {
	constructor(public readonly status?: number) {
		super(
			status ? `Configuration request failed with status ${status}` : 'Configuration request failed'
		);
	}
}

export async function loadClientConfig(signal?: AbortSignal): Promise<void> {
	const response = await api.getConfig(
		{ clientIdentifier: get(clientIdentifierStore) },
		{ signal }
	);

	const status = (response as { status: number }).status;
	if (status !== 200) throw new ConfigLoadError(status);

	configStore.ps(response.data);
}
