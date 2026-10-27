import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { firstValueFrom } from 'rxjs';
import { GqlService } from '../../../core/graphql/gql.service';
import { NotifyService } from '../../../core/ui/notify.service';
import { ConfirmService } from '../../../shared/ui';
import { baseName, bestCopyIndex, dirName, fileQuality, pathKey } from '../organize-rules';

/** Fichier du disque rapproché d'une piste Deezer (verdict de l'index de présence). */
export interface TrackDedupFile {
  path: string;
  reason: string;
  bitrate?: number | null;
  fileSize?: number | null;
  ext?: string | null;
}

/** Piste de la recherche et les fichiers qui lui répondent. */
export interface TrackDedupItem {
  key: string;
  title: string;
  artist: string;
  album?: string | null;
  files: TrackDedupFile[];
}

interface FileRow {
  path: string;
  name: string;
  folder: string;
  quality: string;
  qualityTip: string;
  reason: string;
  keep: boolean;
}

interface Group {
  key: string;
  title: string;
  subtitle: string;
  reason: string;
  files: FileRow[];
}

const DELETE = gql`
  mutation DeleteOrganizeFiles($paths: [String!]!) {
    deleteOrganizeFiles(paths: $paths) { deleted failed errors }
  }
`;

/**
 * Groupes de l'écran : un par piste, chaque fichier montré une seule fois
 * (sous la première piste qui le cite — deux cases ne décident pas du sort
 * d'un même fichier). Pré-cochage : rien si un seul fichier répond (une
 * ressemblance n'est pas une preuve) ; sinon tous sauf le meilleur exemplaire.
 */
export function buildTrackDedupGroups(items: TrackDedupItem[]): { groups: Group[]; checked: Set<string> } {
  const seen = new Set<string>();
  const groups: Group[] = [];
  const checked = new Set<string>();
  for (const item of items ?? []) {
    const files = (item.files ?? []).filter(f => {
      const k = pathKey(f.path);
      if (!f.path || seen.has(k)) return false;
      seen.add(k);
      return true;
    });
    if (files.length === 0) continue;
    const keep = files.length > 1 ? bestCopyIndex(files.map(f => ({ ...f, artist: item.artist }))) : -1;
    const rows = files.map((f, i): FileRow => ({
      path: f.path,
      name: baseName(f.path),
      folder: dirName(f.path),
      quality: fileQuality(f.path, f.ext, f.bitrate, f.fileSize),
      qualityTip: i === keep ? 'Meilleur exemplaire du groupe : conservé par défaut' : '',
      reason: f.reason ?? '',
      keep: i === keep,
    }));
    if (keep >= 0) rows.forEach((r, i) => i !== keep && checked.add(r.path));
    const reasons = [...new Set(files.map(f => f.reason).filter(r => !!r))];
    groups.push({
      key: item.key,
      title: item.title ?? '',
      subtitle: [item.artist, item.album].filter(p => !!p).join(' — '),
      reason: reasons.join(' · '),
      files: rows,
    });
  }
  return { groups, checked };
}

/** Écran « Doublons de la bibliothèque » (Dédoublonner… de la fenêtre de recherche). */
@Component({
  selector: 'dk-track-dedup-dialog',
  imports: [MatDialogModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title class="title">Doublons de la bibliothèque</h2>
    <mat-dialog-content class="content">
      <p class="sub">
        {{ groups.length }} piste(s) de la recherche répondent à {{ fileCount }} fichier(s) déjà sur le disque.
        Cochez les fichiers à supprimer : la piste redevient alors sélectionnable dans la recherche, prête à être
        extraite de nouveau. Les pistes dont un seul fichier répond ne sont pas cochées d'avance — une ressemblance
        n'est pas une preuve.
      </p>
      <div class="dk-table tree" role="tree">
        <div class="dk-thead head dk-hide-mobile">
          <span>Piste Deezer / fichiers du disque</span><span>Qualité</span><span>Motif / emplacement</span>
        </div>
        @for (g of groups; track g.key) {
          <div class="dk-row head-row track" role="treeitem" aria-expanded="true">
            <span class="t-title" [matTooltip]="g.title">{{ g.title }}</span>
            <span class="t-sub">{{ g.subtitle }}</span>
            <span class="t-reason dk-faint" [matTooltip]="g.reason">{{ g.reason }}</span>
          </div>
          @for (f of g.files; track f.path) {
            <label class="dk-row head-row file" role="treeitem" [class.keep]="f.keep">
              <span class="f-name">
                <input type="checkbox" class="dk-check" [checked]="checked().has(f.path)" (change)="toggle(f.path)" />
                <span class="name" [matTooltip]="f.path">{{ f.name }}</span>
              </span>
              <span class="f-quality" [matTooltip]="f.qualityTip">{{ f.quality }}@if (f.keep) { <span class="star"> ★</span> }</span>
              <span class="f-folder dk-faint" [matTooltip]="f.path + (f.reason ? '\n' + f.reason : '')">{{ f.folder }}</span>
            </label>
          }
        }
        @if (groups.length === 0) { <div class="dk-empty">Aucun fichier du disque ne répond aux pistes affichées.</div> }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions class="actions">
      <button class="control-btn" (click)="setAll(true)" [disabled]="busy()">Tout cocher</button>
      <button class="control-btn" (click)="setAll(false)" [disabled]="busy()">Tout décocher</button>
      <span class="dk-spacer"></span>
      <button class="control-btn" (click)="ref.close(undefined)" [disabled]="busy()">Fermer</button>
      <button class="danger-btn" (click)="confirmDelete()" [disabled]="count() === 0 || busy()">{{ deleteLabel() }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .title { color: var(--dk-primary); }
    .content { display: flex; flex-direction: column; gap: 12px; max-height: 70vh; }
    .sub { color: var(--dk-faint); font-size: 12px; margin: 0; }
    .tree { overflow: auto; }
    .head, .head-row { grid-template-columns: minmax(0, 2fr) minmax(140px, 1fr) minmax(0, 2fr); }
    .track { background: rgba(255, 255, 255, .03); font-weight: 600; }
    .track .t-title { color: var(--dk-warn); }
    .file { cursor: pointer; }
    .file .f-name { display: flex; align-items: center; gap: 10px; padding-left: 18px; }
    .file .name { overflow: hidden; text-overflow: ellipsis; }
    .file.keep .f-quality { color: var(--dk-success); }
    .star { color: var(--dk-primary); }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    @media (max-width: 899px) {
      .head-row { grid-template-columns: 1fr; row-gap: 2px; }
      .file .f-name { padding-left: 0; min-height: 40px; }
      .file .f-folder { white-space: normal; word-break: break-all; font-size: 11px; }
    }
  `,
})
export class TrackDedupDialog {
  private readonly items = inject<TrackDedupItem[]>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<TrackDedupDialog, string[] | undefined>);
  private readonly gql = inject(GqlService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  private readonly built = buildTrackDedupGroups(this.items);
  readonly groups = this.built.groups;
  readonly fileCount = this.groups.reduce((n, g) => n + g.files.length, 0);
  readonly checked = signal(new Set<string>(this.built.checked));
  readonly busy = signal(false);
  readonly count = computed(() => this.checked().size);
  readonly deleteLabel = computed(() => (this.count() ? `Supprimer les ${this.count()} fichier(s) cochés` : 'Supprimer les fichiers cochés'));

  toggle(path: string): void {
    this.checked.update(s => {
      const next = new Set(s);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }

  setAll(on: boolean): void {
    this.checked.set(on ? new Set(this.groups.flatMap(g => g.files.map(f => f.path))) : new Set());
  }

  async confirmDelete(): Promise<void> {
    const paths = [...this.checked()];
    if (paths.length === 0) {
      this.notify.info('Cochez au moins un fichier à supprimer.');
      return;
    }
    const ok = await this.confirm.ask({
      title: 'Confirmer la suppression',
      message: `Supprimer ${paths.length} fichier(s) du disque ?\nIls sont placés dans la corbeille de la bibliothèque.`,
      confirm: 'Supprimer',
      cancel: 'Non',
      danger: true,
    });
    if (!ok) return;
    this.busy.set(true);
    const r = await this.gql.mutate<{ deleteOrganizeFiles: { deleted: string[]; failed: string[]; errors: string[] } }>(
      DELETE, { paths }, { label: 'Supprimer des doublons', queueable: false });
    this.busy.set(false);
    if (r.error || !r.data) {
      this.notify.error(r.error ?? 'Suppression impossible.');
      return;
    }
    const result = r.data.deleteOrganizeFiles;
    let message = `${result.deleted.length} fichier(s) supprimé(s).`;
    if (result.errors.length) {
      message += '\n\nErreurs :\n' + result.errors.slice(0, 10).join('\n');
      if (result.errors.length > 10) message += `\n… et ${result.errors.length - 10} autre(s).`;
      this.notify.error(message);
    } else {
      this.notify.success(message);
    }
    this.ref.close(result.deleted);
  }
}

/**
 * Ouvre l'écran « Doublons de la bibliothèque » et supprime (corbeille) les
 * fichiers cochés. Renvoie les chemins effectivement supprimés, ou undefined
 * si l'écran est fermé sans suppression.
 */
export function openTrackDedupDialog(
  dialog: MatDialog,
  items: {
    key: string;
    title: string;
    artist: string;
    album?: string | null;
    files: { path: string; reason: string; bitrate?: number | null; fileSize?: number | null; ext?: string | null }[];
  }[],
): Promise<string[] | undefined> {
  const ref = dialog.open<TrackDedupDialog, TrackDedupItem[], string[] | undefined>(TrackDedupDialog, {
    data: items,
    width: 'min(1200px, 96vw)',
    maxWidth: '96vw',
    autoFocus: false,
  });
  return firstValueFrom(ref.afterClosed());
}
