import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, signal, viewChild } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PlayerService, EQ_BANDS } from './player.service';
import { OfflineAudioService } from './offline-audio.service';
import { DurationPipe } from '../../shared/pipes';

/**
 * Lecteur en bas d'écran : barre compacte toujours visible, panneau déplié
 * avec égaliseur d'analyse 10 bandes (maintien de crête), position cliquable,
 * volume et file d'attente.
 */
@Component({
  selector: 'dk-player-bar',
  imports: [MatTooltipModule, DurationPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './player-bar.html',
  styleUrl: './player-bar.scss',
  host: { '[class.expanded]': 'player.expanded()', '[class.visible]': '!!player.current()' },
})
export class PlayerBar implements OnDestroy {
  readonly player = inject(PlayerService);
  readonly offline = inject(OfflineAudioService);
  readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('eq');
  readonly bands = EQ_BANDS.map(f => (f >= 1000 ? `${f / 1000}k` : `${f}`));
  readonly dragging = signal<number | null>(null);

  private frame = 0;
  private readonly levels = new Float32Array(EQ_BANDS.length);
  private readonly peaks = new Float32Array(EQ_BANDS.length).fill(-90);
  private readonly peakAge = new Float32Array(EQ_BANDS.length);

  constructor() {
    effect(() => {
      const visible = this.player.expanded() && this.player.playing();
      cancelAnimationFrame(this.frame);
      if (visible) this.frame = requestAnimationFrame(() => this.draw());
    });
    effect(() => {
      document.documentElement.style.setProperty('--dk-player-height', this.player.current() ? '72px' : '0px');
    });
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frame);
  }

  progressPercent(): number {
    const d = this.player.duration();
    const p = this.dragging() ?? this.player.position();
    return d > 0 ? Math.min(100, (p / d) * 100) : 0;
  }

  /** Un clic dans la rainure amène la lecture au point visé, sans glisser. */
  seekFromPointer(event: PointerEvent, bar: HTMLElement): void {
    const rect = bar.getBoundingClientRect();
    const ratio = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
    this.player.seek(ratio * this.player.duration());
  }

  onVolume(event: Event): void {
    this.player.setVolume(Number((event.target as HTMLInputElement).value) / 100);
  }

  async togglePin(): Promise<void> {
    const track = this.player.current();
    if (!track) return;
    if (this.offline.isPinned(track.src)) await this.offline.unpin(track.src);
    else await this.offline.pin(track.src);
  }

  private draw(): void {
    const canvas = this.canvas()?.nativeElement;
    if (!canvas || !this.player.expanded()) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (canvas.width !== w * dpr) {
      canvas.width = w * dpr;
      canvas.height = h * dpr;
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const ok = this.player.readBands(this.levels);
    const n = EQ_BANDS.length;
    const gap = 6;
    const bw = (w - gap * (n - 1)) / n;
    const gradient = ctx.createLinearGradient(0, h, 0, 0);
    gradient.addColorStop(0, '#343d96');
    gradient.addColorStop(1, '#00e5ff');
    for (let i = 0; i < n; i++) {
      // Échelle de -80 dB (vide) à -10 dB (plein).
      const db = ok ? this.levels[i] : -120;
      const level = Math.max(0, Math.min(1, (db + 80) / 70));
      const x = i * (bw + gap);
      ctx.fillStyle = '#1c1e26';
      ctx.fillRect(x, 0, bw, h);
      ctx.fillStyle = gradient;
      ctx.fillRect(x, h - level * h, bw, level * h);
      // Maintien de crête : la crête tient ~1 s puis redescend doucement.
      if (db >= this.peaks[i]) {
        this.peaks[i] = db;
        this.peakAge[i] = 0;
      } else if (++this.peakAge[i] > 60) {
        this.peaks[i] -= 0.6;
      }
      const peak = Math.max(0, Math.min(1, (this.peaks[i] + 80) / 70));
      ctx.fillStyle = '#5cefff';
      ctx.fillRect(x, h - peak * h - 2, bw, 2);
    }
    if (this.player.playing()) this.frame = requestAnimationFrame(() => this.draw());
  }
}
