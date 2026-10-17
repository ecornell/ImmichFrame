import { format, type FormatOptions } from 'date-fns';

const warned = new Set<string>();

// Date patterns come from user settings, and date-fns throws on an invalid one (such as an
// unescaped letter in `HH:mm Uhr`). Fall back to the default pattern instead of breaking every
// overlay that shows a date.
export function safeFormat(
	date: Date,
	pattern: string | null | undefined,
	fallback: string,
	options?: FormatOptions
): string {
	if (Number.isNaN(date.getTime())) return '';

	if (pattern) {
		try {
			return format(date, pattern, options);
		} catch (error) {
			if (!warned.has(pattern)) {
				warned.add(pattern);
				console.warn(`Invalid date format "${pattern}", using "${fallback}"`, error);
			}
		}
	}

	return format(date, fallback, options);
}
