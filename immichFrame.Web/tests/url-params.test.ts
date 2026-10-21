import assert from 'node:assert/strict';
import test from 'node:test';
import { searchWithoutAuthSecret } from '../src/lib/url-params.ts';

test('strips the secret and keeps everything else', () => {
	assert.equal(
		searchWithoutAuthSecret('http://frame.local:8080/?client=kitchen&authsecret=s3cret#x'),
		'?client=kitchen#x'
	);
	assert.equal(
		searchWithoutAuthSecret('http://frame.local/?authsecret=s3cret&client=hall'),
		'?client=hall'
	);
	assert.equal(searchWithoutAuthSecret('http://frame.local/?authsecret=s3cret'), '');
	assert.equal(searchWithoutAuthSecret('http://frame.local/?authsecret='), '');
});

test('leaves URLs without a secret alone', () => {
	assert.equal(searchWithoutAuthSecret('http://frame.local/?client=kitchen'), null);
	assert.equal(searchWithoutAuthSecret('http://frame.local/'), null);
});
