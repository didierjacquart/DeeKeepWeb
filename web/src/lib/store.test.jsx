import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Store, useStore } from './store';
import { cx } from './cx';

class Counter extends Store {
  constructor() {
    super({ count: 0, label: 'a' });
  }
  increment() {
    this.setState(s => ({ count: s.count + 1 }));
  }
  rename(label) {
    this.setState({ label });
  }
}

describe('Store', () => {
  it('fusionne l’état et prévient les abonnés', () => {
    const store = new Counter();
    const listener = vi.fn();
    store.subscribe(listener);
    store.increment();
    expect(store.state).toEqual({ count: 1, label: 'a' });
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('ne prévient personne si rien ne change', () => {
    const store = new Counter();
    const listener = vi.fn();
    store.subscribe(listener);
    const before = store.state;
    store.rename('a');
    expect(listener).not.toHaveBeenCalled();
    expect(store.state).toBe(before);
  });

  it('cesse de prévenir après désabonnement', () => {
    const store = new Counter();
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);
    unsubscribe();
    store.increment();
    expect(listener).not.toHaveBeenCalled();
  });
});

describe('useStore', () => {
  function View({ store }) {
    const count = useStore(store, s => s.count);
    const { label } = useStore(store);
    return (
      <p>
        {label}:{count}
      </p>
    );
  }

  it('redessine le composant à chaque changement', () => {
    const store = new Counter();
    render(<View store={store} />);
    expect(screen.getByText('a:0')).toBeInTheDocument();
    act(() => store.increment());
    act(() => store.rename('b'));
    expect(screen.getByText('b:1')).toBeInTheDocument();
  });
});

describe('cx', () => {
  it('ignore les classes fausses', () => {
    expect(cx('a', false, null, undefined, '', 'b')).toBe('a b');
  });
});
