import { ChangeDetectionStrategy, Component, Injectable, computed, inject, input } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

/** Barre de progression au dégradé navy → cyan (indéterminée si <c>value</c> est nul). */
@Component({
  selector: 'dk-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dk-progress" [class.large]="large()" [class.indeterminate]="value() == null">
      <div class="fill" [style.width.%]="percent()"></div>
      @if (label() !== null) { <span class="label">{{ label() ?? percent() + ' %' }}</span> }
    </div>
  `,
})
export class ProgressBar {
  /** 0..1, ou null pour une progression indéterminée. */
  readonly value = input<number | null>(null);
  readonly label = input<string | null | undefined>(undefined);
  readonly large = input(false);
  readonly percent = computed(() => Math.round((this.value() ?? 0) * 100));
}

export type Presence = 'PRESENT' | 'POSSIBLE' | 'ABSENT' | 'PENDING';

/** Pastille ✓ ≈ ○ ⏳ : ce que le dossier de dépôt contient déjà. */
@Component({
  selector: 'dk-presence',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="dk-badge" [class]="cls()" [matTooltip]="tooltip() ?? ''">{{ glyph() }}</span>`,
})
export class PresenceBadge {
  readonly state = input<Presence | null | undefined>('PENDING');
  readonly tooltip = input<string | null | undefined>(null);
  readonly glyph = computed(() => ({ PRESENT: '✓', POSSIBLE: '≈', ABSENT: '○', PENDING: '⏳' })[this.state() ?? 'PENDING']);
  readonly cls = computed(() => 'dk-badge ' + (this.state() ?? 'PENDING').toLowerCase());
}

export interface ConfirmData {
  title: string;
  message: string;
  confirm?: string;
  cancel?: string;
  danger?: boolean;
}

@Component({
  selector: 'dk-confirm-dialog',
  imports: [MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p class="msg">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button class="control-btn" (click)="ref.close(false)">{{ data.cancel ?? 'Annuler' }}</button>
      <button [class]="data.danger ? 'danger-btn' : 'primary-btn'" (click)="ref.close(true)" cdkFocusInitial>{{ data.confirm ?? 'Confirmer' }}</button>
    </mat-dialog-actions>
  `,
  styles: `.msg { white-space: pre-line; color: var(--dk-muted); margin: 0; }`,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ConfirmDialog, boolean>);
}

/** Boîtes de question (les messages de l'original sont repris dans les pages). */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  async ask(data: ConfirmData): Promise<boolean> {
    const ref = this.dialog.open(ConfirmDialog, { data, width: 'min(480px, 94vw)' });
    return (await firstValueFrom(ref.afterClosed())) === true;
  }
}

/** URL d'image passée par le cache du serveur (fonctionne hors ligne via le service worker). */
export function imageUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  if (url.startsWith('/')) return url;
  return `/img?u=${encodeURIComponent(url)}`;
}

/** Flux audio d'un fichier de la bibliothèque (chemin tel que stocké en base). */
export function mediaUrl(dbPath: string): string {
  return `/media/track?path=${encodeURIComponent(dbPath)}`;
}

/** Flux d'un titre lu depuis l'archive (fichier absent du disque). */
export function archiveMediaUrl(dbPath: string): string {
  return `/archive-media/track?path=${encodeURIComponent(dbPath)}`;
}

/** Jaquette stockée en base (table covers). */
export function coverUrl(sha1: string | null | undefined): string | null {
  return sha1 ? `/covers/${sha1}` : null;
}

/** Extrait Deezer de 30 s, relayé par le serveur. */
export function previewUrl(deezerId: number | string): string {
  return `/preview/${deezerId}`;
}
