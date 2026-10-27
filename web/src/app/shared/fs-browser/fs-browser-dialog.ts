import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { gql } from '@apollo/client';
import { firstValueFrom } from 'rxjs';
import { GqlService } from '../../core/graphql/gql.service';
import { formatBytes } from '../pipes';

export interface FsBrowserOptions {
  /** Dossier de départ (chemin local du serveur) ; à défaut, la première racine autorisée. */
  start?: string;
  mode: 'folder' | 'files';
  audioOnly?: boolean;
  title?: string;
}

interface FsEntry { name: string; localPath: string; isDirectory: boolean; size: number | null; mtime: number | null; isAudio: boolean }
interface FsListing { localPath: string; parent: string | null; entries: FsEntry[]; roots: string[] }

const BROWSE = gql`
  query BrowseFolder($path: String, $audioOnly: Boolean!) {
    browseFolder(path: $path, audioOnly: $audioOnly) {
      localPath parent roots entries { name localPath isDirectory size mtime isAudio }
    }
  }
`;

/**
 * Navigation dans les dossiers du serveur (racines autorisées uniquement).
 * Mode « folder » : on choisit le dossier affiché ; mode « files » : multi-sélection de fichiers
 * (un dossier coché est aussi accepté : son contenu audio sera lu récursivement par le serveur).
 */
@Component({
  selector: 'dk-fs-browser',
  imports: [MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title ?? (data.mode === 'folder' ? 'Choisir un dossier' : 'Choisir des fichiers') }}</h2>
    <mat-dialog-content class="content">
      @if (listing(); as l) {
        <div class="bar">
          <button class="icon-btn" [disabled]="!l.parent" (click)="go(l.parent)" aria-label="Dossier parent">↑</button>
          <span class="path" [title]="l.localPath">{{ l.localPath || 'Racines' }}</span>
        </div>
        @if (l.roots.length > 1) {
          <div class="roots">
            @for (r of l.roots; track r) { <button class="control-btn small" (click)="go(r)">{{ r }}</button> }
          </div>
        }
        <ul class="list">
          @for (e of l.entries; track e.localPath) {
            <li [class.selected]="picked().has(e.localPath)">
              @if (data.mode === 'files') {
                <input type="checkbox" class="dk-check" [checked]="picked().has(e.localPath)" (change)="toggle(e)" [attr.aria-label]="e.name" />
              }
              <button class="entry" (click)="open(e)" (dblclick)="e.isDirectory && go(e.localPath)">
                <span class="ico">{{ e.isDirectory ? '📁' : e.isAudio ? '♪' : '·' }}</span>
                <span class="name">{{ e.name }}</span>
                @if (!e.isDirectory && e.size != null) { <span class="size">{{ bytes(e.size) }}</span> }
              </button>
            </li>
          } @empty { <li class="empty">Dossier vide</li> }
        </ul>
      } @else if (error()) {
        <p class="dk-error-text">{{ error() }}</p>
      } @else { <p class="dk-faint">Chargement…</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button class="control-btn" (click)="ref.close(undefined)">Annuler</button>
      @if (data.mode === 'folder') {
        <button class="primary-btn" [disabled]="!listing()?.localPath" (click)="ref.close([listing()!.localPath])">Choisir ce dossier</button>
      } @else {
        <button class="primary-btn" [disabled]="picked().size === 0" (click)="ref.close([...picked()])">Ajouter ({{ picked().size }})</button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .content { min-width: min(560px, 86vw); max-height: 60vh; }
    .bar { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
    .path { font-size: 12px; color: var(--dk-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; direction: rtl; text-align: left; }
    .roots { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 8px; }
    .list { list-style: none; margin: 0; padding: 0; }
    li { display: flex; align-items: center; gap: 8px; border-bottom: 1px solid rgba(255,255,255,.06); padding: 0 4px; &.selected { background: rgba(0,229,255,.08); } }
    .entry { flex: 1; display: flex; align-items: center; gap: 10px; background: none; border: none; color: var(--dk-text); font: 13px var(--dk-font); text-align: left; min-height: 40px; cursor: pointer; min-width: 0; }
    .name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .size { color: var(--dk-faint); font-size: 11px; }
    .empty { color: var(--dk-faint); padding: 16px; justify-content: center; }
  `,
})
export class FsBrowserDialog implements OnInit {
  readonly data = inject<FsBrowserOptions>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<FsBrowserDialog, string[] | undefined>);
  private readonly gql = inject(GqlService);

  readonly listing = signal<FsListing | null>(null);
  readonly error = signal<string | null>(null);
  readonly picked = signal(new Set<string>());
  readonly bytes = formatBytes;
  readonly canPick = computed(() => this.picked().size > 0);

  ngOnInit(): void {
    void this.go(this.data.start ?? null);
  }

  async go(path: string | null): Promise<void> {
    try {
      const d = await this.gql.query<{ browseFolder: FsListing }>(BROWSE, { path: path ?? '', audioOnly: !!this.data.audioOnly });
      this.listing.set(d.browseFolder);
      this.error.set(null);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : String(e));
    }
  }

  open(e: FsEntry): void {
    if (e.isDirectory && this.data.mode === 'folder') void this.go(e.localPath);
    else if (e.isDirectory) void this.go(e.localPath);
    else if (this.data.mode === 'files') this.toggle(e);
  }

  toggle(e: FsEntry): void {
    this.picked.update(s => {
      const n = new Set(s);
      if (!n.delete(e.localPath)) n.add(e.localPath);
      return n;
    });
  }
}

/** Ouvre le navigateur de dossiers du serveur ; rend les chemins choisis, ou undefined si annulé. */
export async function openFsBrowser(dialog: MatDialog, options: FsBrowserOptions): Promise<string[] | undefined> {
  const ref = dialog.open<FsBrowserDialog, FsBrowserOptions, string[] | undefined>(FsBrowserDialog, { data: options, width: 'min(620px, 96vw)' });
  return (await firstValueFrom(ref.afterClosed())) ?? undefined;
}
