/** Only queue the oldest repeat; reconsider recency before choosing another. */
export function oldestRepeat<T>(
	candidates: T[],
	recentKeys: string[],
	key: (candidate: T) => string
): T[] {
	const recency = new Map(recentKeys.map((value, index) => [value, index]));
	return [...candidates]
		.sort((a, b) => (recency.get(key(a)) ?? -1) - (recency.get(key(b)) ?? -1))
		.slice(0, 1);
}
