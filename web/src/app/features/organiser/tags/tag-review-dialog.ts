import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GqlService } from '../../../core/graphql/gql.service';
import { Job, JobsService } from '../../../core/jobs/jobs.service';
import { NotifyService } from '../../../core/ui/notify.service';
import { ProgressBar, ConfirmService } from '../../../shared/ui';
import { LookupFile, PROPOSALS, WRITE_TAGS } from '../organiser.queries';

/** Proposition de tags du Catalogue (événement job.item de la tâche « tags.find »). */
export interface TagProposal {
  path: string;
  confidence: string;
  score: number;
  source: string;
  current: Record<string, string | null> | null;
  proposed: Record<string, string | null> | null;
  missing: string[] | null;
  contradicted: string[] | null;
  coverUrl: string | null;
  deezerId: number | null;
  note: string | null;
}

export interface TagReviewData {
  sessionId: string;
  job: Job;
  files: LookupFile[];
  /** Le fichier a-t-il un tag indispensable (titre, artiste, album) manquant ? Sert au libellé « sans correspondance ». */
  acoustIdAvailable?: boolean;
}

interface Row {
  file: LookupFile;
  state: 'pending' | 'none' | 'found';
  proposal: TagProposal | null;
  sure: boolean;
  fills: [string, string][];
  corrects: [string, string][];
}

const FIELD_LABELS: Record<string, string> = {
  title: 'titre', artist: 'artiste', album: 'album', albumartist: 'artiste album', genre: 'genre', date: 'année',
  track: 'piste', tracktotal: 'total pistes', disc: 'disque',
};
const CORE = new Set(['title', 'artist', 'album']);

const label = (f: string) => FIELD_LABELS[f] ?? f;
const isSure = (p: TagProposal) => /sure|sûr|certain|high/i.test(p.confidence ?? '') || (!/probable|prob/i.test(p.confidence ?? '') && p.score >= 0.8);

/**
 * Écran de revue « Tags manquants » : une ligne par fichier soumis, remplie au fil de la recherche (la tâche
 * « tags.find » du Catalogue tourne côté serveur : fermer la page ou perdre la connexion ne l'interrompt pas, les
 * propositions déjà reçues sont relues à la réouverture). Seuls les champs VIDES sont écrits, sauf si
 * « Corriger aussi… » est coché. « Écrire » est actif dès la première proposition.
 */
@Component({
  selector: 'dk-tag-review-dialog',
  imports: [MatDialogModule, MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title class="title">Tags manquants</h2>
    <mat-dialog-content class="content">
      <p class="sub">
        {{ data.files.length }} fichier(s) ne portent pas tous leurs tags : le catalogue (Deezer, puis MusicBrainz et l'empreinte
        acoustique pour un titre, un artiste ou un album manquant) est interrogé fichier par fichier. Les correspondances sûres sont
        cochées d'office ; les probables sont à vérifier. Par défaut, seuls les champs vides sont renseignés. Rien n'est écrit sans validation.
      </p>
      <p class="state" [class.warn]="stopping()">{{ stateText() }}</p>
      @if (running()) { <dk-progress [value]="progress()" [label]="examined() + ' / ' + data.files.length" /> }
      <label class="dk-check correct" matTooltip="Par défaut, seuls les champs vides sont renseignés. Cochez pour remplacer aussi les tags qui contredisent la correspondance retenue — un artiste erroné, par exemple. Les champs concernés sont signalés « ⟳ » dans le tableau.">
        <input type="checkbox" class="dk-check" [checked]="correct()" (change)="correct.set(!correct())" /> Corriger aussi les tags que la correspondance contredit
      </label>
      <div class="dk-table list">
        <div class="dk-thead row dk-hide-mobile"><span>Écrire</span><span>Fichier</span><span>Tags actuels</span><span>Tags trouvés</span><span>Champs comblés</span><span>Source</span><span>Correspondance</span></div>
        @for (r of rows(); track r.file.dbPath) {
          <div class="dk-row row" [class.dim]="r.state !== 'found'">
            <span class="c-check">
              @if (r.state === 'found') { <input type="checkbox" class="dk-check" [checked]="checked().has(r.file.dbPath)" (change)="toggle(r.file.dbPath)" [attr.aria-label]="'Écrire ' + r.file.relative" /> }
            </span>
            <span class="c-file" [matTooltip]="r.file.path">{{ r.file.relative }}</span>
            <span class="c-cur">{{ current(r.file) }}</span>
            <span class="c-found" [class.sure]="r.sure" [class.prob]="r.state === 'found' && !r.sure" [matTooltip]="foundTip(r)">{{ found(r) }}</span>
            <span class="c-fills">{{ fills(r) }}</span>
            <span class="c-src">{{ source(r) }}</span>
            <span class="c-match">{{ match(r) }}</span>
          </div>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions class="actions">
      <button class="control-btn" (click)="setAll(true)">Tout cocher</button>
      <button class="control-btn" (click)="setAll(false)">Tout décocher</button>
      <span class="dk-spacer"></span>
      @if (running()) { <button class="control-btn" (click)="stop()" [disabled]="stopping()">Arrêter la recherche</button> }
      <button class="control-btn" (click)="cancel()" [disabled]="busy()">Annuler</button>
      <button class="primary-btn" (click)="write()" [disabled]="busy() || checked().size === 0">Écrire les tags cochés</button>
    </mat-dialog-actions>
  `,
  styles: `
    .title { color: var(--dk-primary); }
    .content { display: flex; flex-direction: column; gap: 10px; max-height: 72vh; }
    .sub { color: var(--dk-faint); font-size: 12px; margin: 0; }
    .state { margin: 0; font-size: 13px; color: var(--dk-muted); }
    .state.warn { color: var(--dk-warn); }
    .list { overflow: auto; }
    .row { grid-template-columns: 50px minmax(0, 2.4fr) minmax(0, 1.8fr) minmax(0, 2fr) minmax(0, 1.4fr) 100px minmax(0, 1.8fr); }
    .row > * { white-space: normal; }
    .c-found.sure { color: var(--dk-primary); }
    .c-found.prob { color: var(--dk-warn); }
    .dim .c-file, .dim .c-cur { color: var(--dk-disabled); }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    @media (max-width: 899px) {
      .row { grid-template-columns: 40px 1fr; row-gap: 3px; padding: 10px 12px; }
      .row > * { grid-column: 2; font-size: 12px; }
      .row > .c-check { grid-column: 1; grid-row: 1 / span 6; }
      .c-file { font-weight: 600; font-size: 13px !important; }
    }
  `,
})
export class TagReviewDialog implements OnInit {
  readonly data = inject<TagReviewData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<TagReviewDialog, number | undefined>);
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly results = signal(new Map<string, { status: string; proposal: TagProposal | null }>());
  readonly checked = signal(new Set<string>());
  readonly correct = signal(false);
  readonly busy = signal(false);
  readonly stopping = signal(false);
  private readonly job = this.jobs.get(this.data.job.id);
  private readonly liveJob = computed(() => this.job() ?? this.data.job);

  readonly running = computed(() => this.liveJob().status === 'QUEUED' || this.liveJob().status === 'RUNNING');
  readonly examined = computed(() => this.results().size);
  readonly progress = computed(() => (this.data.files.length ? this.examined() / this.data.files.length : 0));

  readonly rows = computed<Row[]>(() => {
    const done = this.results();
    return this.data.files.map(file => {
      const r = done.get(file.dbPath) ?? done.get(file.path);
      if (!r) return { file, state: 'pending', proposal: null, sure: false, fills: [], corrects: [] } as Row;
      const p = r.proposal;
      if (!p || !p.proposed) return { file, state: 'none', proposal: null, sure: false, fills: [], corrects: [] } as Row;
      const pair = (fields: string[] | null) => (fields ?? []).filter(f => p.proposed?.[f]).map(f => [f, p.proposed![f]!] as [string, string]);
      return { file, state: 'found', proposal: p, sure: isSure(p), fills: pair(p.missing), corrects: pair(p.contradicted) } as Row;
    });
  });

  readonly stateText = computed(() => {
    const rows = this.rows();
    const found = rows.filter(r => r.state === 'found');
    const sure = found.filter(r => r.sure).length;
    const total = this.data.files.length;
    if (this.running()) {
      return this.stopping() ? `Arrêt de la recherche… ${this.examined()} / ${total} fichiers examinés` : `Recherche en cours… ${this.examined()} / ${total} fichiers`;
    }
    let text = `${sure} correspondance(s) sûre(s) · ${found.length - sure} probable(s) · ${this.examined() - found.length} sans réponse`;
    if (this.examined() < total) text += ` · ${total - this.examined()} non examiné(s) — recherche arrêtée`;
    return text;
  });

  ngOnInit(): void {
    // Propositions déjà reçues (rechargement de la page, coupure) puis flux en direct.
    this.gql.query<{ organizeProposals: { itemKey: string; status: string; path: string | null; data: TagProposal | null }[] }>(PROPOSALS, { jobId: this.data.job.id })
      .then(d => d.organizeProposals.forEach(p => this.add(p.itemKey, p.status, p.data)))
      .catch(() => undefined);
    this.jobs.items({ jobId: this.data.job.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => d?.jobItem && d.jobItem.type === 'tags.find' && this.add(d.jobItem.itemKey, d.jobItem.status, d.jobItem.data as TagProposal | null),
      error: () => undefined,
    });
  }

  private add(key: string, status: string, proposal: TagProposal | null): void {
    this.results.update(m => new Map(m).set(key, { status, proposal: proposal && proposal.proposed ? proposal : null }));
    if (proposal?.proposed && isSure(proposal)) this.checked.update(s => new Set(s).add(key));
  }

  current(f: LookupFile): string {
    const parts = [f.titleTagged ? f.title : '', f.artist, f.album].filter(p => !!p);
    return parts.length ? parts.join(' · ') : '(aucun)';
  }

  found(r: Row): string {
    if (r.state === 'pending') return 'en attente…';
    if (r.state === 'none') return 'aucune correspondance trouvée';
    const p = r.proposal!.proposed!;
    return [p['title'], p['artist'], p['album']].filter(v => !!v).join(' · ') || [...r.fills, ...r.corrects].map(([f, v]) => `${label(f)} = ${v}`).join(' · ');
  }

  foundTip(r: Row): string {
    const pr = r.proposal;
    if (!pr) return '';
    const p = pr.proposed ?? {};
    return [`Titre : ${p['title'] ?? ''}`, `Artiste : ${p['artist'] ?? ''}`, `Album : ${p['album'] ?? ''}`, `Source : ${pr.source}`, pr.note ?? ''].filter(l => !!l).join('\n');
  }

  fills(r: Row): string {
    if (r.state !== 'found') return r.state === 'none' ? '—' : '';
    const fills = r.fills.map(([f, v]) => (CORE.has(f) ? label(f) : `${label(f)} = ${v}`)).join(' · ');
    const fix = r.corrects.map(([f]) => label(f)).join(' · ');
    const groups = [fills];
    if (fix) groups.push(this.correct() ? `⟳ ${fix}` : `(⟳ ${fix} ignoré)`);
    return groups.filter(g => !!g).join('   ');
  }

  source(r: Row): string {
    if (r.state === 'found') return r.proposal!.source;
    if (r.state === 'pending') return '';
    return r.file.coreMissing ? 'Deezer + MusicBrainz' + (this.data.acoustIdAvailable ? ' + AcoustID' : '') : 'Deezer';
  }

  match(r: Row): string {
    if (r.state === 'pending') return '';
    if (r.state === 'none') return r.file.coreMissing ? 'tags, nom de fichier et dossier trop pauvres' : 'le catalogue ne reconnaît pas cet enregistrement';
    const p = r.proposal!;
    return `${r.sure ? 'sûre' : 'probable'} (${p.score.toFixed(2)})${p.note ? ' — ' + p.note : ''}`;
  }

  toggle(path: string): void {
    this.checked.update(s => {
      const next = new Set(s);
      if (!next.delete(path)) next.add(path);
      return next;
    });
  }

  setAll(on: boolean): void {
    this.checked.set(on ? new Set(this.rows().filter(r => r.state === 'found').map(r => r.file.dbPath)) : new Set());
  }

  async stop(): Promise<void> {
    this.stopping.set(true);
    await this.jobs.cancel(this.data.job);
  }

  async cancel(): Promise<void> {
    if (this.running()) await this.jobs.cancel(this.data.job);
    this.ref.close(undefined);
  }

  /** Tags à écrire pour une ligne cochée : champs vides comblés, plus les champs contredits si demandé. */
  private tagsOf(r: Row): { field: string; value: string }[] {
    const all = [...r.fills, ...(this.correct() ? r.corrects : [])];
    return all.map(([field, value]) => ({ field, value }));
  }

  async write(): Promise<void> {
    const chosen = this.rows().filter(r => r.state === 'found' && this.checked().has(r.file.dbPath));
    if (chosen.length === 0) {
      this.notify.info('Cochez au moins une correspondance à écrire.');
      return;
    }
    const fieldCount = chosen.reduce((n, r) => n + this.tagsOf(r).length, 0);
    const replaced = this.correct() ? chosen.reduce((n, r) => n + r.corrects.length, 0) : 0;
    let message = `Écrire ${fieldCount} tag(s) dans ${chosen.length} fichier(s) ?`;
    const left = this.data.files.length - this.examined();
    if (this.running() && left > 0) message += `\nLa recherche n'a pas parcouru toute la liste : ${left} fichier(s) n'ont pas été interrogés et resteront tels quels.`;
    message += replaced > 0
      ? `\nDont ${replaced} tag(s) existant(s) remplacé(s) par une valeur différente — l'ancienne valeur sera perdue.`
      : `\nSeuls des champs vides sont renseignés ; aucun tag existant n'est remplacé.`;
    if (!(await this.confirm.ask({ title: 'Écrire les tags', message, confirm: 'Oui', cancel: 'Non' }))) return;

    this.busy.set(true);
    if (this.running()) await this.jobs.cancel(this.data.job);
    const items = chosen.map(r => ({ path: r.file.dbPath, fields: this.tagsOf(r), overwriteExisting: this.correct() && r.corrects.length > 0 }));
    const res = await this.gql.mutate<{ writeOrganizeTags: Job }>(WRITE_TAGS, { sessionId: this.data.sessionId, items }, { label: 'Écrire des tags', queueable: false });
    if (!res.data?.writeOrganizeTags) {
      this.busy.set(false);
      this.notify.error(res.error ?? 'Écriture impossible.');
      return;
    }
    const done = await this.waitFor(res.data.writeOrganizeTags);
    this.busy.set(false);
    const summary = (done.result as { summary?: string; written?: number } | null) ?? {};
    if (done.status === 'FAILED') this.notify.error(done.error ?? 'Échec de l\'écriture.');
    else this.notify.info(summary.summary ?? 'Tags écrits.');
    this.ref.close(summary.written ?? 0);
  }

  /** Attend la fin d'une tâche (suivie par JobsService, rattrapée à la reconnexion). */
  private async waitFor(job: Job): Promise<Job> {
    this.jobs.upsert(job);
    const signalJob = this.jobs.get(job.id);
    for (;;) {
      const current = signalJob() ?? job;
      if (current.status === 'SUCCEEDED' || current.status === 'FAILED' || current.status === 'CANCELLED') return current;
      await new Promise(r => setTimeout(r, 400));
    }
  }
}

/** Ouvre la revue des tags ; rend le nombre de fichiers écrits, ou undefined si annulée. */
export function openTagReviewDialog(dialog: MatDialog, data: TagReviewData): Promise<number | undefined> {
  const ref = dialog.open<TagReviewDialog, TagReviewData, number | undefined>(TagReviewDialog, {
    data, width: 'min(1300px, 97vw)', maxWidth: '97vw', autoFocus: false, disableClose: true,
  });
  return firstValueFrom(ref.afterClosed());
}
