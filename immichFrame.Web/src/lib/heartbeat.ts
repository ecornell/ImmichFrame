/** Slack on top of three slide lengths, covering slow downloads and the recovery backoff. */
export const HEARTBEAT_GRACE_MS = 60_000;
/** How often the heartbeat is checked. */
export const HEARTBEAT_CHECK_MS = 30_000;
/** A fault that survives reloads must not turn into a reload loop. */
export const MIN_AUTO_RELOAD_SPACING_MS = 10 * 60_000;
export const LAST_AUTO_RELOAD_KEY = 'immichframe:last-auto-reload';

/** How long the slideshow may go without finishing a transition before it counts as wedged. */
export function heartbeatThresholdMs(slideDurationSeconds: number): number {
	const seconds =
		Number.isFinite(slideDurationSeconds) && slideDurationSeconds > 0 ? slideDurationSeconds : 0;
	return 3 * seconds * 1000 + HEARTBEAT_GRACE_MS;
}

export interface HeartbeatConditions {
	/** Length of the slide on screen (a video's own duration), in seconds. */
	slideDurationSeconds: number;
	/** Paused on purpose (pause button, info overlay) with no transition in flight. */
	paused: boolean;
	/** Background tab: timers and animations are throttled, so silence means nothing. */
	hidden: boolean;
	/** Waiting for a new secret; a reload cannot fix that. */
	authError: boolean;
}

/**
 * Last-resort liveness check. Every finished transition counts as a beat, successful or not:
 * an offline frame keeps retrying (and beating) with the last photo on screen, which a reload
 * would throw away. Only a slideshow that stops finishing transitions at all is wedged.
 */
export class SlideshowHeartbeat {
	#now: () => number;
	#lastBeat: number;

	constructor(now: () => number = Date.now) {
		this.#now = now;
		this.#lastBeat = now();
	}

	beat(): void {
		this.#lastBeat = this.#now();
	}

	/** Returns true when the page should reload. Quiet-by-design states restart the clock. */
	isWedged(conditions: HeartbeatConditions): boolean {
		if (conditions.paused || conditions.hidden || conditions.authError) {
			this.beat();
			return false;
		}
		return this.#now() - this.#lastBeat > heartbeatThresholdMs(conditions.slideDurationSeconds);
	}
}

/** Minimal slice of Storage, so tests can pass a plain object. */
export interface ReloadStore {
	getItem(key: string): string | null;
	setItem(key: string, value: string): void;
}

/**
 * Records and permits an automatic reload unless one happened recently. Returns how long to
 * wait before a reload is allowed (0 = reload now).
 */
export function claimAutoReload(store: ReloadStore | undefined, now: number): number {
	let last = NaN;
	try {
		last = Number(store?.getItem(LAST_AUTO_RELOAD_KEY));
	} catch {
		// Storage unavailable: fall through and allow the reload.
	}
	const wait = Number.isFinite(last) && last > 0 ? last + MIN_AUTO_RELOAD_SPACING_MS - now : 0;
	// A stamp from the future (clock changed) is ignored rather than blocking reloads for good.
	if (wait > 0 && wait <= MIN_AUTO_RELOAD_SPACING_MS) return wait;
	try {
		store?.setItem(LAST_AUTO_RELOAD_KEY, String(now));
	} catch {
		// Best effort; without storage the spacing guard simply does not apply.
	}
	return 0;
}
