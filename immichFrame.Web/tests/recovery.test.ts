import assert from 'node:assert/strict';
import test from 'node:test';
import {
	assetPromiseKeysToRemove,
	ConsecutiveMediaFailures,
	fatalMediaErrorAfterCandidate,
	retryDelay,
	transitionIsCurrent,
	untilAborted,
	WATCHDOG_ABORT,
	withObjectUrlCleanup
} from '../src/lib/recovery.ts';

test('configuration retries use bounded exponential backoff', () => {
	assert.deepEqual([0, 1, 2, 3, 10].map(retryDelay), [2000, 4000, 8000, 16000, 30000]);
});

test('aborted and stale transitions cannot commit state', () => {
	const controller = new AbortController();
	assert.equal(transitionIsCurrent(4, 4, controller.signal), true);
	assert.equal(transitionIsCurrent(4, 5, controller.signal), false);
	controller.abort();
	assert.equal(transitionIsCurrent(4, 4, controller.signal), false);
});

test('forward, forward, back retains the requested historical asset promise', () => {
	const afterFirstForward = assetPromiseKeysToRemove(
		['first', 'second', 'third'],
		['second'],
		['third'],
		['first']
	);
	assert.deepEqual(afterFirstForward, []);

	const afterSecondForward = assetPromiseKeysToRemove(
		['first', 'second', 'third', 'fourth'],
		['third'],
		['fourth'],
		['second']
	);
	assert.deepEqual(afterSecondForward, ['first']);

	const afterBack = assetPromiseKeysToRemove(
		['second', 'third', 'fourth'],
		['second'],
		['third', 'fourth'],
		['third']
	);
	assert.deepEqual(afterBack, []);
});

test('fatal media gate reopens for a recovery candidate without resetting failures', () => {
	const failures = new ConsecutiveMediaFailures();
	for (let count = 1; count <= 11; count++) assert.equal(failures.failed(), count);

	assert.equal(fatalMediaErrorAfterCandidate(true, true), false);
	assert.equal(failures.count, 11);
	failures.succeeded();
	assert.equal(failures.count, 0);
});

test('abort after image fetch revokes its object URL', async () => {
	const controller = new AbortController();
	const revoked: string[] = [];
	controller.abort();

	await assert.rejects(
		withObjectUrlCleanup(
			'blob:image',
			async () => {
				throw new DOMException('aborted', 'AbortError');
			},
			(url) => revoked.push(url)
		),
		{ name: 'AbortError' }
	);
	assert.deepEqual(revoked, ['blob:image']);
});

test('a watchdog abort ends the wait but leaves the download reusable', async () => {
	let finish!: (value: string) => void;
	const download = new Promise<string>((resolve) => (finish = resolve));
	const transition = new AbortController();

	const waiting = untilAborted(download, transition.signal);
	transition.abort(WATCHDOG_ABORT);
	await assert.rejects(waiting, { name: 'AbortError' });

	// The retry awaits the same in-flight download instead of starting over.
	const retry = new AbortController();
	const retried = untilAborted(download, retry.signal);
	finish('blob:photo');
	assert.equal(await retried, 'blob:photo');
});

test('untilAborted passes results and failures through', async () => {
	const signal = new AbortController().signal;
	assert.equal(await untilAborted(Promise.resolve(1), signal), 1);
	await assert.rejects(untilAborted(Promise.reject(new Error('boom')), signal), /boom/);
	assert.equal(await untilAborted(Promise.resolve(2)), 2);

	const aborted = new AbortController();
	aborted.abort();
	await assert.rejects(untilAborted(Promise.resolve(3), aborted.signal), { name: 'AbortError' });
});
