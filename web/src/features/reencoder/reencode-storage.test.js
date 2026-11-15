import { describe, expect, it, vi } from 'vitest';
import { STORAGE_KEY, loadItems, saveItems } from './reencode-storage';

describe('stockage local de la liste', () => {
  it('relit ce qui a été sauvegardé', () => {
    saveItems([{ path: 'a', matchState: 'found' }]);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY))).toEqual([{ path: 'a', matchState: 'found' }]);
    expect(loadItems()).toEqual([{ path: 'a', matchState: 'found' }]);
  });

  it('transforme une correspondance interrompue en « none »', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([{ path: 'a', matchState: 'pending' }]));
    expect(loadItems()).toEqual([{ path: 'a', matchState: 'none' }]);
  });

  it('rend une liste vide sans sauvegarde ou si elle est illisible', () => {
    expect(loadItems()).toEqual([]);
    localStorage.setItem(STORAGE_KEY, '{oups');
    expect(loadItems()).toEqual([]);
  });

  it('ignore un stockage plein', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    expect(() => saveItems([{ path: 'a' }])).not.toThrow();
  });
});
