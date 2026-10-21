import assert from 'node:assert/strict';
import test from 'node:test';
import {
	SETTINGS_IDLE_MS,
	settingsFingerprint,
	shouldLeaveIdleSettings
} from '../src/lib/settings-idle.ts';

const general = { interval: 20, webcalendars: [] as string[] };
const account = {
	uid: 0,
	index: 0,
	immichServerUrl: 'http://immich',
	albums: ['a'],
	catalog: null
};

test('UI-only account state is not an edit', () => {
	const loaded = settingsFingerprint(general, [account]);
	const browsed = {
		...account,
		catalog: { albums: [], people: [], tags: [] },
		catalogLoading: false,
		catalogError: null,
		editingApiKey: true,
		newApiKey: ''
	};
	assert.equal(settingsFingerprint(general, [browsed]), loaded);
});

test('changed values, a typed key or an added account are edits', () => {
	const loaded = settingsFingerprint(general, [account]);
	assert.notEqual(settingsFingerprint({ ...general, interval: 30 }, [account]), loaded);
	assert.notEqual(settingsFingerprint(general, [{ ...account, albums: ['a', 'b'] }]), loaded);
	assert.notEqual(
		settingsFingerprint(general, [{ ...account, editingApiKey: true, newApiKey: 'k' }]),
		loaded
	);
	assert.notEqual(settingsFingerprint(general, [account, { ...account, uid: 1 }]), loaded);
});

test('an idle page returns to the frame unless edits or a save are pending', () => {
	const idle = { idleMs: SETTINGS_IDLE_MS, dirty: false, saving: false };
	assert.equal(shouldLeaveIdleSettings(idle), true);
	assert.equal(shouldLeaveIdleSettings({ ...idle, idleMs: SETTINGS_IDLE_MS - 1 }), false);
	assert.equal(shouldLeaveIdleSettings({ ...idle, dirty: true }), false);
	assert.equal(shouldLeaveIdleSettings({ ...idle, saving: true }), false);
});
