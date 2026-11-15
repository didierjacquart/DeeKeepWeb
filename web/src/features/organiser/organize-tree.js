import { mediaUrl } from '../../shared/urls';

/**
 * Ligne affichée de l'arbre : un dossier ou un fichier.
 * @typedef {{ kind: 'dir', key: string, name: string, depth: number, files: number[], note: string | null }} DirNode
 * @typedef {{ kind: 'file', entry: any, depth: number, row: any }} FileNode
 * @typedef {DirNode | FileNode} TreeNode
 */

/** Fichier déjà à sa place (en place, ou dans un album préservé) : grisé, masquable. */
export const isSettled = row => row?.status === 'inplace' || row?.status === 'album';

/**
 * Arbre complet (dossiers et fichiers dans l'ordre du parcours), avec les fichiers descendants de chaque dossier.
 * @returns {TreeNode[]}
 */
export function buildTree(entries, rowByIndex, albumFolders, rootName) {
  const albums = new Map((albumFolders ?? []).map(a => [a.folder, a.label]));
  const nodes = [];
  const dirs = new Map();
  const ensure = key => {
    let d = dirs.get(key);
    if (d) return d;
    const parts = key ? key.split('/') : [];
    if (key) ensure(parts.slice(0, -1).join('/'));
    d = {
      kind: 'dir',
      key,
      name: key ? parts[parts.length - 1] : (rootName ?? ''),
      depth: parts.length,
      files: [],
      note: albums.get(key) ?? null,
    };
    dirs.set(key, d);
    nodes.push(d);
    return d;
  };
  ensure('');
  for (const entry of entries) {
    const parts = entry.folder ? entry.folder.split('/') : [];
    ensure(entry.folder);
    for (let i = 0; i <= parts.length; i++) dirs.get(parts.slice(0, i).join('/')).files.push(entry.index);
    nodes.push({ kind: 'file', entry, depth: parts.length + 1, row: rowByIndex.get(entry.index) ?? null });
  }
  return nodes;
}

/** Lignes visibles : dossiers repliés et filtre « Masquer ce qui est déjà en place » (`hide`) appliqués. */
export function visibleNodes(tree, collapsed, hide) {
  const shown = n => !hide || !isSettled(n.row);
  const visibleFiles = new Set();
  for (const n of tree) if (n.kind === 'file' && shown(n)) visibleFiles.add(n.entry.index);
  const out = [];
  let skipDepth = -1;
  for (const n of tree) {
    if (skipDepth >= 0) {
      if (n.depth > skipDepth) continue;
      skipDepth = -1;
    }
    if (n.kind === 'dir') {
      if (hide && n.key !== '' && !n.files.some(i => visibleFiles.has(i))) {
        skipDepth = n.depth;
        continue;
      }
      out.push(n);
      if (collapsed.has(n.key)) skipDepth = n.depth;
    } else if (shown(n)) out.push(n);
  }
  return out;
}

/** Case d'un dossier : « empty » (aucun fichier), « none », « some » ou « all ». */
export function dirState(dir, checkedSet) {
  if (dir.files.length === 0) return 'empty';
  let n = 0;
  for (const i of dir.files) if (checkedSet.has(i)) n++;
  return n === 0 ? 'none' : n === dir.files.length ? 'all' : 'some';
}

export const nodeKey = n => (n.kind === 'dir' ? 'd:' + n.key : 'f:' + n.entry.index);

/** Dossiers à replier par « Tout replier » : tous sauf la racine, qui reste dépliée. */
export const collapsibleKeys = tree => tree.filter(n => n.kind === 'dir' && n.key !== '').map(n => n.key);

/** Ajoute ou retire une clé d'un ensemble (copie). */
export function toggled(set, key) {
  const next = new Set(set);
  if (!next.delete(key)) next.add(key);
  return next;
}

/** Classe de la colonne Action : statut du plan, « none » si le plan ne dit rien, vide sans plan. */
export const actionClass = (n, hasPlan) => n.row?.status ?? (hasPlan ? 'none' : '');

/** Piste du lecteur pour un fichier analysé. */
export function entryTrack(e) {
  return { id: e.dbPath, title: e.name, artist: e.artist, album: e.album, src: mediaUrl(e.dbPath), context: e.path };
}

/** File d'attente : fichiers visibles du même dossier, dans l'ordre de l'arbre, en démarrant sur le fichier choisi. */
export function folderQueue(visible, entry) {
  const siblings = visible.filter(n => n.kind === 'file' && n.entry.folder === entry.folder).map(n => n.entry);
  return { track: entryTrack(entry), queue: siblings.map(entryTrack) };
}
