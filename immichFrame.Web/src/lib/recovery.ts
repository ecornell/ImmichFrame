export const CONFIG_RETRY_BASE_MS = 2000;
export const CONFIG_RETRY_MAX_MS = 30000;

export function retryDelay(attempt: number): number {
	return Math.min(CONFIG_RETRY_MAX_MS, CONFIG_RETRY_BASE_MS * 2 ** Math.max(0, attempt));
}

export function transitionIsCurrent(
	epoch: number,
	currentEpoch: number,
	signal: AbortSignal
): boolean {
	return epoch === currentEpoch && !signal.aborted;
}

export function assetPromiseKeysToRemove(
	promiseKeys: string[],
	visibleAssetIds: string[],
	backlogAssetIds: string[],
	outgoingAssetIds: string[]
): string[] {
	const retained = new Set([...visibleAssetIds, ...backlogAssetIds, ...outgoingAssetIds]);
	return promiseKeys.filter((key) => !retained.has(key));
}

export function fatalMediaErrorAfterCandidate(
	fatalMediaError: boolean,
	candidateLoaded: boolean
): boolean {
	return fatalMediaError && !candidateLoaded;
}

export async function withObjectUrlCleanup<T>(
	url: string,
	work: () => Promise<T>,
	revoke: (url: string) => void
): Promise<T> {
	try {
		return await work();
	} catch (error) {
		revoke(url);
		throw error;
	}
}

export class ConsecutiveMediaFailures {
	#count = 0;

	failed(): number {
		return ++this.#count;
	}

	succeeded(): void {
		this.#count = 0;
	}

	get count(): number {
		return this.#count;
	}
}
