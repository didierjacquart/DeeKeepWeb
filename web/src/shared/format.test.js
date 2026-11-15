import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatBytes, formatDuration, formatWhen } from './format';

describe('formatDuration', () => {
  it.each([
    [0, '0:00'],
    [5, '0:05'],
    [241.5, '4:01'],
    [3599, '59:59'],
    [3723, '1:02:03'],
  ])('%s s → %s', (seconds, expected) => {
    expect(formatDuration(seconds)).toBe(expected);
  });

  it.each([null, undefined, -1, Number.NaN, Number.POSITIVE_INFINITY])('rend un tiret pour %s', value => {
    expect(formatDuration(value)).toBe('—');
  });
});

describe('formatBytes', () => {
  it('compte en unités décimales, à la française', () => {
    expect(formatBytes(999)).toBe('999 o');
    expect(formatBytes(1500)).toBe('1,5 Ko');
    expect(formatBytes(14_100_000_000)).toBe('14,1 Go');
    expect(formatBytes(3_456_789, 2)).toBe('3,46 Mo');
  });

  it('rend un tiret sans valeur', () => {
    expect(formatBytes(null)).toBe('—');
    expect(formatBytes(undefined)).toBe('—');
  });
});

describe('formatWhen', () => {
  afterEach(() => vi.useRealTimers());

  const now = new Date(2026, 10, 15, 14, 30, 0);
  const ago = seconds => now.getTime() / 1000 - seconds;

  it('dit « à l’instant », puis les minutes écoulées', () => {
    vi.useFakeTimers({ now });
    expect(formatWhen(ago(10))).toBe("à l'instant");
    expect(formatWhen(ago(5 * 60))).toBe('il y a 5 min');
  });

  it('donne l’heure pour plus tôt dans la journée', () => {
    vi.useFakeTimers({ now });
    expect(formatWhen(ago(3 * 3600))).toBe('11:30');
  });

  it('donne la date complète au-delà', () => {
    vi.useFakeTimers({ now });
    expect(formatWhen(ago(3 * 86400))).toBe('12/11/26 14:30');
  });

  it('rend un tiret sans date', () => {
    expect(formatWhen(null)).toBe('—');
    expect(formatWhen(0)).toBe('—');
  });
});
