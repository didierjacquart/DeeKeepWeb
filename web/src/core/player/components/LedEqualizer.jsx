import { useEffect, useRef } from 'react';
import { EQ_BANDS, EQ_BAND_LABELS, levelRatio } from '../eq-bands';
import { createMeter, LED_SEGMENTS, segmentColor, stepMeter } from '../led-meter';
import { player } from '../player';

const PAD_SIDE = 30;
const PAD_TOP = 10;
const PAD_BOTTOM = 22;
const SCALE = 'rgba(132, 147, 150, 0.6)';
const COLORS = Array.from({ length: LED_SEGMENTS }, (_, i) => segmentColor(i));

/** Une image : fond, diodes allumées / éteintes, crêtes, échelles latérales et fréquences. */
function drawFrame(canvas, meter) {
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  const dpr = window.devicePixelRatio || 1;
  const w = canvas.clientWidth;
  const h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);

  const areaW = w - 2 * PAD_SIDE;
  const areaH = h - PAD_TOP - PAD_BOTTOM;
  if (areaW <= 0 || areaH <= 0) return;
  const n = EQ_BANDS.length;
  const segH = areaH / LED_SEGMENTS;
  const gap = Math.max(1, segH * 0.3);
  const colW = areaW / n;
  const barW = colW * 0.66;

  for (let c = 0; c < n; c++) {
    const lit = Math.round(meter.levels[c] * LED_SEGMENTS);
    const peak = Math.round(meter.peaks[c] * LED_SEGMENTS);
    const x = PAD_SIDE + c * colW + (colW - barW) / 2;
    for (let s = 0; s < LED_SEGMENTS; s++) {
      const [r, g, b] = COLORS[s];
      if (s < lit) ctx.fillStyle = `rgb(${r},${g},${b})`;
      else if (s + 1 === peak) ctx.fillStyle = 'rgba(0, 229, 255, 0.8)';
      else ctx.fillStyle = `rgba(${r},${g},${b},0.1)`;
      const y = PAD_TOP + areaH - (s + 1) * segH + gap / 2;
      ctx.fillRect(x, y, barW, Math.max(1, segH - gap));
    }
  }

  // Échelle relative de part et d'autre (un point toutes les deux diodes, 10 / 5 / 0) et fréquences.
  ctx.fillStyle = SCALE;
  ctx.font = '9px Inter, "Segoe UI", sans-serif';
  ctx.textBaseline = 'middle';
  for (let s = 0; s <= LED_SEGMENTS; s += 2) {
    const y = PAD_TOP + areaH - s * segH;
    ctx.fillRect(PAD_SIDE - 9, y - 0.5, 2, 2);
    ctx.fillRect(w - PAD_SIDE + 7, y - 0.5, 2, 2);
  }
  for (const [value, s] of [
    [10, LED_SEGMENTS],
    [5, LED_SEGMENTS / 2],
    [0, 0],
  ]) {
    const y = PAD_TOP + areaH - s * segH;
    ctx.textAlign = 'right';
    ctx.fillText(String(value), PAD_SIDE - 13, y);
    ctx.textAlign = 'left';
    ctx.fillText(String(value), w - PAD_SIDE + 13, y);
  }
  ctx.textAlign = 'center';
  EQ_BAND_LABELS.forEach((label, c) => ctx.fillText(label, PAD_SIDE + c * colW + colW / 2, h - PAD_BOTTOM / 2));
  ctx.textAlign = 'left';
  ctx.fillText('Hz', PAD_SIDE + n * colW + 4, h - PAD_BOTTOM / 2);
}

/**
 * Égaliseur d'analyse à diodes, 10 bandes d'octave (31 Hz → 16 kHz). Animé tant que la
 * lecture est en cours ; à l'arrêt, les barres retombent puis l'animation s'endort.
 */
export function LedEqualizer({ active }) {
  const canvas = useRef(null);
  const meter = useRef(null);

  useEffect(() => {
    meter.current ??= createMeter(EQ_BANDS.length);
    const m = meter.current;
    const db = new Float32Array(EQ_BANDS.length);
    const targets = new Float32Array(EQ_BANDS.length);
    let frame = 0;
    let last = performance.now();
    const loop = now => {
      const dt = Math.min(0.1, (now - last) / 1000);
      last = now;
      const measured = active && player.readBands(db);
      if (measured) for (let i = 0; i < db.length; i++) targets[i] = levelRatio(db[i]);
      const lit = stepMeter(m, measured ? targets : null, dt);
      if (canvas.current) drawFrame(canvas.current, m);
      if (active || lit) frame = requestAnimationFrame(loop);
    };
    frame = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(frame);
  }, [active]);

  return <canvas ref={canvas} className="led-eq" role="img" aria-label="Égaliseur d'analyse" />;
}
