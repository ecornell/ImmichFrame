// Immich sends birthDate as a calendar date ("1990-05-03", or with a midnight time after it).
// `new Date()` reads a date-only string as UTC midnight, which is the previous day west of UTC,
// so compare the written month and day with today's local date instead.
export function isBirthdayToday(birthDate: string | null | undefined, today = new Date()): boolean {
	const match = /^\d{4}-(\d{2})-(\d{2})/.exec(birthDate ?? '');
	if (!match) return false;

	return Number(match[1]) === today.getMonth() + 1 && Number(match[2]) === today.getDate();
}
