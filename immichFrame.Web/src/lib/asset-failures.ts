/** Thrown when the server answers an asset request with a non-success status. */
export class AssetLoadError extends Error {
	readonly assetId: string;
	readonly status: number;

	constructor(assetId: string, status: number) {
		super(`Failed to load asset ${assetId}: status ${status}`);
		this.name = 'AssetLoadError';
		this.assetId = assetId;
		this.status = status;
	}
}

/**
 * - `auth`: the secret was rejected; skipping to another asset would fail the same way.
 * - `missing`: the server will never serve this asset (deleted, filtered, 404); skip it now.
 * - `transient`: network error, timeout, 5xx or rate limit; retry it a few times first.
 */
export type AssetFailureKind = 'auth' | 'missing' | 'transient';

export function classifyAssetError(error: unknown): AssetFailureKind {
	if (error instanceof AssetLoadError) {
		if (error.status === 401) return 'auth';
		// 408 and 429 mean "try again later", not "this asset is gone".
		if (error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
			return 'missing';
		}
	}
	return 'transient';
}

/** A transient failure this many times in a row skips the asset for now. */
export const MAX_TRANSIENT_ASSET_FAILURES = 3;
/** Bounds the memory of permanently skipped assets on a frame that runs for weeks. */
const MAX_TRACKED_ASSETS = 1000;

/**
 * Remembers which assets failed to load, so one bad asset is skipped instead of being retried
 * forever while the frame reports itself offline.
 */
export class AssetFailureTracker {
	#failures = new Map<string, { kind: AssetFailureKind; count: number }>();
	#maxTransient: number;

	constructor(maxTransient = MAX_TRANSIENT_ASSET_FAILURES) {
		this.#maxTransient = maxTransient;
	}

	failed(assetId: string, error: unknown): AssetFailureKind {
		const kind = classifyAssetError(error);
		const previous = this.#failures.get(assetId);
		this.#failures.delete(assetId); // re-insert so the map stays ordered oldest-first
		this.#failures.set(assetId, {
			kind,
			count: kind === previous?.kind ? previous.count + 1 : 1
		});
		while (this.#failures.size > MAX_TRACKED_ASSETS) {
			this.#failures.delete(this.#failures.keys().next().value!);
		}
		return kind;
	}

	succeeded(assetId: string): void {
		this.#failures.delete(assetId);
	}

	shouldSkip(assetId: string): boolean {
		const failure = this.#failures.get(assetId);
		if (!failure) return false;
		return (
			failure.kind === 'missing' ||
			(failure.kind === 'transient' && failure.count >= this.#maxTransient)
		);
	}

	/**
	 * Called once an asset has been dropped from the queue. A missing asset stays skipped if the
	 * server offers it again; a transiently failing one gets a fresh start, so a long outage on
	 * a small library does not end up skipping every photo it has.
	 */
	skipped(assetId: string): void {
		if (this.#failures.get(assetId)?.kind === 'transient') this.#failures.delete(assetId);
	}

	lastKind(assetId: string): AssetFailureKind | undefined {
		return this.#failures.get(assetId)?.kind;
	}
}

/**
 * What a transition does after the assets it was about to show failed to load: skip past them
 * if the tracker says they are broken (bounded per transition), otherwise keep the last photo
 * on screen and report the frame offline so the recovery loop retries.
 */
export function afterFailedPick(
	assetIds: string[],
	failures: AssetFailureTracker,
	skipsSoFar: number,
	maxSkips: number
): { action: 'skip' | 'offline'; authError: boolean } {
	const authError = assetIds.some((id) => failures.lastKind(id) === 'auth');
	const skippable = assetIds.some((id) => failures.shouldSkip(id));
	return { action: skippable && skipsSoFar < maxSkips ? 'skip' : 'offline', authError };
}
