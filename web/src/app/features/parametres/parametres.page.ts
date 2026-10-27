import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { AuthService } from '../../core/auth/auth.service';
import { GqlService } from '../../core/graphql/gql.service';
import { JOB_FIELDS, Job, JobsService, isActive } from '../../core/jobs/jobs.service';
import { NotifyService } from '../../core/ui/notify.service';
import { formatBytes } from '../../shared/pipes';
import { ConfirmService, ProgressBar } from '../../shared/ui';
import { openFsBrowser } from '../../shared/fs-browser/fs-browser-dialog';

interface TableCount { table: string; rows: number; deleted: number }
interface DatabaseInfo {
  path: string; sizeBytes: number; tables: TableCount[]; historyCount: number; coverBytes: number;
  downloadDir: string | null; localDownloadDir: string | null; presenceReady: boolean; presenceFiles: number; desktopDataDir: string | null;
}
type Settings = Record<string, unknown>;

const SETTINGS_Q = gql`query Settings { settings databaseInfo { path sizeBytes tables { table rows deleted } historyCount coverBytes downloadDir localDownloadDir presenceReady presenceFiles desktopDataDir } }`;
const UPDATE = gql`mutation UpdateSettings($values: Any!) { updateSettings(values: $values) }`;
const RESCAN = gql`mutation RescanLibrary { rescanLibrary { ${JOB_FIELDS} } }`;
const IMPORT = gql`mutation ImportDesktopData($dataDir: String) { importDesktopData(dataDir: $dataDir) { ${JOB_FIELDS} } }`;
const SETTINGS_CHANGED = gql`subscription SettingsChanged { settingsChanged }`;

const BROWSERS = ['Aucun (anonyme)', 'Chrome', 'Edge', 'Firefox', 'Brave', 'Opera', 'Vivaldi'];
const FORMATS = ['MP3', 'FLAC', 'WAV', 'M4A'];
/** Qualités proposées selon le format (le format est appliqué d'abord, puis la qualité). */
export function qualitiesFor(format: string): string[] {
  return format === 'FLAC' || format === 'WAV' ? ['Lossless (Qualité Source)'] : ['320 kbps (HQ)', '128 kbps (Standard)'];
}

/**
 * Paramètres d'exportation et de rangement, base de données, compte. Chaque changement est
 * enregistré aussitôt (comme l'original) ; hors ligne, il part dans la file d'actions en attente.
 */
@Component({
  selector: 'dk-parametres-page',
  imports: [MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './parametres.page.html',
  styleUrl: './parametres.page.scss',
})
export class SettingsPage {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly formats = FORMATS;
  readonly browsers = BROWSERS;
  readonly bytes = formatBytes;
  readonly diversRange = [2, 3, 4, 5, 6, 7, 8, 9, 10];

  readonly settings = signal<Settings>({});
  readonly db = signal<DatabaseInfo | null>(null);
  readonly loaded = signal(false);
  readonly stale = signal(false);
  readonly saving = signal(false);

  readonly format = computed(() => this.str('file_format', 'MP3'));
  readonly qualities = computed(() => qualitiesFor(this.format()));
  readonly quality = computed(() => {
    const current = this.str('quality', '320 kbps (HQ)');
    return this.qualities().includes(current) ? current : this.qualities()[0];
  });

  /** Tâches de collecte et d'import : l'état survit à la navigation (la tâche tourne sur le serveur). */
  private readonly libraryJobs = this.jobs.byType('library.scan', 'library.import');
  readonly scanJob = computed<Job | undefined>(() => this.libraryJobs().find(j => j.type === 'library.scan'));
  readonly importJob = computed<Job | undefined>(() => this.libraryJobs().find(j => j.type === 'library.import'));
  readonly scanning = computed(() => !!this.scanJob() && isActive(this.scanJob()!));
  readonly importing = computed(() => !!this.importJob() && isActive(this.importJob()!));

  // Mot de passe.
  readonly newPassword = signal('');
  readonly passwordMessage = signal<string | null>(null);

  constructor() {
    this.gql.watch<{ settings: Settings; databaseInfo: DatabaseInfo }>(SETTINGS_Q).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(s => {
      if (s.data?.settings) {
        this.settings.set(s.data.settings);
        this.loaded.set(true);
      }
      if (s.data?.databaseInfo) this.db.set(s.data.databaseInfo);
      this.stale.set(s.stale);
    });
    this.gql.subscribe<{ settingsChanged: Settings | null }>(SETTINGS_CHANGED).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => d?.settingsChanged && this.settings.set(d.settingsChanged),
      error: () => undefined,
    });
  }

  str(key: string, fallback = ''): string {
    const v = this.settings()[key];
    return typeof v === 'string' ? v : fallback;
  }

  bool(key: string, fallback = false): boolean {
    const v = this.settings()[key];
    return typeof v === 'boolean' ? v : fallback;
  }

  num(key: string, fallback: number): number {
    const v = this.settings()[key];
    return typeof v === 'number' ? v : fallback;
  }

  async save(values: Settings, label = 'Enregistrer les réglages'): Promise<void> {
    // Affichage immédiat ; le serveur confirme (ou la file hors ligne rejoue plus tard).
    this.settings.update(s => ({ ...s, ...values }));
    this.saving.set(true);
    const r = await this.gql.mutate<{ updateSettings: Settings }>(UPDATE, { values }, { label });
    this.saving.set(false);
    if (r.error) this.notify.error(r.error);
    else if (r.data?.updateSettings) this.settings.set(r.data.updateSettings);
    else if (r.queued) this.notify.info('Réglage mis en attente : il sera enregistré au retour de la connexion.');
  }

  setFormat(format: string): void {
    // Le format est appliqué d'abord, ce qui reconstruit la liste des qualités ; la qualité suit.
    const qualities = qualitiesFor(format);
    const quality = qualities.includes(this.str('quality')) ? this.str('quality') : qualities[0];
    void this.save({ file_format: format, quality }, "Changer le format d'export");
  }

  async chooseDownloadDir(): Promise<void> {
    const picked = await openFsBrowser(this.dialog, { start: this.db()?.localDownloadDir ?? undefined, mode: 'folder', title: "Dossier d'exportation" });
    if (!picked?.length) return;
    await this.save({ download_dir: picked[0] }, 'Changer le dossier de sortie');
    this.notify.success(`Musique enregistrée dans : ${picked[0]}`);
  }

  async chooseCookies(): Promise<void> {
    const picked = await openFsBrowser(this.dialog, { start: this.db()?.localDownloadDir ?? undefined, mode: 'files', title: 'Fichier cookies.txt (format Netscape)' });
    if (picked?.length) await this.save({ cookies_file: picked[0] }, 'Choisir le fichier de cookies');
  }

  async rescan(): Promise<void> {
    const r = await this.gql.mutate<{ rescanLibrary: Job }>(RESCAN, {}, { label: 'Relire la bibliothèque', queueable: false });
    if (r.error) this.notify.error(r.error);
    else if (r.data) this.jobs.upsert(r.data.rescanLibrary);
  }

  async importDesktop(): Promise<void> {
    const dir = this.db()?.desktopDataDir ?? '';
    const ok = await this.confirm.ask({
      title: "Importer depuis l'application desktop",
      message: `Reprendre les réglages et l'historique des téléchargements de l'application desktop ?\nDossier lu sur le serveur : ${dir}\nLes clés secrètes ne sont pas importées.`,
      confirm: 'Importer',
    });
    if (!ok) return;
    const r = await this.gql.mutate<{ importDesktopData: Job }>(IMPORT, {}, { label: "Importer depuis l'application desktop", queueable: false });
    if (r.error) this.notify.error(r.error);
    else if (r.data) this.jobs.upsert(r.data.importDesktopData);
  }

  cancel(job: Job): void {
    void this.jobs.cancel(job);
  }

  async changePassword(): Promise<void> {
    const error = await this.auth.changePassword(this.newPassword());
    this.passwordMessage.set(error ?? 'Mot de passe modifié.');
    if (!error) this.newPassword.set('');
  }

  async logout(): Promise<void> {
    await this.auth.logout();
    location.assign('/login');
  }
}
