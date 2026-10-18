import assert from 'node:assert/strict';
import test from 'node:test';
import { isBirthdayToday } from '../src/lib/birthday.ts';

test('matches the written month and day in local time', () => {
	const today = new Date(2026, 4, 3, 23, 30); // 3 May, late evening local time
	assert.equal(isBirthdayToday('1990-05-03', today), true);
	assert.equal(isBirthdayToday('1990-05-03T00:00:00+00:00', today), true);
	assert.equal(isBirthdayToday('1990-05-02', today), false);
	assert.equal(isBirthdayToday('1990-05-04', today), false);
	assert.equal(isBirthdayToday('1990-06-03', today), false);
});

test('missing or malformed dates are never birthdays', () => {
	const today = new Date(2026, 4, 3);
	for (const value of [null, undefined, '', 'May 3', '05-03']) {
		assert.equal(isBirthdayToday(value, today), false);
	}
});
