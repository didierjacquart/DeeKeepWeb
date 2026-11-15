import { describe, expect, it } from 'vitest';
import {
  actionClass,
  buildTree,
  collapsibleKeys,
  dirState,
  folderQueue,
  nodeKey,
  toggled,
  visibleNodes,
} from './organize-tree';

const entries = [
  { index: 0, folder: '', name: 'root.mp3', dbPath: 'root.mp3', path: '/m/root.mp3' },
  { index: 1, folder: 'A', name: 'a1.mp3', dbPath: 'A/a1.mp3', path: '/m/A/a1.mp3', artist: 'Inna', album: 'Hot' },
  { index: 2, folder: 'A/B', name: 'b1.mp3', dbPath: 'A/B/b1.mp3', path: '/m/A/B/b1.mp3' },
  { index: 3, folder: 'C', name: 'c1.mp3', dbPath: 'C/c1.mp3', path: '/m/C/c1.mp3' },
];
const rows = new Map([
  [0, { index: 0, status: 'move' }],
  [1, { index: 1, status: 'inplace' }],
  [2, { index: 2, status: 'album' }],
  [3, { index: 3, status: 'inplace' }],
]);
const tree = buildTree(entries, rows, [{ folder: 'A', label: 'Album X' }], '/m');

describe('buildTree', () => {
  it('rend dossiers et fichiers dans l’ordre du parcours, avec leur profondeur', () => {
    expect(tree.map(nodeKey)).toEqual(['d:', 'f:0', 'd:A', 'f:1', 'd:A/B', 'f:2', 'd:C', 'f:3']);
    expect(tree.map(n => n.depth)).toEqual([0, 1, 1, 2, 2, 3, 1, 2]);
  });

  it('nomme la racine, note les albums préservés et rattache les fichiers descendants', () => {
    const [root, , a] = tree;
    expect(root.name).toBe('/m');
    expect(root.files).toEqual([0, 1, 2, 3]);
    expect(a).toMatchObject({ name: 'A', note: 'Album X', files: [1, 2] });
    expect(tree[1].row).toEqual({ index: 0, status: 'move' });
  });
});

describe('visibleNodes', () => {
  it('masque le contenu des dossiers repliés', () => {
    expect(visibleNodes(tree, new Set(['A']), false).map(nodeKey)).toEqual(['d:', 'f:0', 'd:A', 'd:C', 'f:3']);
  });

  it('masque ce qui est déjà en place, et les dossiers qui n’ont plus rien à montrer', () => {
    expect(visibleNodes(tree, new Set(), true).map(nodeKey)).toEqual(['d:', 'f:0']);
  });
});

describe('dirState', () => {
  const dir = { files: [1, 2] };
  it('rend l’état de la case d’un dossier', () => {
    expect(dirState(dir, new Set())).toBe('none');
    expect(dirState(dir, new Set([1]))).toBe('some');
    expect(dirState(dir, new Set([1, 2, 3]))).toBe('all');
    expect(dirState({ files: [] }, new Set([1]))).toBe('empty');
  });
});

describe('aides', () => {
  it('collapsibleKeys laisse la racine dépliée', () => {
    expect(collapsibleKeys(tree)).toEqual(['A', 'A/B', 'C']);
  });

  it('toggled ajoute ou retire une clé sans modifier l’original', () => {
    const s = new Set(['a']);
    expect([...toggled(s, 'b')]).toEqual(['a', 'b']);
    expect([...toggled(s, 'a')]).toEqual([]);
    expect([...s]).toEqual(['a']);
  });

  it('actionClass suit le statut du plan', () => {
    expect(actionClass({ row: { status: 'move' } }, true)).toBe('move');
    expect(actionClass({ row: null }, true)).toBe('none');
    expect(actionClass({ row: null }, false)).toBe('');
  });

  it('folderQueue met en file les fichiers visibles du même dossier', () => {
    const visible = visibleNodes(tree, new Set(), false);
    const { track, queue } = folderQueue(visible, entries[1]);
    expect(track).toEqual({
      id: 'A/a1.mp3',
      title: 'a1.mp3',
      artist: 'Inna',
      album: 'Hot',
      src: '/media/track?path=A%2Fa1.mp3',
      context: '/m/A/a1.mp3',
    });
    expect(queue.map(t => t.id)).toEqual(['A/a1.mp3']);
  });
});
