import assert from 'node:assert/strict';
import test from 'node:test';
import { oldestRepeat } from '../src/lib/asset-selection.ts';

test('full album history does not queue recently shown photos behind the oldest repeat', () => {
	const history = Array.from({ length: 332 }, (_, i) => String(i));
	const candidates = ['331', '330', '0', '1'];
	assert.deepEqual(
		oldestRepeat(candidates, history, (id) => id),
		['0']
	);
	assert.deepEqual(candidates, ['331', '330', '0', '1']);
	const updated = [...history.slice(1), '0'];
	assert.deepEqual(
		oldestRepeat(candidates, updated, (id) => id),
		['1']
	);
});

test('empty and single-photo albums remain safe', () => {
	assert.deepEqual(
		oldestRepeat<string>([], [], (id) => id),
		[]
	);
	assert.deepEqual(
		oldestRepeat(['only'], ['only'], (id) => id),
		['only']
	);
});
