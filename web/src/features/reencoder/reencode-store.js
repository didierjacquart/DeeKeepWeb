import { Store } from '../../lib/store';
import { isActive, jobs } from '../../core/jobs/jobs';
import { confirm } from '../../shared/dialog/confirm';
import { enqueueConversions, findBestMatch, scanLocalFiles } from './reencode-api';
import { applyConversionResults, applyMatch, itemsFromScan } from './reencode-items';
import { loadItems, saveItems } from './reencode-storage';

/** Limite de la correspondance Deezer automatique (REENCODE_AUTOMATCH_LIMIT). */
export const AUTOMATCH_LIMIT = 150;

/**
 * État de la page Réencoder, conservé hors du composant : quitter la page ne perd
 * ni la liste ni le suivi des conversions (qui tournent sur le serveur).
 */
export class ReencodeStore extends Store {
  buffer = new Map();
  started = false;

  constructor() {
    super({ items: loadItems(), scanJobIds: [], batchIds: [], matching: 0 });
  }

  /** Démarre l'écoute des analyses et des conversions (au premier affichage de la page). */
  start() {
    if (this.started) return;
    this.started = true;
    jobs.items({ type: 'files.scan' }).subscribe({
      next: d => {
        const e = d?.jobItem;
        if (!e || e.status !== 'ok') return;
        const list = this.buffer.get(e.jobId) ?? [];
        list.push(e.data);
        this.buffer.set(e.jobId, list);
      },
      error: () => undefined,
    });
    jobs.subscribe(() => this.sync());
    // Sauvegarde locale de la liste à chaque changement.
    let saved = this.state.items;
    this.subscribe(() => {
      if (this.state.items === saved) return;
      saved = this.state.items;
      saveItems(saved);
    });
    this.sync();
  }

  /** Équivalent des effets de l'original : fin des analyses, résultat des conversions. */
  sync() {
    const all = jobs.state.all;
    // Ingestion en une fois, à la fin de chaque analyse.
    for (const id of this.state.scanJobIds) {
      const job = all.find(j => j.id === id);
      if (job && !isActive(job)) this.finishScan(job);
    }
    // Résultat des conversions : fichier produit (chemin, format, débit) et tags retenus.
    this.updateItems(list => applyConversionResults(list, jobs.state.byId));
  }

  updateItems(fn) {
    this.setState(s => ({ items: fn(s.items) }));
  }

  async scan(paths) {
    const id = await scanLocalFiles(paths);
    this.setState(s => ({ scanJobIds: [...s.scanJobIds, id] }));
    this.sync();
    void jobs.load();
  }

  finishScan(job) {
    this.setState(s => ({ scanJobIds: s.scanJobIds.filter(x => x !== job.id) }));
    const found = this.buffer.get(job.id) ?? [];
    this.buffer.delete(job.id);
    if (!found.length) {
      if (job.status === 'SUCCEEDED') void this.info('Aucun ajout', 'Aucun fichier audio trouvé.');
      return;
    }
    const fresh = itemsFromScan(found, this.state.items);
    if (!fresh.length) {
      void this.info('Aucun ajout', 'Tous les fichiers trouvés sont déjà présents dans la liste.');
      return;
    }
    const skip = fresh.length > AUTOMATCH_LIMIT;
    this.updateItems(l => [...l, ...fresh.map(i => (skip ? { ...i, matchState: 'skipped', selected: true } : i))]);
    if (skip) {
      void this.info(
        'Bibliothèque volumineuse',
        `${fresh.length} fichiers ajoutés. La recherche automatique de correspondances Deezer (jaquettes) a été désactivée pour préserver la fluidité ; ` +
          'les fichiers sont présélectionnés et seront réencodés à partir de leurs métadonnées locales.\n\n' +
          'Astuce : utilisez « Masquer ceux déjà au format cible » pour ne convertir que le nécessaire.',
      );
    } else {
      void this.matchAll(fresh.map(i => i.path));
    }
  }

  /** Correspondance Deezer séquentielle (le serveur limite déjà le débit vers Deezer). */
  async matchAll(paths) {
    this.setState(s => ({ matching: s.matching + paths.length }));
    for (const path of paths) {
      const item = this.state.items.find(i => i.path === path);
      const match = item && (item.artist || item.title) ? await findBestMatch(item.artist, item.title) : null;
      this.updateItems(l => applyMatch(l, path, match));
      this.setState(s => ({ matching: s.matching - 1 }));
    }
  }

  info(title, message) {
    return confirm({ title, message, confirm: 'OK', cancel: 'Fermer' });
  }

  async convert(items) {
    const batchId = crypto.randomUUID().replaceAll('-', '');
    const created = await enqueueConversions(items, batchId);
    const byPath = new Map(items.map((i, n) => [i.path, created[n]?.id ?? null]));
    this.setState({ batchIds: created.map(j => j.id) });
    this.updateItems(l =>
      l.map(i => (byPath.has(i.path) ? { ...i, jobId: byPath.get(i.path) ?? null, converted: false } : i)),
    );
    this.sync();
    void jobs.load();
  }
}

export const reencodeStore = new ReencodeStore();
