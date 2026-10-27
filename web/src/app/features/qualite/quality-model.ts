import { gql } from '@apollo/client';

export type DefectId = 'hiss' | 'crackle' | 'clipping' | 'gaps' | 'duration' | 'tail' | 'head';

/** DEFECT_ORDER et DEFECT_LABELS de l'original. */
export const DEFECTS: { id: DefectId; label: string }[] = [
  { id: 'hiss', label: 'Souffle' },
  { id: 'crackle', label: 'Craquements' },
  { id: 'clipping', label: 'Saturation' },
  { id: 'gaps', label: 'Coupures' },
  { id: 'duration', label: 'Durée' },
  { id: 'tail', label: 'Blanc de fin' },
  { id: 'head', label: 'Blanc de début' },
];

/** Défauts que seule l'analyse approfondie sait voir. */
export const DEEP_DEFECTS: DefectId[] = ['crackle'];

export interface QualityThresholds {
  hissDb: number;
  cracklePct: number;
  clippingPct: number;
  gapDensityPct: number;
  durationExcess: number;
  durationGap: number;
  tailSeconds: number;
  headSeconds: number;
  deep: boolean;
}

export interface QualityRow {
  uid: number;
  path: string;
  title: string | null;
  artist: string | null;
  album: string | null;
  format: string | null;
  bitrate: number | null;
  duration: number | null;
  referenceDuration: number | null;
  referenceState: 'pending' | 'value' | 'none' | 'network';
  score: number;
  defects: { id: DefectId; label: string; severity: number; detail: string | null }[];
  cells: Record<string, string>;
  tooltips: Record<string, string>;
  error: string | null;
  onDisk: boolean;
  updatedAt: number;
  status: string | null;
  statusDetail: string | null;
  redownloadJobId: string | null;
}

export interface QualityPageData {
  items: QualityRow[];
  total: number;
  flagged: number;
  errors: number;
  referencesPending: number;
  folder: string | null;
  thresholds: QualityThresholds | null;
}

export interface QualityUpdate {
  reason: string;
  entries: QualityRow[];
  removed: string[] | null;
}

const THRESHOLD_FIELDS = 'hissDb cracklePct clippingPct gapDensityPct durationExcess durationGap tailSeconds headSeconds deep';
const ROW_FIELDS = `
  uid path title artist album format bitrate duration referenceDuration referenceState score
  defects { id label severity detail } cells tooltips error onDisk updatedAt status statusDetail redownloadJobId
`;

export const QUALITY_ENTRIES = gql`
  query QualityEntries {
    qualityEntries(includeClean: true, limit: 20000) {
      items { ${ROW_FIELDS} }
      total flagged errors referencesPending folder
      thresholds { ${THRESHOLD_FIELDS} }
    }
  }
`;

export const QUALITY_UPDATED = gql`subscription QualityUpdated { qualityUpdated { reason removed entries { ${ROW_FIELDS} } } }`;
export const UPDATE_THRESHOLDS = gql`mutation UpdateQualityThresholds($thresholds: QualityThresholdsInput!) { updateQualityThresholds(thresholds: $thresholds) { ${THRESHOLD_FIELDS} } }`;
export const START_SCAN = gql`mutation StartQualityScan($folder: String, $deep: Boolean) { startQualityScan(folder: $folder, deep: $deep) { id } }`;
export const REANALYZE = gql`mutation Reanalyze($paths: [String!]!) { reanalyze(paths: $paths) { id } }`;
export const REDOWNLOAD = gql`mutation RedownloadQuality($paths: [String!]!) { redownloadQuality(paths: $paths) { requested skipped skippedPaths jobs { id } } }`;
export const SET_FOLDER = gql`mutation SetQualityFolder($folder: String!) { setQualityFolder(folder: $folder) }`;
export const CLEAR = gql`mutation ClearQuality { clearQuality }`;

/** Champs du volet « Seuils » : libellé, bornes, pas, décimales, suffixe, infobulle (textes de l'original). */
export interface ThresholdField {
  key: keyof QualityThresholds;
  label: string;
  min: number;
  max: number;
  step: number;
  decimals: number;
  suffix: string;
  tip: string;
}

export const THRESHOLD_FIELDS_UI: ThresholdField[] = [
  {
    key: 'hissDb', label: 'Souffle au-dessus de', min: -90, max: -30, step: 1, decimals: 0, suffix: 'dB',
    tip: 'Plancher de bruit mesuré au-dessus de 8 kHz. Un titre sain\nreste sous −80 dB ; un souffle audible remonte ce plancher\nà son propre niveau.\nUn morceau que rien n\'interrompt n\'a pas de plancher à\nmesurer : il n\'est signalé que si le bruit relevé est bien\nlarge bande, ce que la colonne « Souffle » explique au survol.',
  },
  {
    key: 'cracklePct', label: 'Craquements au-delà de', min: 0.0005, max: 1, step: 0.0005, decimals: 4, suffix: '%',
    tip: 'Part d\'échantillons imprévisibles. Titres sains : moins de\n0,001 % ; deux craquements par seconde : 0,004 %.',
  },
  {
    key: 'clippingPct', label: 'Saturation au-delà de', min: 0.005, max: 5, step: 0.005, decimals: 3, suffix: '%',
    tip: 'Part d\'échantillons collés à la butée du format. Une variété\nrécente en montre couramment 0,03 à 0,08 % : c\'est le master\nqui est ainsi, retélécharger n\'y changerait rien. Au-delà de\n0,2 %, la distorsion s\'entend.',
  },
  {
    key: 'gapDensityPct', label: 'Coupures occupant plus de', min: 5, max: 90, step: 5, decimals: 0, suffix: '%',
    tip: 'Part de silence numérique — des échantillons nuls, non du signal\nfaible — du passage de dix secondes le plus troué.\nLes compter ne suffirait pas : « We Will Rock You » montre\nsoixante-neuf silences, ceux qui séparent ses frappes de pied,\net se porte très bien. Ce qui trahit une extraction qui hoquette,\nc\'est qu\'elle blanchit une large part d\'un passage entier :\n51 % là où un fichier sain ne dépasse pas 19 %, et reste le plus\nsouvent sous 5 %.\nLes blancs de début et de fin sont exclus : ils ont leur propre\nseuil.',
  },
  {
    key: 'tailSeconds', label: 'Blanc de fin au-delà de', min: 1, max: 60, step: 1, decimals: 0, suffix: 's',
    tip: 'Durée de la plage muette terminant le fichier. Une fin en\nfondu en occupe deux ou trois.\nUn blanc n\'est signalé que si le fichier dépasse en outre la\ndurée du morceau (voir le seuil ci-dessous).',
  },
  {
    key: 'headSeconds', label: 'Blanc de début au-delà de', min: 0.5, max: 30, step: 0.5, decimals: 1, suffix: 's',
    tip: 'Durée de la plage muette ouvrant le fichier.\nUn blanc n\'est signalé que si le fichier dépasse en outre la\ndurée du morceau (voir le seuil ci-dessous).',
  },
  {
    key: 'durationGap', label: 'Durée s\'écartant de la référence de plus de', min: 2, max: 120, step: 1, decimals: 0, suffix: 's',
    tip: 'Écart, dans un sens ou dans l\'autre, entre la durée du fichier\net celle du morceau chez Deezer ou MusicBrainz.\nUn fichier plus court ou plus long n\'est pas la version\nattendue : montage écourté, édition radio, extraction d\'une\nmauvaise source. C\'est souvent le seul indice exploitable — un\nrip radio propre se mesure comme un master propre.\nSur soixante-dix titres tirés au hasard, l\'écart médian est de\n1,6 s et les trois quarts tiennent sous 3,4 s ; au-delà de dix\nsecondes commence une autre famille.',
  },
  {
    key: 'durationExcess', label: 'Blancs signalés si la durée dépasse de', min: 1, max: 60, step: 1, decimals: 0, suffix: 's',
    tip: 'Un silence n\'est un défaut que s\'il a été ajouté : beaucoup de\nmorceaux se terminent sur un fondu ou des applaudissements que\nleur master contient bel et bien.\nLa durée du morceau est cherchée chez Deezer, puis chez\nMusicBrainz — et la plus proche de celle du fichier est retenue,\npour ne pas accuser un titre de ne pas durer autant qu\'un remix.\nSur des fichiers sains, l\'écart mesuré ne dépasse pas 3 s.\nSans durée de référence, aucun blanc n\'est signalé.',
  },
];

export const DEEP_TIP =
  'Les craquements se repèrent en prédisant chaque échantillon à\npartir des précédents : c\'est la mesure la plus sûre, et de loin\nla plus coûteuse. Décochez-la pour une analyse rapide limitée au\nsouffle, à la saturation et aux blancs.';

export type SortKey =
  | 'score' | 'title' | 'artist' | 'defects' | 'hiss' | 'crackle' | 'clipping' | 'gaps' | 'duration' | 'blanks' | 'format' | 'path' | 'status';

const text = (s: string | null | undefined) => (s ?? '').toLowerCase();
const finite = (n: number) => (Number.isFinite(n) ? n : -Infinity);
const num = (cell: string | undefined) => {
  const m = /-?\d+(\.\d+)?/.exec(cell ?? '');
  return m ? parseFloat(m[0]) : null;
};

/** Clés de tri de l'original (quality_page.py:836-869), sur les textes déjà calculés par le serveur. */
export function sortKey(row: QualityRow, key: SortKey): (number | string)[] {
  switch (key) {
    case 'score': return [row.error ? -1 : row.score, text(row.title)];
    case 'title': return [text(row.title)];
    case 'artist': return [text(row.artist), text(row.title)];
    case 'defects': return [row.defects.length, row.score];
    case 'hiss': return [finite(num(row.cells['hiss']) ?? -Infinity)];
    case 'crackle': return [row.cells['crackle'] === 'non mesuré' ? -1 : num(row.cells['crackle']) ?? -1];
    case 'clipping': return [num(row.cells['clipping']) ?? 0];
    case 'gaps': return [num((row.cells['gaps'] ?? '').split('·')[1]) ?? 0];
    case 'duration': return [row.referenceDuration != null && row.duration != null ? Math.abs(row.duration - row.referenceDuration) : -1];
    case 'blanks': return [Math.max(blank(row, 'tail'), blank(row, 'head'))];
    case 'format': return [row.bitrate ?? 0, text(row.format)];
    case 'path': return [text(row.path)];
    case 'status': return [text(row.cells['status'])];
  }
}

function blank(row: QualityRow, which: 'tail' | 'head'): number {
  // La cellule « blanks » ne porte que les défauts retenus ; le détail complet est dans l'infobulle « Début : x s — Fin : y s ».
  const m = /Début : ([\d.]+) s — Fin : ([\d.]+) s/.exec(row.tooltips['blanks'] ?? '');
  return m ? parseFloat(m[which === 'head' ? 1 : 2]) : 0;
}

export function compareKeys(a: (number | string)[], b: (number | string)[]): number {
  for (let i = 0; i < Math.min(a.length, b.length); i++) {
    if (a[i] < b[i]) return -1;
    if (a[i] > b[i]) return 1;
  }
  return 0;
}

/**
 * keep(e) : un défaut visible, ou illisible (toujours montré), ou « Titres sans défaut » cochée et aucun défaut.
 * Un défaut « profond » n'est visible que si la case « Détecter les craquements » est cochée.
 */
export function keepRow(row: QualityRow, visible: ReadonlySet<DefectId>, includeClean: boolean): boolean {
  if (row.error) return true;
  if (row.defects.length === 0) return includeClean;
  return row.defects.some(d => visible.has(d.id));
}

export function scoreClass(row: QualityRow): string {
  if (row.error) return '';
  return row.score >= 100 ? 'ok' : row.score < 75 ? 'bad' : 'warn';
}
