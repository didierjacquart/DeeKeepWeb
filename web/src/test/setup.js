import 'fake-indexeddb/auto';
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { installMatchMedia, setMobile } from './media';

// ------------------------------------------------------------ Ce que jsdom ne fournit pas

installMatchMedia();

/** Rapporte une taille fixe à l'observation (les listes virtuelles en ont besoin). */
globalThis.ResizeObserver = class {
  constructor(callback) {
    this.callback = callback;
  }
  observe(target) {
    this.callback([{ target, contentRect: target.getBoundingClientRect() }], this);
  }
  unobserve() {}
  disconnect() {}
};

window.scrollTo = vi.fn();
Element.prototype.scrollIntoView = vi.fn();

// Lecture audio : jsdom n'implémente pas les médias ; on simule les événements utiles.
Object.defineProperty(HTMLMediaElement.prototype, 'paused', {
  configurable: true,
  get() {
    return this.dataset.playing !== 'true';
  },
});
HTMLMediaElement.prototype.play = function play() {
  this.dataset.playing = 'true';
  this.dispatchEvent(new Event('play'));
  return Promise.resolve();
};
HTMLMediaElement.prototype.pause = function pause() {
  if (this.dataset.playing === 'true') {
    this.dataset.playing = 'false';
    this.dispatchEvent(new Event('pause'));
  }
};
HTMLMediaElement.prototype.load = function load() {};

// ------------------------------------------------------------ Isolation des tests

beforeEach(() => {
  setMobile(false);
  localStorage.clear();
});

// Les services sont des singletons : on vide ce que chaque test a pu y laisser.
// Imports dynamiques : charger ces modules ici, avant le fichier de test, rendrait
// sans effet les vi.mock de ce dernier.
afterEach(async () => {
  cleanup();
  const [{ dialogs }, { notify }, { jobs }] = await Promise.all([
    import('../shared/dialog/dialog-stack'),
    import('../core/ui/notify'),
    import('../core/jobs/jobs'),
  ]);
  for (const d of dialogs.state.stack) d.close();
  notify.dismiss();
  jobs.forget(jobs.state.all.map(j => j.id));
});
