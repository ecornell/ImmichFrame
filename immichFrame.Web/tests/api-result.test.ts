import assert from 'node:assert/strict';
import test from 'node:test';
import { okArray } from '../src/lib/api-result.ts';

test('only a 200 with an array body yields items', () => {
	assert.deepEqual(okArray({ status: 200, data: [1, 2] }), [1, 2]);
	assert.deepEqual(okArray({ status: 404, data: { title: 'Not Found', status: 404 } }), []);
	assert.deepEqual(okArray({ status: 500, data: 'Internal Server Error' }), []);
	assert.deepEqual(okArray({ status: 200, data: { title: 'surprise' } }), []);
	assert.deepEqual(okArray({ status: 200 }), []);
});
