import * as api from '$lib/index';
import type { ClientSettingsDto } from '$lib/immichFrameApi';
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

export async function fetchClientConfig(signal?: AbortSignal): Promise<ClientSettingsDto> {
	const response = await api.getConfig(
		{ clientIdentifier: get(clientIdentifierStore) },
		{ signal }
	);

	const status = (response as { status: number }).status;
	if (status !== 200) throw new ConfigLoadError(status);

	return response.data;
}

export async function loadClientConfig(signal?: AbortSignal): Promise<void> {
	configStore.ps(await fetchClientConfig(signal));
}

/**
 * Whether the server's configuration differs from the one this page was started with. Both
 * come from the same serializer, so comparing the JSON is enough.
 */
export function configChanged(current: ClientSettingsDto, latest: ClientSettingsDto): boolean {
	return JSON.stringify(current) !== JSON.stringify(latest);
}
