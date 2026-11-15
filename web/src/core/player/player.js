import { Store } from '../../lib/store';
import { offlineAudio } from './offline-audio';
import { PlayerClock } from './player-clock';
import { bandLevels } from './eq-bands';
import { bindMediaKeys, showOnLockScreen } from './media-session';
import { AudioGraph } from './audio-graph';
import { createPlaybackAudio, createPreviewAudio, playbackErrorMessage } from './audio-elements';
import { handlePlayerKey } from './keyboard-shortcuts';
import { loadVolume, saveVolume } from './volume-storage';

/**
 * Titre jouable par le lecteur.
 * @typedef {object} PlayerTrack
 * @property {string} id Identifiant stable (chemin en base pour un fichier, « deezer:id » pour un extrait).
 * @property {string} title
 * @property {string | null} [artist]
 * @property {string | null} [album]
 * @property {string} src URL du flux (ex. /media/track?path=…, /archive-media/track?path=…, /preview/123).
 * @property {string | null} [cover]
 * @property {number | null} [duration]
 * @property {string | null} [context] Contexte affiché sous le titre (écran Qualité : format, débit, note, défauts).
 */

/**
 * @typedef {object} PlayerState
 * @property {PlayerTrack[]} queue
 * @property {number} index
 * @property {boolean} playing
 * @property {string | null} error
 * @property {number} volume Volume « perçu » 0..1 ; le gain appliqué est son carré (courbe quadratique de l'original).
 * @property {boolean} expanded
 * @property {boolean} pausedByPreview Mis en pause par un extrait : reprend quand l'extrait s'arrête.
 * @property {PlayerTrack | null} preview
 * @property {boolean} previewPlaying
 */

/** @param {PlayerState} s */
export function currentTrack(s) {
  return s.queue[s.index] ?? null;
}

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
 *
 * @extends {Store<PlayerState>}
 */
export class PlayerService extends Store {
  clock = new PlayerClock();

  #audio = null;
  #previewAudio = null;
  #graph = new AudioGraph();
  #objectUrl = null;

  constructor() {
    super({
      queue: [],
      index: -1,
      playing: false,
      error: null,
      volume: loadVolume(),
      expanded: false,
      pausedByPreview: false,
      preview: null,
      previewPlaying: false,
    });
    if (typeof document !== 'undefined') document.addEventListener('keydown', e => handlePlayerKey(this, e));
  }

  get current() {
    return currentTrack(this.state);
  }

  get hasNext() {
    return this.state.index < this.state.queue.length - 1;
  }

  get hasPrevious() {
    return this.state.index > 0;
  }

  // ------------------------------------------------------------ Lecture

  /**
   * Joue `track` en prenant `queue` (la liste affichée) comme file.
   * @param {PlayerTrack} track
   * @param {PlayerTrack[]} [queue]
   * @param {{ open?: boolean }} [options] `open` : ouvre la fenêtre « Lecteur DeeKeep ».
   */
  play(track, queue, options = {}) {
    const list = queue && queue.length ? queue : [track];
    const at = Math.max(
      0,
      list.findIndex(t => t.id === track.id),
    );
    this.setState(options.open ? { queue: list, expanded: true } : { queue: list });
    void this.#load(at, true);
  }

  playAt(index) {
    if (index >= 0 && index < this.state.queue.length) void this.#load(index, true);
  }

  toggle() {
    const audio = this.#audio;
    if (!audio || !this.current) return;
    if (audio.paused) this.resume();
    else audio.pause();
  }

  resume() {
    const audio = this.#audio;
    if (!audio || !this.current) return;
    this.stopPreview(false);
    void this.#graph.resume(audio);
    void audio.play().catch(e => this.#playRejected(e));
  }

  pause() {
    this.#audio?.pause();
  }

  stop() {
    if (!this.#audio) return;
    this.#audio.pause();
    this.#audio.currentTime = 0;
  }

  next() {
    if (this.hasNext) this.playAt(this.state.index + 1);
  }

  previous() {
    // Comme les lecteurs usuels : au-delà de 3 s, « précédent » revient au début du titre.
    if (this.#audio && this.#audio.currentTime > 3) this.seek(0);
    else if (this.hasPrevious) this.playAt(this.state.index - 1);
  }

  seek(seconds) {
    if (!this.#audio || !Number.isFinite(seconds)) return;
    this.#audio.currentTime = Math.max(0, Math.min(seconds, this.clock.state.duration || seconds));
  }

  seekBy(delta) {
    if (this.#audio) this.seek(this.#audio.currentTime + delta);
  }

  setVolume(value) {
    const v = Math.max(0, Math.min(1, value));
    this.setState({ volume: v });
    if (this.#audio) this.#audio.volume = v * v;
    if (this.#previewAudio) this.#previewAudio.volume = v * v;
    saveVolume(v);
  }

  setExpanded(expanded) {
    this.setState({ expanded });
  }

  close() {
    this.stop();
    this.setState({ queue: [], index: -1, expanded: false });
  }

  // ------------------------------------------------------------ Extraits

  /**
   * Écoute immédiate d'un extrait de 30 s ; un second clic sur le même l'arrête.
   * @param {PlayerTrack} track
   */
  togglePreview(track) {
    if (this.state.preview?.id === track.id && this.state.previewPlaying) {
      this.stopPreview(true);
      return;
    }
    const audio = this.#ensurePreviewAudio();
    if (this.#audio && !this.#audio.paused) {
      this.#audio.pause();
      this.setState({ pausedByPreview: true });
    }
    this.setState({ preview: track });
    audio.src = track.src;
    audio.volume = this.state.volume ** 2;
    void audio.play().catch(() => this.stopPreview(true));
  }

  stopPreview(resume) {
    if (this.#previewAudio) {
      this.#previewAudio.pause();
      this.#previewAudio.removeAttribute('src');
      this.#previewAudio.load();
    }
    const { pausedByPreview } = this.state;
    this.setState({ previewPlaying: false, preview: null, pausedByPreview: false });
    if (resume && pausedByPreview && this.#audio) void this.#audio.play().catch(() => undefined);
  }

  // ------------------------------------------------------------ Égaliseur

  /**
   * Niveaux (dB, ~ -90..0) des 10 bandes d'octave ; faux si l'analyse est indisponible.
   * @param {Float32Array} target
   */
  readBands(target) {
    const spectrum = this.#graph.spectrum();
    if (!spectrum) return false;
    bandLevels(spectrum.bins, spectrum.hzPerBin, target);
    return true;
  }

  // ------------------------------------------------------------ Interne

  async #load(index, autoplay) {
    const track = this.state.queue[index];
    if (!track) return;
    this.stopPreview(false);
    const audio = this.#ensureAudio();
    this.setState({ index, error: null });
    this.clock.update({ position: 0, duration: track.duration ?? 0 });
    if (this.#objectUrl) {
      URL.revokeObjectURL(this.#objectUrl);
      this.#objectUrl = null;
    }
    // Un titre épinglé pour l'écoute hors ligne est lu depuis l'appareil.
    const local = await offlineAudio.objectUrl(track.src);
    if (local) this.#objectUrl = local;
    audio.src = local ?? track.src;
    showOnLockScreen(track);
    if (autoplay) {
      await this.#graph.resume(audio);
      await audio.play().catch(e => this.#playRejected(e));
    }
  }

  /**
   * `play()` refusé : rien ne joue. Un abandon (titre changé pendant le chargement) n'est
   * pas une erreur ; un flux illisible est déjà signalé par l'événement « error ».
   */
  #playRejected(e) {
    if (e?.name === 'AbortError') return;
    this.setState(s => ({ playing: false, error: s.error ?? (e instanceof Error ? e.message : String(e)) }));
  }

  #ensureAudio() {
    if (this.#audio) return this.#audio;
    this.#audio = createPlaybackAudio(this.state.volume ** 2, {
      onPlay: () => this.setState({ playing: true }),
      onPause: () => this.setState({ playing: false }),
      onTime: position => this.clock.update({ position }),
      onDuration: duration => this.clock.update({ duration }),
      onBuffering: buffering => this.clock.update({ buffering }),
      onEnded: () => (this.hasNext ? this.next() : this.setState({ playing: false })),
      onError: () => {
        // Flux refusé (404, archive injoignable…) : le lecteur n'est plus « en lecture ».
        this.clock.update({ buffering: false });
        this.setState({ playing: false, error: playbackErrorMessage(navigator.onLine, this.current?.src) });
      },
    });
    bindMediaKeys({
      play: () => this.toggle(),
      pause: () => this.pause(),
      previous: () => this.previous(),
      next: () => this.next(),
      seekBy: s => this.seekBy(s),
      seek: s => this.seek(s),
    });
    return this.#audio;
  }

  #ensurePreviewAudio() {
    this.#previewAudio ??= createPreviewAudio({
      onPlay: () => this.setState({ previewPlaying: true }),
      onStop: () => this.stopPreview(true),
    });
    return this.#previewAudio;
  }
}

export const player = new PlayerService();
