import assert from 'node:assert/strict';
import test from 'node:test';
import { SlideTimer, type TimerClock } from '../src/lib/slide-timer.ts';

/** A manual clock: time moves only when the test advances it. */
function fakeClock() {
	let now = 0;
	let nextId = 1;
	const timers = new Map<number, { at: number; callback: () => void }>();
	const clock: TimerClock = {
		now: () => now,
		setTimeout: (callback, ms) => {
			const id = nextId++;
			timers.set(id, { at: now + ms, callback });
			return id;
		},
		clearTimeout: (handle) => void timers.delete(handle as number)
	};
	const advance = (ms: number) => {
		const target = now + ms;
		for (;;) {
			const due = [...timers.entries()]
				.filter(([, timer]) => timer.at <= target)
				.sort((a, b) => a[1].at - b[1].at)[0];
			if (!due) break;
			timers.delete(due[0]);
			now = due[1].at;
			due[1].callback();
		}
		now = target;
	};
	return { clock, advance, pending: () => timers.size };
}

test('play returns at once and the slide ends after its duration', () => {
	const { clock, advance } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(20, () => done++, clock);

	// Regression: play() used to return a promise that settled only when the slide ended, and a
	// transition awaiting it held the slideshow's lock for the whole slide.
	assert.equal(timer.play(), undefined);
	advance(19_999);
	assert.equal(done, 0);
	advance(1);
	assert.equal(done, 1);
	advance(60_000);
	assert.equal(done, 1, 'fires once per slide');
});

test('pause and resume do not wedge: the remaining time still runs out', () => {
	const { clock, advance, pending } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(20, () => done++, clock);

	timer.play();
	advance(5_000);
	timer.pause();
	assert.equal(pending(), 0, 'a paused slide holds no timer');
	advance(10 * 60_000);
	assert.equal(done, 0, 'a long pause never ends the slide');
	assert.equal(timer.elapsedMs, 5_000);

	timer.play();
	advance(14_999);
	assert.equal(done, 0);
	advance(1);
	assert.equal(done, 1);
});

test('repeated pause/play and restart keep exactly one pending timer', () => {
	const { clock, advance, pending } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(10, () => done++, clock);
	for (let i = 0; i < 5; i++) {
		timer.play();
		timer.play();
		advance(1_000);
		timer.pause();
		timer.pause();
	}
	assert.equal(timer.elapsedMs, 5_000);
	timer.restart(true);
	assert.equal(pending(), 1);
	advance(10_000);
	assert.equal(done, 1);

	timer.restart(true);
	advance(10_000);
	assert.equal(done, 2, 'a restarted slide can finish again');
});

test('restart without autoplay waits for play', () => {
	const { clock, advance } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(5, () => done++, clock);
	timer.play();
	advance(4_000);
	timer.restart(false);
	advance(60_000);
	assert.equal(done, 0);
	timer.play();
	advance(5_000);
	assert.equal(done, 1);
});

test('a duration change keeps the time already spent on the slide', () => {
	const { clock, advance } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(20, () => done++, clock);
	timer.play();
	advance(5_000);
	timer.setDuration(60); // e.g. the slide turned out to be a one-minute video
	advance(54_999);
	assert.equal(done, 0);
	advance(1);
	assert.equal(done, 1);

	const shorter = new SlideTimer(20, () => done++, clock);
	shorter.play();
	advance(15_000);
	shorter.setDuration(10); // already past the new length: ends now
	advance(0);
	assert.equal(done, 2);
});

test('dispose cancels the pending slide', () => {
	const { clock, advance } = fakeClock();
	let done = 0;
	const timer = new SlideTimer(1, () => done++, clock);
	timer.play();
	timer.dispose();
	advance(5_000);
	assert.equal(done, 0);
});
