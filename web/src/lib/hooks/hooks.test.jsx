import { act, renderHook } from '@testing-library/react';
import { Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setMobile } from '../../test/media';
import { useDebounced } from './useDebounced';
import { useEscape } from './useEscape';
import { useInterval } from './useInterval';
import { useLatest } from './useLatest';
import { useMobile } from './useMediaQuery';
import { useSubscription } from './useSubscription';

afterEach(() => vi.useRealTimers());

describe('useMobile', () => {
  it('suit le passage sous 900 px', () => {
    const { result } = renderHook(() => useMobile());
    expect(result.current).toBe(false);
    act(() => setMobile(true));
    expect(result.current).toBe(true);
  });
});

describe('useDebounced', () => {
  it('ne recopie la valeur qu’après le délai sans changement', () => {
    vi.useFakeTimers();
    const { result, rerender } = renderHook(({ v }) => useDebounced(v, 300), { initialProps: { v: 'a' } });
    rerender({ v: 'ab' });
    act(() => vi.advanceTimersByTime(200));
    rerender({ v: 'abc' });
    act(() => vi.advanceTimersByTime(200));
    expect(result.current).toBe('a');
    act(() => vi.advanceTimersByTime(100));
    expect(result.current).toBe('abc');
  });
});

describe('useInterval', () => {
  it('appelle la dernière version du rappel, et s’arrête à null', () => {
    vi.useFakeTimers();
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(({ cb, ms }) => useInterval(cb, ms), { initialProps: { cb: first, ms: 1000 } });
    act(() => vi.advanceTimersByTime(1000));
    rerender({ cb: second, ms: 1000 });
    act(() => vi.advanceTimersByTime(2000));
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(2);
    rerender({ cb: second, ms: null });
    act(() => vi.advanceTimersByTime(5000));
    expect(second).toHaveBeenCalledTimes(2);
  });
});

describe('useSubscription', () => {
  it('reçoit les valeurs puis se désabonne au démontage', () => {
    const source = new Subject();
    const onNext = vi.fn();
    const { unmount } = renderHook(() => useSubscription(source, onNext));
    act(() => source.next(1));
    expect(onNext).toHaveBeenCalledWith(1);
    unmount();
    expect(source.observed).toBe(false);
  });

  it('ignore les erreurs de l’abonnement', () => {
    const source = new Subject();
    renderHook(() => useSubscription(source, vi.fn()));
    expect(() => act(() => source.error(new Error('coupure')))).not.toThrow();
  });

  it('ne s’abonne à rien sans source', () => {
    expect(() => renderHook(() => useSubscription(null, vi.fn()))).not.toThrow();
  });
});

describe('useEscape', () => {
  it('réagit à Échap seulement quand il est actif', () => {
    const onEscape = vi.fn();
    const { rerender } = renderHook(({ active }) => useEscape(onEscape, active), { initialProps: { active: false } });
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(onEscape).not.toHaveBeenCalled();
    rerender({ active: true });
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(onEscape).toHaveBeenCalledTimes(1);
  });
});

describe('useLatest', () => {
  it('donne la dernière valeur rendue', () => {
    const { result, rerender } = renderHook(({ v }) => useLatest(v), { initialProps: { v: 1 } });
    rerender({ v: 2 });
    expect(result.current.current).toBe(2);
  });
});
