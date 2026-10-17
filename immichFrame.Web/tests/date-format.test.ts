import assert from 'node:assert/strict';
import test from 'node:test';
import { safeFormat } from '../src/lib/date-format.ts';

const date = new Date(2026, 4, 3, 14, 5, 9);

test('valid patterns are used as given', () => {
	assert.equal(safeFormat(date, 'HH:mm', 'HH:mm:ss'), '14:05');
	assert.equal(safeFormat(date, "HH:mm 'Uhr'", 'HH:mm:ss'), '14:05 Uhr');
});

test('invalid patterns fall back instead of throwing', () => {
	assert.equal(safeFormat(date, 'HH:mm Uhr', 'HH:mm:ss'), '14:05:09');
	assert.equal(safeFormat(date, 'YYYY-MM-DD', 'dd.MM.yyyy'), '03.05.2026');
});

test('missing patterns use the fallback and invalid dates render empty', () => {
	assert.equal(safeFormat(date, null, 'dd.MM.yyyy'), '03.05.2026');
	assert.equal(safeFormat(date, '', 'dd.MM.yyyy'), '03.05.2026');
	assert.equal(safeFormat(new Date('nope'), 'HH:mm', 'HH:mm:ss'), '');
});
