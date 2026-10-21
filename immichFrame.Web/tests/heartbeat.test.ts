import assert from 'node:assert/strict';
import test from 'node:test';
import {
	claimAutoReload,
	heartbeatThresholdMs,
	LAST_AUTO_RELOAD_KEY,
	MIN_AUTO_RELOAD_SPACING_MS,
	SlideshowHeartbeat
} from '../src/lib/heartbeat.ts';

const running = { paused: false, hidden: false, authError: false };

function clock(start = 1_000_000) {
	const state = { now: start };
	return { state, now: () => state.now };
}

test('threshold is three slides plus a minute, using the slide on screen', () => {
	assert.equal(heartbeatThresholdMs(20), 120_000);
	// A 10-minute video gets a proportionally longer leash than the 20 s photo interval.
	assert.equal(heartbeatThresholdMs(600), 1_860_000);
	assert.equal(heartbeatThresholdMs(NaN), 60_000);
});

test('a slideshow that keeps finishing transitions is never wedged', () => {
	const { state, now } = clock();
	const heartbeat = new SlideshowHeartbeat(now);
	for (let slide = 0; slide < 100; slide++) {
		state.now += 20_000;
		heartbeat.beat();
		assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 20 }), false);
	}
});

test('a slideshow that stops finishing transitions is wedged after the threshold', () => {
	const { state, now } = clock();
	const heartbeat = new SlideshowHeartbeat(now);
	state.now += 120_000;
	assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 20 }), false);
	state.now += 1;
	assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 20 }), true);
	// The same silence is fine while a long video plays.
	assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 600 }), false);
});

test('an intentional pause never reloads, and resuming starts a fresh window', () => {
	const { state, now } = clock();
	const heartbeat = new SlideshowHeartbeat(now);
	for (let minute = 0; minute < 60; minute++) {
		state.now += 60_000;
		assert.equal(heartbeat.isWedged({ ...running, paused: true, slideDurationSeconds: 20 }), false);
	}
	state.now += 60_000;
	assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 20 }), false);
	state.now += 61_000;
	assert.equal(heartbeat.isWedged({ ...running, slideDurationSeconds: 20 }), true);
});

test('hidden tabs and auth errors do not count as wedged', () => {
	const { state, now } = clock();
	const heartbeat = new SlideshowHeartbeat(now);
	state.now += 10 * 60_000;
	assert.equal(heartbeat.isWedged({ ...running, hidden: true, slideDurationSeconds: 20 }), false);
	state.now += 10 * 60_000;
	assert.equal(
		heartbeat.isWedged({ ...running, authError: true, slideDurationSeconds: 20 }),
		false
	);
});

test('automatic reloads are spaced out so a persistent fault cannot loop', () => {
	const values = new Map<string, string>();
	const store = {
		getItem: (key: string) => values.get(key) ?? null,
		setItem: (key: string, value: string) => void values.set(key, value)
	};
	assert.equal(claimAutoReload(store, 1_000_000), 0);
	assert.equal(values.get(LAST_AUTO_RELOAD_KEY), '1000000');
	assert.equal(claimAutoReload(store, 1_000_000 + 60_000), MIN_AUTO_RELOAD_SPACING_MS - 60_000);
	assert.equal(claimAutoReload(store, 1_000_000 + MIN_AUTO_RELOAD_SPACING_MS), 0);

	values.set(LAST_AUTO_RELOAD_KEY, String(10 ** 15)); // stamp from a wrong clock
	assert.equal(claimAutoReload(store, 2_000_000), 0);
	assert.equal(claimAutoReload(undefined, 2_000_000), 0);
});
