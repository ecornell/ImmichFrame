import assert from 'node:assert/strict';
import test from 'node:test';
import {
	afterFailedPick,
	AssetFailureTracker,
	AssetLoadError,
	classifyAssetError
} from '../src/lib/asset-failures.ts';

test('client errors mean the asset is gone; everything else is worth retrying', () => {
	assert.equal(classifyAssetError(new AssetLoadError('a', 404)), 'missing');
	assert.equal(classifyAssetError(new AssetLoadError('a', 400)), 'missing');
	assert.equal(classifyAssetError(new AssetLoadError('a', 403)), 'missing');
	assert.equal(classifyAssetError(new AssetLoadError('a', 401)), 'auth');
	assert.equal(classifyAssetError(new AssetLoadError('a', 408)), 'transient');
	assert.equal(classifyAssetError(new AssetLoadError('a', 429)), 'transient');
	assert.equal(classifyAssetError(new AssetLoadError('a', 500)), 'transient');
	assert.equal(classifyAssetError(new AssetLoadError('a', 503)), 'transient');
	assert.equal(classifyAssetError(new TypeError('Failed to fetch')), 'transient');
	assert.equal(classifyAssetError(new DOMException('timed out', 'AbortError')), 'transient');
});

test('a missing asset is skipped at once and stays skipped', () => {
	const failures = new AssetFailureTracker();
	assert.equal(failures.shouldSkip('gone'), false);
	failures.failed('gone', new AssetLoadError('gone', 404));
	assert.equal(failures.shouldSkip('gone'), true);
	failures.skipped('gone');
	// The server may offer it again in a later sample; it must not stall the frame again.
	assert.equal(failures.shouldSkip('gone'), true);
});

test('a transiently failing asset is skipped after repeated failures, then forgiven', () => {
	const failures = new AssetFailureTracker(3);
	const timeout = new DOMException('timed out', 'AbortError');
	failures.failed('slow', timeout);
	failures.failed('slow', timeout);
	assert.equal(failures.shouldSkip('slow'), false);
	failures.failed('slow', timeout);
	assert.equal(failures.shouldSkip('slow'), true);

	failures.skipped('slow');
	assert.equal(failures.shouldSkip('slow'), false);
});

test('success clears the count, and auth failures never skip', () => {
	const failures = new AssetFailureTracker(2);
	failures.failed('flaky', new AssetLoadError('flaky', 502));
	failures.succeeded('flaky');
	failures.failed('flaky', new AssetLoadError('flaky', 502));
	assert.equal(failures.shouldSkip('flaky'), false);

	for (let i = 0; i < 5; i++) failures.failed('locked', new AssetLoadError('locked', 401));
	assert.equal(failures.shouldSkip('locked'), false);
	assert.equal(failures.lastKind('locked'), 'auth');
});

test('a 404 asset is skipped past; a server outage keeps the last photo and retries', () => {
	const failures = new AssetFailureTracker(3);

	failures.failed('deleted', new AssetLoadError('deleted', 404));
	assert.deepEqual(afterFailedPick(['deleted'], failures, 0, 3), {
		action: 'skip',
		authError: false
	});
	// Bounded: a library full of broken assets still ends the transition.
	assert.equal(afterFailedPick(['deleted'], failures, 3, 3).action, 'offline');

	// Server down: offline, and the recovery loop retries the same asset...
	failures.failed('photo', new TypeError('Failed to fetch'));
	assert.equal(afterFailedPick(['photo'], failures, 0, 3).action, 'offline');
	// ...which loads once the server is back, clearing its record.
	failures.succeeded('photo');
	assert.equal(failures.shouldSkip('photo'), false);

	failures.failed('secret', new AssetLoadError('secret', 401));
	assert.deepEqual(afterFailedPick(['secret'], failures, 0, 3), {
		action: 'offline',
		authError: true
	});
});
