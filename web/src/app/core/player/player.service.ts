import { Injectable, computed, inject, signal } from '@angular/core';
import { OfflineAudioService } from './offline-audio.service';

/** Titre jouable par le lecteur. */
export interface PlayerTrack {
  /** Identifiant stable (chemin en base pour un fichier, « deezer:id » pour un extrait). */
  id: string;
  title: string;
  artist?: string | null;
  album?: string | null;
  /** URL du flux (ex. /media/track?path=…, /archive-media/track?path=…, /preview/123). */
  src: string;
  cover?: string | null;
  duration?: number | null;
  /** Contexte affiché sous le titre (écran Qualité : format, débit, note, défauts). */
  context?: string | null;
}

/** Fréquences centrales des 10 bandes d'octave de l'égaliseur d'analyse. */
export const EQ_BANDS = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

const VOLUME_KEY = 'dk-player-volume';

/**
 * Lecteur commun à toute l'application.
 *
 * - La liste affichée au moment du clic devient la file d'attente.
 * - Un seul son à la fois : lancer un extrait Deezer met le lecteur en pause
 *   (il reprend au même endroit quand l'extrait s'arrête) ; relancer le
 *   lecteur coupe l'extrait.
 * - L'égaliseur mesure le signal réellement joué (Web Audio), sans décodage
 *   parallèle sur le serveur.
 * - Commandes de l'écran verrouillé et du casque via Media Session.
 */
@Injectable({ providedIn: 'root' })
export class PlayerService {
  private readonly offline = inject(OfflineAudioService);

  readonly queue = signal<PlayerTrack[]>([]);
  readonly index = signal(-1);
  readonly current = computed(() => this.queue()[this.index()] ?? null);
  readonly playing = signal(false);
  readonly position = signal(0);
  readonly duration = signal(0);
  readonly buffering = signal(false);
  readonly error = signal<string | null>(null);
  /** Volume « perçu » 0..1 ; le gain appliqué est son carré (courbe quadratique de l'original). */
  readonly volume = signal(this.loadVolume());
  readonly expanded = signal(false);
  /** Mis en pause par un extrait : reprend quand l'extrait s'arrête. */
  readonly pausedByPreview = signal(false);

  readonly preview = signal<PlayerTrack | null>(null);
  readonly previewPlaying = signal(false);

  readonly hasNext = computed(() => this.index() < this.queue().length - 1);
  readonly hasPrevious = computed(() => this.index() > 0);

  private audio: HTMLAudioElement | null = null;
  private previewAudio: HTMLAudioElement | null = null;
  private context: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private objectUrl: string | null = null;

  constructor() {
    if (typeof window === 'undefined') return;
    document.addEventListener('keydown', e => this.onKey(e));
  }

  // ------------------------------------------------------------ Lecture

  /** Joue <paramref name="track"/> en prenant <paramref name="queue"/> (la liste affichée) comme file. */
  play(track: PlayerTrack, queue?: PlayerTrack[]): void {
    const list = queue && queue.length ? queue : [track];
    const at = Math.max(0, list.findIndex(t => t.id === track.id));
    this.queue.set(list);
    void this.load(at, true);
  }

  playAt(index: number): void {
    if (index >= 0 && index < this.queue().length) void this.load(index, true);
  }

  toggle(): void {
    const audio = this.audio;
    if (!audio || !this.current()) return;
    if (audio.paused) {
      this.stopPreview(false);
      void this.resumeContext();
      void audio.play().catch(e => this.error.set(String(e)));
    } else {
      audio.pause();
    }
  }

  pause(): void {
    this.audio?.pause();
  }

  stop(): void {
    if (!this.audio) return;
    this.audio.pause();
    this.audio.currentTime = 0;
  }

  next(): void {
    if (this.hasNext()) this.playAt(this.index() + 1);
  }

  previous(): void {
    // Comme les lecteurs usuels : au-delà de 3 s, « précédent » revient au début du titre.
    if (this.audio && this.audio.currentTime > 3) this.seek(0);
    else if (this.hasPrevious()) this.playAt(this.index() - 1);
  }

  seek(seconds: number): void {
    if (!this.audio || !Number.isFinite(seconds)) return;
    this.audio.currentTime = Math.max(0, Math.min(seconds, this.duration() || seconds));
  }

  seekBy(delta: number): void {
    if (this.audio) this.seek(this.audio.currentTime + delta);
  }

  setVolume(value: number): void {
    const v = Math.max(0, Math.min(1, value));
    this.volume.set(v);
    if (this.audio) this.audio.volume = v * v;
    if (this.previewAudio) this.previewAudio.volume = v * v;
    try { localStorage.setItem(VOLUME_KEY, String(v)); } catch { /* stockage indisponible */ }
  }

  close(): void {
    this.stop();
    this.queue.set([]);
    this.index.set(-1);
    this.expanded.set(false);
  }

  // ------------------------------------------------------------ Extraits

  /** Écoute immédiate d'un extrait de 30 s ; un second clic sur le même l'arrête. */
  togglePreview(track: PlayerTrack): void {
    if (this.preview()?.id === track.id && this.previewPlaying()) {
      this.stopPreview(true);
      return;
    }
    const audio = this.ensurePreviewAudio();
    if (this.audio && !this.audio.paused) {
      this.audio.pause();
      this.pausedByPreview.set(true);
    }
    this.preview.set(track);
    audio.src = track.src;
    audio.volume = this.volume() ** 2;
    void audio.play().catch(() => this.stopPreview(true));
  }

  stopPreview(resume: boolean): void {
    if (this.previewAudio) {
      this.previewAudio.pause();
      this.previewAudio.removeAttribute('src');
      this.previewAudio.load();
    }
    this.previewPlaying.set(false);
    this.preview.set(null);
    if (resume && this.pausedByPreview() && this.audio) void this.audio.play().catch(() => undefined);
    this.pausedByPreview.set(false);
  }

  // ------------------------------------------------------------ Égaliseur

  /** Niveaux (dB, ~ -90..0) des 10 bandes d'octave, ou null si l'analyse est indisponible. */
  readBands(target: Float32Array): boolean {
    const analyser = this.analyser;
    if (!analyser || !this.context) return false;
    const bins = new Float32Array(analyser.frequencyBinCount);
    analyser.getFloatFrequencyData(bins);
    const hzPerBin = this.context.sampleRate / analyser.fftSize;
    EQ_BANDS.forEach((center, i) => {
      const lo = Math.max(1, Math.floor(center / Math.SQRT2 / hzPerBin));
      const hi = Math.min(bins.length - 1, Math.ceil((center * Math.SQRT2) / hzPerBin));
      // Énergie de la bande = somme des puissances de ses raies.
      let power = 0;
      for (let b = lo; b <= hi; b++) power += 10 ** (bins[b] / 10);
      target[i] = power > 0 ? 10 * Math.log10(power) : -120;
    });
    return true;
  }

  // ------------------------------------------------------------ Interne

  private async load(index: number, autoplay: boolean): Promise<void> {
    const track = this.queue()[index];
    if (!track) return;
    this.stopPreview(false);
    const audio = this.ensureAudio();
    this.index.set(index);
    this.error.set(null);
    this.position.set(0);
    this.duration.set(track.duration ?? 0);
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
    // Un titre épinglé pour l'écoute hors ligne est lu depuis l'appareil.
    const local = await this.offline.objectUrl(track.src);
    if (local) this.objectUrl = local;
    audio.src = local ?? track.src;
    this.updateMediaSession(track);
    if (autoplay) {
      await this.resumeContext();
      await audio.play().catch(e => this.error.set(e instanceof Error ? e.message : String(e)));
    }
  }

  private ensureAudio(): HTMLAudioElement {
    if (this.audio) return this.audio;
    const audio = new Audio();
    audio.preload = 'auto';
    audio.volume = this.volume() ** 2;
    audio.addEventListener('play', () => this.playing.set(true));
    audio.addEventListener('pause', () => this.playing.set(false));
    audio.addEventListener('timeupdate', () => this.position.set(audio.currentTime));
    audio.addEventListener('durationchange', () => Number.isFinite(audio.duration) && this.duration.set(audio.duration));
    audio.addEventListener('waiting', () => this.buffering.set(true));
    audio.addEventListener('playing', () => this.buffering.set(false));
    audio.addEventListener('ended', () => (this.hasNext() ? this.next() : this.playing.set(false)));
    audio.addEventListener('error', () => {
      this.buffering.set(false);
      this.error.set(navigator.onLine ? 'Lecture impossible : fichier introuvable ou illisible.' : 'Hors ligne : ce titre n\'est pas disponible sur l\'appareil.');
    });
    this.audio = audio;
    this.setupMediaSessionHandlers();
    return audio;
  }

  private ensurePreviewAudio(): HTMLAudioElement {
    if (this.previewAudio) return this.previewAudio;
    const audio = new Audio();
    audio.addEventListener('play', () => this.previewPlaying.set(true));
    audio.addEventListener('ended', () => this.stopPreview(true));
    audio.addEventListener('error', () => this.stopPreview(true));
    this.previewAudio = audio;
    return audio;
  }

  /** Le graphe Web Audio ne peut naître qu'après un geste de l'utilisateur (règle des navigateurs). */
  private async resumeContext(): Promise<void> {
    try {
      if (!this.context && this.audio) {
        this.context = new AudioContext();
        const source = this.context.createMediaElementSource(this.audio);
        this.analyser = this.context.createAnalyser();
        this.analyser.fftSize = 8192;
        this.analyser.smoothingTimeConstant = 0.6;
        source.connect(this.analyser);
        this.analyser.connect(this.context.destination);
      }
      if (this.context?.state === 'suspended') await this.context.resume();
    } catch {
      // Sans Web Audio, la lecture fonctionne ; seul l'égaliseur reste éteint.
    }
  }

  private updateMediaSession(track: PlayerTrack): void {
    if (!('mediaSession' in navigator)) return;
    navigator.mediaSession.metadata = new MediaMetadata({
      title: track.title,
      artist: track.artist ?? '',
      album: track.album ?? '',
      artwork: track.cover ? [{ src: track.cover, sizes: '500x500' }] : [],
    });
  }

  private setupMediaSessionHandlers(): void {
    if (!('mediaSession' in navigator)) return;
    const ms = navigator.mediaSession;
    ms.setActionHandler('play', () => this.toggle());
    ms.setActionHandler('pause', () => this.pause());
    ms.setActionHandler('previoustrack', () => this.previous());
    ms.setActionHandler('nexttrack', () => this.next());
    ms.setActionHandler('seekbackward', () => this.seekBy(-10));
    ms.setActionHandler('seekforward', () => this.seekBy(10));
    ms.setActionHandler('seekto', d => d.seekTime != null && this.seek(d.seekTime));
  }

  /** Clavier : Espace, ← →, ↑ ↓, P / N — hors des champs de saisie. */
  private onKey(e: KeyboardEvent): void {
    if (!this.current()) return;
    const target = e.target as HTMLElement | null;
    if (target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(target.tagName))) return;
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    switch (e.key) {
      case ' ': this.toggle(); break;
      case 'ArrowLeft': this.seekBy(-5); break;
      case 'ArrowRight': this.seekBy(5); break;
      case 'ArrowUp': this.setVolume(this.volume() + 0.05); break;
      case 'ArrowDown': this.setVolume(this.volume() - 0.05); break;
      case 'p': case 'P': this.previous(); break;
      case 'n': case 'N': this.next(); break;
      default: return;
    }
    e.preventDefault();
  }

  private loadVolume(): number {
    try {
      const v = Number(localStorage.getItem(VOLUME_KEY));
      return Number.isFinite(v) && v > 0 ? Math.min(1, v) : 0.8;
    } catch {
      return 0.8;
    }
  }
}
