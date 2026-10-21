/** A settings page left alone this long goes back to the slideshow (unless it has edits). */
export const SETTINGS_IDLE_MS = 5 * 60 * 1000;

/** Fields the settings page keeps per account for its own UI; they are not edits. */
const LOCAL_ACCOUNT_FIELDS = new Set([
	'uid',
	'catalog',
	'catalogLoading',
	'catalogError',
	'editingApiKey'
]);

/**
 * A comparable fingerprint of the form, so "has the user changed anything" is a string compare.
 * UI-only account fields are left out, and an API key box opened but left empty is no edit.
 */
export function settingsFingerprint(general: unknown, accounts: Record<string, unknown>[]): string {
	return JSON.stringify({
		general,
		accounts: accounts.map((account) =>
			Object.fromEntries(
				Object.entries(account).filter(
					([key, value]) => !LOCAL_ACCOUNT_FIELDS.has(key) && !(key === 'newApiKey' && !value)
				)
			)
		)
	});
}

export interface SettingsIdleState {
	idleMs: number;
	/** The form differs from what was loaded; leaving would throw the edits away. */
	dirty: boolean;
	saving: boolean;
}

/**
 * Whether the page should return to the slideshow. An accidental tap on the frame's settings
 * button must not leave a wall-mounted frame on this page for good.
 */
export function shouldLeaveIdleSettings(
	state: SettingsIdleState,
	timeoutMs = SETTINGS_IDLE_MS
): boolean {
	return state.idleMs >= timeoutMs && !state.dirty && !state.saving;
}
