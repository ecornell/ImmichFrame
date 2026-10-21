import assert from 'node:assert/strict';
import test from 'node:test';
import { parseStoredValue } from '../src/lib/stores/stored-value.ts';

test('stored JSON strings round-trip', () => {
	assert.equal(parseStoredValue(JSON.stringify('kitchen'), 'fallback'), 'kitchen');
	assert.equal(parseStoredValue('null', 'fallback'), null);
});

test('missing, corrupt or foreign values fall back instead of throwing', () => {
	assert.equal(parseStoredValue(null, 'fallback'), 'fallback');
	assert.equal(parseStoredValue(undefined, 'fallback'), 'fallback');
	assert.equal(parseStoredValue('', 'fallback'), 'fallback');
	assert.equal(parseStoredValue('kitchen', 'fallback'), 'fallback'); // raw, not JSON
	assert.equal(parseStoredValue('{"broken"', 'fallback'), 'fallback');
	assert.equal(parseStoredValue('{"a":1}', null), null);
	assert.equal(parseStoredValue('42', 'fallback'), 'fallback');
});
