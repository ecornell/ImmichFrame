export interface TimerClock {
	now(): number;
	setTimeout(callback: () => void, ms: number): unknown;
	clearTimeout(handle: unknown): void;
}

const defaultClock: TimerClock = {
	now: () => Date.now(),
	setTimeout: (callback, ms) => globalThis.setTimeout(callback, ms),
	clearTimeout: (handle) => globalThis.clearTimeout(handle as ReturnType<typeof setTimeout>)
};

/**
 * Times one slide: play, pause, restart and duration changes, firing `onDone` once when the
 * slide's time is used up. Every call returns synchronously; nothing here can leave a caller
 * awaiting a slide that was paused or replaced.
 *
 * Driven by a timer rather than by the progress bar's animation, so the slideshow advances on
 * time whether or not the bar is shown.
 */
export class SlideTimer {
	#clock: TimerClock;
	#onDone: () => void;
	#durationMs: number;
	#elapsedMs = 0;
	#startedAt: number | null = null;
	#handle: unknown = undefined;
	#done = false;

	constructor(durationSeconds: number, onDone: () => void, clock: TimerClock = defaultClock) {
		this.#clock = clock;
		this.#onDone = onDone;
		this.#durationMs = toMs(durationSeconds);
	}

	get playing(): boolean {
		return this.#startedAt !== null;
	}

	get done(): boolean {
		return this.#done;
	}

	get elapsedMs(): number {
		const running = this.#startedAt === null ? 0 : this.#clock.now() - this.#startedAt;
		return Math.min(this.#durationMs, this.#elapsedMs + running);
	}

	get durationMs(): number {
		return this.#durationMs;
	}

	play(): void {
		if (this.#done || this.#startedAt !== null) return;
		this.#startedAt = this.#clock.now();
		this.#schedule();
	}

	pause(): void {
		if (this.#startedAt === null) return;
		this.#elapsedMs = this.elapsedMs;
		this.#startedAt = null;
		this.#cancel();
	}

	/** Back to the start of the slide; plays again if `autoplay`. */
	restart(autoplay: boolean): void {
		this.#cancel();
		this.#startedAt = null;
		this.#elapsedMs = 0;
		this.#done = false;
		if (autoplay) this.play();
	}

	/** Changes the slide's length, keeping the time already spent on it. */
	setDuration(durationSeconds: number): void {
		const durationMs = toMs(durationSeconds);
		if (durationMs === this.#durationMs) return;
		if (this.#startedAt !== null) {
			this.#elapsedMs = this.elapsedMs;
			this.#startedAt = this.#clock.now();
		}
		this.#durationMs = durationMs;
		if (this.#startedAt !== null) this.#schedule();
	}

	dispose(): void {
		this.#cancel();
		this.#startedAt = null;
	}

	#schedule(): void {
		this.#cancel();
		const remaining = Math.max(0, this.#durationMs - this.elapsedMs);
		this.#handle = this.#clock.setTimeout(() => this.#finish(), remaining);
	}

	#cancel(): void {
		if (this.#handle !== undefined) this.#clock.clearTimeout(this.#handle);
		this.#handle = undefined;
	}

	#finish(): void {
		this.#handle = undefined;
		this.#elapsedMs = this.#durationMs;
		this.#startedAt = null;
		this.#done = true;
		this.#onDone();
	}
}

function toMs(seconds: number): number {
	return Number.isFinite(seconds) && seconds > 0 ? seconds * 1000 : 0;
}
