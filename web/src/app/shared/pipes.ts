import { Pipe, PipeTransform } from '@angular/core';

/** 241.5 → « 4:01 » ; au-delà d'une heure « 1:02:03 » (format_duration de l'original). */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return '—';
  const s = Math.floor(seconds);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const r = s % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}` : `${m}:${String(r).padStart(2, '0')}`;
}

/** Taille lisible en unités décimales, comme les jauges de l'original (Mo, Go). */
export function formatBytes(bytes: number | null | undefined, digits = 1): string {
  if (bytes == null || !Number.isFinite(bytes)) return '—';
  const units = ['o', 'Ko', 'Mo', 'Go', 'To'];
  let v = bytes;
  let i = 0;
  while (Math.abs(v) >= 1000 && i < units.length - 1) {
    v /= 1000;
    i++;
  }
  return `${v.toLocaleString('fr-FR', { maximumFractionDigits: i === 0 ? 0 : digits })} ${units[i]}`;
}

/** Date relative courte (« il y a 5 min », « hier 14:02 »). */
export function formatWhen(unixSeconds: number | null | undefined): string {
  if (!unixSeconds) return '—';
  const date = new Date(unixSeconds * 1000);
  const diff = (Date.now() - date.getTime()) / 1000;
  if (diff < 60) return 'à l\'instant';
  if (diff < 3600) return `il y a ${Math.floor(diff / 60)} min`;
  if (diff < 86400 && new Date().getDate() === date.getDate()) return date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  return date.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' });
}

@Pipe({ name: 'duration' })
export class DurationPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatDuration(value);
  }
}

@Pipe({ name: 'bytes' })
export class BytesPipe implements PipeTransform {
  transform(value: number | null | undefined, digits = 1): string {
    return formatBytes(value, digits);
  }
}

@Pipe({ name: 'when' })
export class WhenPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return formatWhen(value);
  }
}
