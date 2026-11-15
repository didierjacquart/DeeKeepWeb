import { describe, expect, it, vi } from 'vitest';
import { handlePlayerKey, isPlayerShortcut } from './keyboard-shortcuts';
import { playbackErrorMessage } from './audio-elements';

const key = (k, fields = {}) => ({ key: k, target: document.body, preventDefault: vi.fn(), ...fields });
const fakePlayer = () => ({
  current: { id: 'a' },
  state: { volume: 0.5 },
  toggle: vi.fn(),
  seekBy: vi.fn(),
  setVolume: vi.fn(),
  previous: vi.fn(),
  next: vi.fn(),
});

describe('raccourcis du lecteur', () => {
  it.each([
    [' ', 'toggle', []],
    ['ArrowLeft', 'seekBy', [-5]],
    ['ArrowRight', 'seekBy', [5]],
    ['ArrowUp', 'setVolume', [0.55]],
    ['ArrowDown', 'setVolume', [0.45]],
    ['P', 'previous', []],
    ['n', 'next', []],
  ])('« %s » → %s', (k, method, args) => {
    const p = fakePlayer();
    const e = key(k);
    expect(handlePlayerKey(p, e)).toBe(true);
    expect(p[method]).toHaveBeenCalledWith(...args);
    expect(e.preventDefault).toHaveBeenCalled();
  });

  it('laisse passer les autres touches, les champs de saisie et les combinaisons', () => {
    const p = fakePlayer();
    expect(handlePlayerKey(p, key('x'))).toBe(false);
    expect(handlePlayerKey(p, key(' ', { target: document.createElement('input') }))).toBe(false);
    expect(handlePlayerKey(p, key('n', { ctrlKey: true }))).toBe(false);
    expect(p.next).not.toHaveBeenCalled();
  });

  it('ne fait rien quand rien ne joue', () => {
    const p = { ...fakePlayer(), current: null };
    expect(handlePlayerKey(p, key(' '))).toBe(false);
  });

  it('reconnaît les cibles éditables', () => {
    const editable = document.createElement('div');
    editable.contentEditable = 'true';
    Object.defineProperty(editable, 'isContentEditable', { value: true });
    expect(isPlayerShortcut(key(' ', { target: editable }))).toBe(false);
    expect(isPlayerShortcut(key(' ', { target: document.createElement('button') }))).toBe(false);
  });

  it('explique un échec de lecture selon la connexion', () => {
    expect(playbackErrorMessage(true)).toBe('Lecture impossible : fichier introuvable ou illisible.');
    expect(playbackErrorMessage(false)).toBe("Hors ligne : ce titre n'est pas disponible sur l'appareil.");
  });
});
