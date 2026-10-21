import { writable } from 'svelte/store';
import { parseStoredValue } from './stored-value';

function readStorage(key: string): string | null {
	try {
		return localStorage?.getItem(key) ?? null;
	} catch {
		return null; // storage disabled or blocked
	}
}

function persistStore(key: string, defaultValue: string | null) {
	const initialValue = parseStoredValue(readStorage(key), defaultValue) as string;

	const store = writable(initialValue);

	store.subscribe((value) => {
		try {
			localStorage?.setItem(key, JSON.stringify(value));
		} catch {
			// Storage full or blocked: the value still lives in memory for this session.
		}
	});

	return store;
}

function generateGUID() {
	return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
		const r = (Math.random() * 16) | 0,
			v = c === 'x' ? r : (r & 0x3) | 0x8;
		return v.toString(16);
	});
}

export const clientIdentifierStore = persistStore('clientIdentifier', generateGUID());
export const authSecretStore = persistStore('authSecret', null);
