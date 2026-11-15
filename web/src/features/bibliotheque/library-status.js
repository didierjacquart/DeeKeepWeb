import { formatBytes } from '../../shared/format';

const sum = (rows, field) => rows.reduce((s, r) => s + (r.track[field] ?? 0), 0);

/**
 * Ligne d'état sous la liste : titres affichés, présents sur le disque, en miroir
 * (avec le gain de place par rapport aux originaux) et à renvoyer.
 *
 * @param {{ track: object }[]} rows toute la bibliothèque
 * @param {number} displayedCount titres retenus par la recherche
 */
export function statusLine(rows, displayedCount) {
  const onDisk = rows.filter(r => r.track.onDisk);
  const parts = [
    `${displayedCount} titres affichés sur ${rows.length}`,
    `${onDisk.length} sur le disque (${formatBytes(sum(onDisk, 'fileSize'))})`,
  ];
  const fresh = rows.filter(r => r.track.archiveState === 'ok');
  if (fresh.length) {
    const stored = sum(fresh, 'archiveBytes');
    const source = sum(fresh, 'archiveSourceBytes');
    const pct = source > 0 ? Math.round((100 * stored) / source) : 0;
    parts.push(`${fresh.length} en miroir (${formatBytes(stored)}, soit ${pct} % du poids d'origine)`);
  } else parts.push('aucun titre en miroir');
  const staleCount = rows.filter(r => r.track.archiveState === 'stale').length;
  if (staleCount) parts.push(`${staleCount} à renvoyer`);
  return parts.join(' · ');
}
