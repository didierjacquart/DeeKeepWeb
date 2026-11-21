import { Store } from '../../../lib/store';
import { api } from '../../../core/api/api';
import { isActive, jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import {
  ARCHIVE,
  ARCHIVE_CONFIG,
  ARCHIVE_PLAN,
  CHECK,
  MEASURE,
  REMOVE,
  RESTORE,
  TEST,
  UPDATE_CONFIG,
} from './archive-api';
import {
  ARCHIVE_JOB_TYPES,
  ARCHIVE_WORK_TYPES,
  UNCONFIGURED_TEXT,
  nothingToRestore,
  selectConfigured,
} from './archive-model';

/**
 * Actions de l'archive, partagées par le volet « Archive » et le menu
 * contextuel de la Bibliothèque : archiver, restaurer, retirer. Chaque action
 * longue lance une tâche serveur et rend la main ; la progression se lit dans
 * `jobs`. La configuration est lue à l'ouverture du volet, ou à la première
 * action qui en a besoin (importer le module n'interroge pas le serveur).
 */
export class ArchiveActions extends Store {
  constructor() {
    super({ config: null, openPanelRequest: 0 });
  }

  get configured() {
    return selectConfigured(this.state);
  }

  /** Archivage ou restauration en cours (ou en file). */
  get busy() {
    return jobs.state.all.some(j => ARCHIVE_WORK_TYPES.includes(j.type) && isActive(j));
  }

  async loadConfig() {
    try {
      const data = await api.query(ARCHIVE_CONFIG);
      this.setState({ config: data.archiveConfig });
    } catch {
      // Hors ligne : on garde la dernière valeur connue.
    }
    return this.state.config;
  }

  async saveConfig(root) {
    const r = await api.mutate(
      UPDATE_CONFIG,
      { root, prefix: this.state.config?.prefix ?? null },
      { label: "Enregistrer l'archive", refetch: ['ArchiveUsage'] },
    );
    if (r.error) {
      notify.error(r.error);
      return null;
    }
    if (r.queued) {
      notify.info('Enregistrement en attente du retour de la connexion.');
      return null;
    }
    if (r.data?.updateArchiveConfig) this.setState({ config: r.data.updateArchiveConfig });
    return r.data?.updateArchiveConfig ?? null;
  }

  async test(root) {
    const r = await api.mutate(TEST, { root }, { label: "Tester l'archive", queueable: false });
    return r.data?.testArchive ?? { ok: false, message: r.error ?? 'Serveur injoignable.' };
  }

  async plan(paths) {
    try {
      return (await api.query(ARCHIVE_PLAN, { paths })).archivePlan;
    } catch (e) {
      notify.error(e instanceof Error ? e.message : String(e));
      return null;
    }
  }

  /** Archive des titres (chemins en base) ; ceux absents du disque sont écartés. */
  async archive(paths) {
    if (!paths.length || this.busy) return;
    if (!(await this.ensureConfigured())) return;
    const plan = await this.plan(paths);
    if (!plan) return;
    if (!plan.onDisk) {
      notify.info("Aucun des titres retenus n'est présent sur le disque : il n'y a rien à archiver.");
      return;
    }
    const r = await api.mutate(ARCHIVE, { paths }, { label: 'Archiver', queueable: false });
    this.started(r.data?.archiveTracks, r.error, `Envoi de ${plan.onDisk} titres en cours…`);
  }

  /** Restaure les titres absents du disque ; rien n'est écrasé. */
  async restore(paths, plan) {
    if (!paths.length || this.busy) return;
    if (!(await this.ensureConfigured())) return;
    plan ??= await this.plan(paths);
    if (!plan) return;
    if (!plan.restorable) {
      notify.info(nothingToRestore(plan));
      return;
    }
    const r = await api.mutate(RESTORE, { paths }, { label: 'Restaurer', queueable: false });
    this.started(r.data?.restoreTracks, r.error, `Restauration de ${plan.restorable} titres en cours…`);
  }

  /**
   * Retire des titres de l'archive après confirmation. La copie archivée n'est
   * effacée que si plus aucun titre identique ne la partage.
   */
  async remove(paths) {
    if (!paths.length) return;
    const one = paths.length === 1;
    const ok = await confirm({
      title: "Retirer de l'archive",
      message:
        (one ? "Retirer ce titre de l'archive ?" : `Retirer ces ${paths.length} titres de l'archive ?`) +
        "\nLa copie archivée est effacée, sauf si un autre titre identique la partage. Le fichier sur le disque n'est pas touché.",
      confirm: 'Retirer',
      danger: true,
    });
    if (!ok) return;
    const r = await api.mutate(REMOVE, { paths }, { label: "Retirer de l'archive", refetch: ['ArchiveUsage'] });
    if (r.error) notify.error(r.error);
    else if (r.queued) notify.info('Retrait en attente du retour de la connexion.');
    else
      notify.success(
        r.data?.removeFromArchive === 1
          ? "Titre retiré de l'archive."
          : `${r.data?.removeFromArchive ?? 0} titres retirés de l'archive.`,
      );
  }

  async measure(root) {
    const r = await api.mutate(MEASURE, { root }, { label: "Mesurer l'archive", queueable: false });
    if (r.error) notify.error(r.error);
    if (r.data?.measureArchive) jobs.upsert(r.data.measureArchive);
    return r.data?.measureArchive ?? null;
  }

  async check() {
    const r = await api.mutate(CHECK, {}, { label: "Vérifier l'archive", queueable: false });
    if (r.error) notify.error(r.error);
    if (r.data?.checkArchive) jobs.upsert(r.data.checkArchive);
    return r.data?.checkArchive ?? null;
  }

  /** « Arrêter » vaut pour toutes les tâches de l'archive en cours. */
  async stop() {
    const active = jobs.state.all.filter(j => ARCHIVE_JOB_TYPES.includes(j.type) && isActive(j));
    await Promise.all(active.map(j => jobs.cancel(j)));
  }

  async ensureConfigured() {
    const config = this.state.config ?? (await this.loadConfig());
    if (config?.configured) return true;
    notify.info(UNCONFIGURED_TEXT);
    this.setState(s => ({ openPanelRequest: s.openPanelRequest + 1 }));
    return false;
  }

  started(job, error, message) {
    if (error) {
      notify.error(error);
      return;
    }
    if (job) {
      jobs.upsert(job);
      notify.info(message);
    }
  }
}

export const archiveActions = new ArchiveActions();
