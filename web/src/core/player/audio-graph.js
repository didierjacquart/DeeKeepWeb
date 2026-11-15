/**
 * Graphe Web Audio branché sur l'élément de lecture : la source passe par un
 * analyseur (égaliseur) avant la sortie. Il ne peut naître qu'après un geste de
 * l'utilisateur (règle des navigateurs) ; sans Web Audio, la lecture fonctionne
 * et seul l'égaliseur reste éteint.
 */
export class AudioGraph {
  #context = null;
  #analyser = null;

  /** Crée le graphe au premier appel, puis réveille le contexte s'il est suspendu. */
  async resume(audio) {
    try {
      if (!this.#context && audio && typeof AudioContext !== 'undefined') {
        this.#context = new AudioContext();
        const source = this.#context.createMediaElementSource(audio);
        this.#analyser = this.#context.createAnalyser();
        this.#analyser.fftSize = 8192;
        this.#analyser.smoothingTimeConstant = 0.6;
        source.connect(this.#analyser);
        this.#analyser.connect(this.#context.destination);
      }
      if (this.#context?.state === 'suspended') await this.#context.resume();
    } catch {
      // Sans Web Audio, la lecture fonctionne ; seul l'égaliseur reste éteint.
    }
  }

  /**
   * Spectre courant en dB et largeur d'une raie, ou null si l'analyse est indisponible.
   * @returns {{ bins: Float32Array, hzPerBin: number } | null}
   */
  spectrum() {
    if (!this.#analyser || !this.#context) return null;
    const bins = new Float32Array(this.#analyser.frequencyBinCount);
    this.#analyser.getFloatFrequencyData(bins);
    return { bins, hzPerBin: this.#context.sampleRate / this.#analyser.fftSize };
  }
}
