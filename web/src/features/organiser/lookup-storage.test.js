import { describe, expect, it } from 'vitest';
import { clearLookup, LOOKUP_KEY, readLookup, writeLookup } from './lookup-storage';

describe('lookup-storage', () => {
  it('relit la revue de la même session seulement', () => {
    writeLookup({ sessionId: 's1', files: [] });
    expect(readLookup('s1')).toEqual({ sessionId: 's1', files: [] });
    expect(readLookup('s2')).toBeNull();
    clearLookup();
    expect(readLookup('s1')).toBeNull();
  });

  it('ignore un contenu illisible', () => {
    localStorage.setItem(LOOKUP_KEY, '{oups');
    expect(readLookup('s1')).toBeNull();
  });
});
