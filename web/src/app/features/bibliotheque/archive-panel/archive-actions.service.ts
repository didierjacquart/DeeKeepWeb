import { Injectable, computed, inject, signal } from '@angular/core';
import { gql } from '@apollo/client';
import { GqlService } from '../../../core/graphql/gql.service';
import { JOB_FIELDS, Job, JobsService, isActive } from '../../../core/jobs/jobs.service';
import { NotifyService } from '../../../core/ui/notify.service';
import { ConfirmService } from '../../../shared/ui';

/** Types de tâches de l'archive (StorageJobTypes côté serveur). */
export const ARCHIVE_JOB_TYPES = ['archive.upload', 'archive.restore', 'archive.check', 'archive.usage'] as const;
/** Les deux sens de la copie : ils ne tournent pas ensemble et partagent la progression. */
export const ARCHIVE_WORK_TYPES = ['archive.upload', 'archive.restore'] as const;

export interface ArchiveConfig {
  root: string | null;
  prefix: string;
  archiveFile: string | null;
  configured: boolean;
}

export interface ArchiveUsage {
  objectCount: number;
  storedBytes: number;
  archiveFileBytes: number;
  diskFreeBytes: number | null;
  diskTotalBytes: number | null;
  tracksArchived: number;
  tracksStale: number;
  tracksNotArchived: number;
  bytesToArchive: number;
  tracksOnDisk: number;
  bytesOnDisk: number;
  sameVolume: boolean;
  configured: boolean;
  archiveFile: string | null;
  error: string | null;
}

export interface ArchivePlan {
  requested: number;
  onDisk: number;
  restorable: number;
  restorableElsewhere: number;
  configured: boolean;
}

export interface ArchiveTestResult {
  ok: boolean;
  message: string;
}

/** Résultat élément par élément (données de jobItem). */
export interface ArchiveItemResult {
  path: string;
  state: 'sent' | 'reused' | 'skipped' | 'failed' | 'restored';
  message: string | null;
  bytes: number;
  key: string | null;
}

const CONFIG_FIELDS = 'root prefix archiveFile configured';
const USAGE_FIELDS = `objectCount storedBytes archiveFileBytes diskFreeBytes diskTotalBytes tracksArchived tracksStale
  tracksNotArchived bytesToArchive tracksOnDisk bytesOnDisk sameVolume configured archiveFile error`;

export const ARCHIVE_CONFIG = gql`query ArchiveConfig { archiveConfig { ${CONFIG_FIELDS} } }`;
export const ARCHIVE_USAGE = gql`query ArchiveUsage($root: String) { archiveUsage(root: $root) { ${USAGE_FIELDS} } }`;
const ARCHIVE_PLAN = gql`query ArchivePlan($paths: [String!]!) { archivePlan(paths: $paths) { requested onDisk restorable restorableElsewhere configured } }`;
const UPDATE_CONFIG = gql`mutation UpdateArchiveConfig($root: String, $prefix: String) { updateArchiveConfig(root: $root, prefix: $prefix) { ${CONFIG_FIELDS} } }`;
const TEST = gql`mutation TestArchive($root: String) { testArchive(root: $root) { ok message } }`;
const ARCHIVE = gql`mutation ArchiveTracks($paths: [String!]!) { archiveTracks(paths: $paths) { ${JOB_FIELDS} } }`;
const RESTORE = gql`mutation RestoreTracks($paths: [String!]!) { restoreTracks(paths: $paths) { ${JOB_FIELDS} } }`;
const REMOVE = gql`mutation RemoveFromArchive($paths: [String!]!) { removeFromArchive(paths: $paths) }`;
const MEASURE = gql`mutation MeasureArchive($root: String) { measureArchive(root: $root) { ${JOB_FIELDS} } }`;
const CHECK = gql`mutation CheckArchive { checkArchive { ${JOB_FIELDS} } }`;
export const ARCHIVE_UPDATED = gql`subscription ArchiveUpdated { archiveUpdated { paths reason } }`;

/** Texte de l'original quand aucune archive n'est enregistrée (_unconfigured_text). */
export const UNCONFIGURED_TEXT =
  'Aucune archive configurée. Ouvrez le volet « Archive » et indiquez le fichier où ranger les titres, puis « Enregistrer ».';

/**
 * Actions de l'archive, partagées par le volet « Archive » et le menu
 * contextuel de la Bibliothèque : archiver, restaurer, retirer. Chaque action
 * longue lance une tâche serveur et rend la main ; la progression se lit dans
 * JobsService.
 */
@Injectable({ providedIn: 'root' })
export class ArchiveActions {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  /** Dernière configuration connue (null tant qu'elle n'a pas été lue). */
  readonly config = signal<ArchiveConfig | null>(null);
  readonly configured = computed(() => this.config()?.configured ?? false);
  /** Demande d'ouverture du volet (archivage demandé sans archive configurée). */
  readonly openPanelRequest = signal(0);

  private readonly workJobs = this.jobs.byType(...ARCHIVE_WORK_TYPES);
  /** Archivage ou restauration en cours (ou en file). */
  readonly busy = computed(() => this.workJobs().some(isActive));
  /** Tâche de copie la plus récente (en cours ou terminée). */
  readonly currentWork = computed<Job | null>(() => this.workJobs()[0] ?? null);

  constructor() {
    void this.loadConfig();
  }

  async loadConfig(): Promise<ArchiveConfig | null> {
    try {
      const data = await this.gql.query<{ archiveConfig: ArchiveConfig }>(ARCHIVE_CONFIG);
      this.config.set(data.archiveConfig);
    } catch {
      // Hors ligne : on garde la dernière valeur connue.
    }
    return this.config();
  }

  async saveConfig(root: string): Promise<ArchiveConfig | null> {
    const r = await this.gql.mutate<{ updateArchiveConfig: ArchiveConfig }>(
      UPDATE_CONFIG, { root, prefix: this.config()?.prefix ?? null }, { label: 'Enregistrer l\'archive', refetch: ['ArchiveUsage'] });
    if (r.error) {
      this.notify.error(r.error);
      return null;
    }
    if (r.queued) {
      this.notify.info('Enregistrement en attente du retour de la connexion.');
      return null;
    }
    if (r.data?.updateArchiveConfig) this.config.set(r.data.updateArchiveConfig);
    return r.data?.updateArchiveConfig ?? null;
  }

  async test(root: string | null): Promise<ArchiveTestResult> {
    const r = await this.gql.mutate<{ testArchive: ArchiveTestResult }>(TEST, { root }, { label: 'Tester l\'archive', queueable: false });
    return r.data?.testArchive ?? { ok: false, message: r.error ?? 'Serveur injoignable.' };
  }

  async plan(paths: string[]): Promise<ArchivePlan | null> {
    try {
      return (await this.gql.query<{ archivePlan: ArchivePlan }>(ARCHIVE_PLAN, { paths })).archivePlan;
    } catch (e) {
      this.notify.error(e instanceof Error ? e.message : String(e));
      return null;
    }
  }

  /** Archive des titres (chemins en base) ; ceux absents du disque sont écartés. */
  async archive(paths: string[]): Promise<void> {
    if (!paths.length || this.busy()) return;
    if (!(await this.ensureConfigured())) return;
    const plan = await this.plan(paths);
    if (!plan) return;
    if (!plan.onDisk) {
      this.notify.info('Aucun des titres retenus n\'est présent sur le disque : il n\'y a rien à archiver.');
      return;
    }
    const r = await this.gql.mutate<{ archiveTracks: Job }>(ARCHIVE, { paths }, { label: 'Archiver', queueable: false });
    this.started(r.data?.archiveTracks, r.error, `Envoi de ${plan.onDisk} titres en cours…`);
  }

  /** Restaure les titres absents du disque ; rien n'est écrasé. */
  async restore(paths: string[], plan?: ArchivePlan | null): Promise<void> {
    if (!paths.length || this.busy()) return;
    if (!(await this.ensureConfigured())) return;
    plan ??= await this.plan(paths);
    if (!plan) return;
    if (!plan.restorable) {
      this.notify.info(nothingToRestore(plan));
      return;
    }
    const r = await this.gql.mutate<{ restoreTracks: Job }>(RESTORE, { paths }, { label: 'Restaurer', queueable: false });
    this.started(r.data?.restoreTracks, r.error, `Restauration de ${plan.restorable} titres en cours…`);
  }

  /**
   * Retire des titres de l'archive après confirmation. La copie archivée n'est
   * effacée que si plus aucun titre identique ne la partage.
   */
  async remove(paths: string[]): Promise<void> {
    if (!paths.length) return;
    const one = paths.length === 1;
    const ok = await this.confirm.ask({
      title: 'Retirer de l\'archive',
      message: (one ? 'Retirer ce titre de l\'archive ?' : `Retirer ces ${paths.length} titres de l'archive ?`) +
        '\nLa copie archivée est effacée, sauf si un autre titre identique la partage. Le fichier sur le disque n\'est pas touché.',
      confirm: 'Retirer',
      danger: true,
    });
    if (!ok) return;
    const r = await this.gql.mutate<{ removeFromArchive: number }>(REMOVE, { paths }, { label: 'Retirer de l\'archive', refetch: ['ArchiveUsage'] });
    if (r.error) this.notify.error(r.error);
    else if (r.queued) this.notify.info('Retrait en attente du retour de la connexion.');
    else this.notify.success(r.data?.removeFromArchive === 1 ? 'Titre retiré de l\'archive.' : `${r.data?.removeFromArchive ?? 0} titres retirés de l'archive.`);
  }

  async measure(root: string | null): Promise<Job | null> {
    const r = await this.gql.mutate<{ measureArchive: Job }>(MEASURE, { root }, { label: 'Mesurer l\'archive', queueable: false });
    if (r.error) this.notify.error(r.error);
    if (r.data?.measureArchive) this.jobs.upsert(r.data.measureArchive);
    return r.data?.measureArchive ?? null;
  }

  async check(): Promise<Job | null> {
    const r = await this.gql.mutate<{ checkArchive: Job }>(CHECK, {}, { label: 'Vérifier l\'archive', queueable: false });
    if (r.error) this.notify.error(r.error);
    if (r.data?.checkArchive) this.jobs.upsert(r.data.checkArchive);
    return r.data?.checkArchive ?? null;
  }

  /** « Arrêter » vaut pour toutes les tâches de l'archive en cours. */
  async stop(): Promise<void> {
    const active = this.jobs.all().filter(j => (ARCHIVE_JOB_TYPES as readonly string[]).includes(j.type) && isActive(j));
    await Promise.all(active.map(j => this.jobs.cancel(j)));
  }

  private async ensureConfigured(): Promise<boolean> {
    const config = this.config() ?? (await this.loadConfig());
    if (config?.configured) return true;
    this.notify.info(UNCONFIGURED_TEXT);
    this.openPanelRequest.update(n => n + 1);
    return false;
  }

  private started(job: Job | undefined, error: string | undefined, message: string): void {
    if (error) {
      this.notify.error(error);
      return;
    }
    if (job) {
      this.jobs.upsert(job);
      this.notify.info(message);
    }
  }
}

/** Message de l'original quand rien n'est à restaurer parmi les titres retenus. */
export function nothingToRestore(plan: ArchivePlan): string {
  return plan.restorableElsewhere > 0
    ? `Aucun des titres affichés n'est à restaurer, mais ${plan.restorableElsewhere} le sont ailleurs dans la bibliothèque. ` +
      'Cochez « Titres absents du disque », ou videz la recherche, pour les faire apparaître.'
    : 'Rien à restaurer : tous les titres que l\'archive détient sont déjà sur le disque.';
}
