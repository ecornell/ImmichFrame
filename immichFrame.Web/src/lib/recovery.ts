export const CONFIG_RETRY_BASE_MS = 2000;
export const CONFIG_RETRY_MAX_MS = 30000;

export function retryDelay(attempt: number): number {
	return Math.min(CONFIG_RETRY_MAX_MS, CONFIG_RETRY_BASE_MS * 2 ** Math.max(0, attempt));
}

/** A rejected secret is retried slowly, so restoring it on the server recovers the frame. */
export const AUTH_RETRY_MS = 5 * 60 * 1000;

export interface RecoveryState {
	failing: boolean;
	authError: boolean;
	transitionInFlight: boolean;
	now: number;
	nextRecoveryAt: number;
	nextAuthRetryAt: number;
}

/** Whether the recovery loop should start a transition now. */
export function shouldAttemptRecovery(state: RecoveryState): boolean {
	if (!state.failing || state.transitionInFlight) return false;
	return state.authError ? state.now >= state.nextAuthRetryAt : state.now >= state.nextRecoveryAt;
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

/** Abort reason used by the transition watchdog, so in-flight downloads can tell it apart. */
export const WATCHDOG_ABORT = 'transition-watchdog';

/**
 * Waits for `promise`, but gives up with an AbortError as soon as `signal` aborts — without
 * cancelling the underlying work, which keeps running and can be awaited again later.
 */
export function untilAborted<T>(promise: Promise<T>, signal?: AbortSignal): Promise<T> {
	if (!signal) return promise;

	const aborted = () => new DOMException('The transition was aborted.', 'AbortError');
	if (signal.aborted) return Promise.reject(aborted());

	return new Promise<T>((resolve, reject) => {
		const onAbort = () => reject(aborted());
		signal.addEventListener('abort', onAbort, { once: true });
		promise.then(
			(value) => {
				signal.removeEventListener('abort', onAbort);
				resolve(value);
			},
			(error) => {
				signal.removeEventListener('abort', onAbort);
				reject(error);
			}
		);
	});
}
