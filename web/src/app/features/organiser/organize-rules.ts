/**
 * Règles d'affichage et de choix partagées par les écrans de doublons
 * (Organiser, « Dédoublonner… » de la recherche). Le serveur applique les mêmes
 * (OrganizeRules.BestCopy côté .NET) : garder les deux en phase.
 */

/** Exemplaire candidat à la conservation dans un groupe de doublons. */
export interface CopyCandidate {
  path: string;
  /** Débit en kb/s (0 ou absent si inconnu). */
  bitrate?: number | null;
  /** Taille en octets (absente si le fichier est illisible). */
  fileSize?: number | null;
  /** Extension ou format (« mp3 », « MP3 »). */
  ext?: string | null;
  /** Artiste du titre : un exemplaire rangé dans le dossier de l'artiste est préféré à qualité égale. */
  artist?: string | null;
}

/** Écart de taille en deçà duquel deux exemplaires sont de qualité équivalente (DUPLICATE_SIZE_RATIO). */
export const DUPLICATE_SIZE_RATIO = 0.97;

const norm = (text: string | null | undefined) => (text ?? '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').replace(/\s+/g, ' ').trim();

/** Vrai si un dossier parent du chemin porte exactement le nom de l'artiste (casse et ponctuation ignorées). */
export function inArtistFolder(path: string, artist: string | null | undefined): boolean {
  const target = norm(artist);
  if (!target) return false;
  const parts = path.replace(/\\/g, '/').split('/');
  parts.pop();
  return parts.some(p => norm(p) === target);
}

/**
 * Règle unique du « meilleur exemplaire » (voir docs : Organize/BestCopy) :
 * la qualité d'abord — une suppression ne se rattrape pas —, puis le rangement.
 * 1. meilleur débit ;
 * 2. taille à 3 % près de la plus grande (au-delà, l'exemplaire est incomplet) ;
 * 3. format cible des Paramètres ;
 * 4. rangé dans le dossier de son artiste ;
 * 5. hors « Divers » ;
 * 6. le moins enfoui ;
 * 7. le plus gros ; 8. chemin le plus court ; 9. ordre alphabétique.
 * Renvoie l'indice de l'exemplaire à garder (le premier en cas d'égalité totale).
 */
export function bestCopyIndex(items: CopyCandidate[], targetFormat?: string | null): number {
  if (items.length === 0) return -1;
  const largest = Math.max(...items.map(i => i.fileSize ?? 0));
  const target = (targetFormat ?? '').replace(/^\./, '').toUpperCase();
  const rank = (i: CopyCandidate): (number | string)[] => {
    const path = i.path ?? '';
    const unified = path.replace(/\\/g, '/');
    const size = i.fileSize ?? 0;
    const fmt = (i.ext ?? path.split('.').pop() ?? '').replace(/^\./, '').toUpperCase();
    const inDivers = unified.split('/').slice(0, -1).some(p => p.toLowerCase() === 'divers');
    return [
      -(i.bitrate ?? 0),
      size >= DUPLICATE_SIZE_RATIO * largest ? 0 : 1,
      target && fmt === target ? 0 : 1,
      inArtistFolder(path, i.artist) ? 0 : 1,
      inDivers ? 1 : 0,
      (unified.match(/\//g) ?? []).length,
      -size,
      path.length,
      path.toLowerCase(),
    ];
  };
  let best = 0;
  let bestRank = rank(items[0]);
  for (let k = 1; k < items.length; k++) {
    const r = rank(items[k]);
    if (compareRank(r, bestRank) < 0) {
      best = k;
      bestRank = r;
    }
  }
  return best;
}

function compareRank(a: (number | string)[], b: (number | string)[]): number {
  for (let i = 0; i < a.length; i++) {
    if (a[i] === b[i]) continue;
    if (typeof a[i] === 'number') return (a[i] as number) < (b[i] as number) ? -1 : 1;
    return (a[i] as string) < (b[i] as string) ? -1 : 1;
  }
  return 0;
}

/** human_size : « 8,4 Mo » (unités binaires, virgule décimale, octets entiers). */
export function humanSize(size: number | null | undefined): string {
  let value = Number(size ?? 0);
  const units = ['o', 'Ko', 'Mo', 'Go'];
  for (const unit of units) {
    if (value < 1024 || unit === 'Go') {
      const text = unit === 'o' ? value.toFixed(0) : value.toFixed(1);
      return `${text} ${unit}`.replace('.', ',');
    }
    value /= 1024;
  }
  return '';
}

/** file_quality : « MP3 · 320 kbps · 8,4 Mo », « fichier introuvable » si la taille est inconnue. */
export function fileQuality(path: string, ext?: string | null, bitrate?: number | null, fileSize?: number | null): string {
  if (fileSize == null) return 'fichier introuvable';
  const fmt = (ext ?? baseName(path).split('.').slice(1).pop() ?? '').replace(/^\./, '').toUpperCase() || '?';
  const parts = [fmt];
  if (bitrate) parts.push(`${bitrate} kbps`);
  parts.push(humanSize(fileSize));
  return parts.join(' · ');
}

/** Clé de rapprochement d'un chemin (casse et séparateurs ignorés), comme os.path.normcase. */
export function pathKey(path: string): string {
  return (path ?? '').replace(/\//g, '\\').toLowerCase();
}

export function baseName(path: string): string {
  const parts = (path ?? '').split(/[\\/]/);
  return parts[parts.length - 1] ?? '';
}

export function dirName(path: string): string {
  const p = path ?? '';
  const i = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
  return i >= 0 ? p.slice(0, i) : '';
}
