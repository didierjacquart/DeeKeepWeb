import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GqlService } from '../../../core/graphql/gql.service';
import { NotifyService } from '../../../core/ui/notify.service';
import { ConfirmService } from '../../../shared/ui';
import { DELETE_FILES, DuplicatesResult } from '../organiser.queries';

export interface FileDedupData {
  result: DuplicatesResult;
  sessionId: string;
}

/**
 * Écran « Fichiers en double » (fichier/fichier) : un groupe = des fichiers rapprochés par leur nom ou leurs
 * tags. L'exemplaire gardé par défaut (★) est choisi par le serveur (règle unique : qualité, puis rangement) ;
 * tous les autres sont cochés. La suppression va à la corbeille de la bibliothèque.
 */
@Component({
  selector: 'dk-file-dedup-dialog',
  imports: [MatDialogModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title class="title">Fichiers en double</h2>
    <mat-dialog-content class="content">
      <p class="sub">
        {{ data.result.fileCount }} fichiers dans {{ data.result.groups.length }} groupe(s). Cochez ceux à supprimer.
        Par défaut, chaque groupe conserve (décoché) le meilleur exemplaire : le meilleur débit, puis le fichier le plus
        complet, au format cible ({{ data.result.targetFormat }}), rangé dans le dossier de l'artiste, hors « Divers »,
        à l'emplacement le moins profond.
      </p>
      <div class="dk-table list">
        <div class="dk-thead row dk-hide-mobile"><span>Suppr.</span><span>Groupe</span><span>Titre</span><span>Artiste</span><span>Format / débit</span><span>Fichier</span></div>
        @for (g of data.result.groups; track g.number) {
          @for (f of g.files; track f.path) {
            <label class="dk-row row" [class.keep]="f.keep">
              <span><input type="checkbox" class="dk-check" [checked]="checked().has(f.dbPath)" (change)="toggle(f.dbPath)" /></span>
              <span class="grp" [matTooltip]="f.keep ? 'Exemplaire conservé par défaut (meilleure qualité)' : ''">{{ g.number }}{{ f.keep ? ' ★' : '' }}</span>
              <span class="ttl">{{ f.title }}</span>
              <span class="art">{{ f.artist }}</span>
              <span class="fmt">{{ label(f) }}</span>
              <span class="path dk-faint" [matTooltip]="f.path">{{ f.path }}</span>
            </label>
          }
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions class="actions">
      <button class="control-btn" (click)="setAll(true)">Tout cocher</button>
      <button class="control-btn" (click)="setAll(false)">Tout décocher</button>
      <span class="dk-spacer"></span>
      <button class="control-btn" (click)="ref.close(undefined)" [disabled]="busy()">Annuler</button>
      <button class="danger-btn" (click)="remove()" [disabled]="busy() || checked().size === 0">Supprimer les fichiers cochés</button>
    </mat-dialog-actions>
  `,
  styles: `
    .title { color: var(--dk-primary); }
    .content { display: flex; flex-direction: column; gap: 12px; max-height: 70vh; }
    .sub { color: var(--dk-faint); font-size: 12px; margin: 0; }
    .list { overflow: auto; }
    .row { grid-template-columns: 56px 56px minmax(0, 2fr) minmax(0, 1.5fr) 130px minmax(0, 3fr); cursor: pointer; }
    .keep .grp { color: var(--dk-primary); font-weight: 700; }
    @media (max-width: 899px) {
      .row { grid-template-columns: 40px 1fr; row-gap: 2px; padding: 10px 12px; }
      .row > :nth-child(2) { grid-column: 2; }
      .row > :nth-child(n+3) { grid-column: 2; white-space: normal; font-size: 12px; }
      .row > :first-child { grid-row: 1 / span 5; }
    }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
  `,
})
export class FileDedupDialog {
  readonly data = inject<FileDedupData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<FileDedupDialog, string[] | undefined>);
  private readonly gql = inject(GqlService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);

  readonly checked = signal(new Set(this.data.result.groups.flatMap(g => g.files.filter(f => !f.keep).map(f => f.dbPath))));
  readonly busy = signal(false);
  readonly count = computed(() => this.checked().size);

  label(f: { format: string; bitrate: number }): string {
    const fmt = f.format || '?';
    return f.bitrate ? `${fmt} · ${f.bitrate} kbps` : fmt;
  }

  toggle(path: string): void {
    this.checked.update(s => {
      const next = new Set(s);
      if (!next.delete(path)) next.add(path);
      return next;
    });
  }

  setAll(on: boolean): void {
    this.checked.set(on ? new Set(this.data.result.groups.flatMap(g => g.files.map(f => f.dbPath))) : new Set());
  }

  async remove(): Promise<void> {
    const paths = [...this.checked()];
    if (paths.length === 0) {
      this.notify.info('Cochez au moins un fichier à supprimer.');
      return;
    }
    const ok = await this.confirm.ask({
      title: 'Confirmer la suppression',
      message: `Supprimer ${paths.length} fichier(s) du disque ?\nIls sont placés dans la corbeille de la bibliothèque (.deekeep-trash).`,
      confirm: 'Supprimer',
      cancel: 'Non',
      danger: true,
    });
    if (!ok) return;
    this.busy.set(true);
    const r = await this.gql.mutate<{ deleteOrganizeFiles: { deleted: string[]; failed: string[]; errors: string[] } }>(
      DELETE_FILES, { paths, sessionId: this.data.sessionId }, { label: 'Supprimer des doublons', queueable: false });
    this.busy.set(false);
    const result = r.data?.deleteOrganizeFiles;
    if (!result) {
      this.notify.error(r.error ?? 'Suppression impossible.');
      return;
    }
    let message = `${result.deleted.length} fichier(s) supprimé(s).`;
    if (result.errors.length) message += '\n\nErreurs :\n' + result.errors.slice(0, 10).join('\n');
    (result.errors.length ? this.notify.error : this.notify.success).call(this.notify, message);
    this.ref.close(result.deleted);
  }
}

/** Ouvre l'écran fichier/fichier ; rend les chemins supprimés (ou undefined si fermé sans suppression). */
export function openFileDedupDialog(dialog: MatDialog, data: FileDedupData): Promise<string[] | undefined> {
  const ref = dialog.open<FileDedupDialog, FileDedupData, string[] | undefined>(FileDedupDialog, {
    data, width: 'min(1200px, 96vw)', maxWidth: '96vw', autoFocus: false,
  });
  return firstValueFrom(ref.afterClosed());
}
