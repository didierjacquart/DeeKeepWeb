/** Colonnes du tableau : clé de tri (null = non triable), libellé, classe de cellule, masquage sur mobile. */
export const COLUMNS = [
  { key: null, label: '', cls: 'c-chk' },
  { key: 'score', label: 'Écouter', cls: 'c-play', tip: 'Écouter ce titre dans le lecteur (égaliseur, déplacement)' },
  { key: 'score', label: 'Note', cls: 'c-score' },
  { key: 'title', label: 'Titre', cls: 'c-title' },
  { key: 'artist', label: 'Artiste', cls: 'c-artist', hideMobile: true },
  { key: 'defects', label: 'Défauts', cls: 'c-defects' },
  { key: 'hiss', label: 'Souffle', cls: 'c-hiss', hideMobile: true },
  { key: 'crackle', label: 'Craquements', cls: 'c-crackle', hideMobile: true },
  { key: 'clipping', label: 'Saturation', cls: 'c-clip', hideMobile: true },
  { key: 'gaps', label: 'Coupures', cls: 'c-gaps', hideMobile: true },
  { key: 'duration', label: 'Durée', cls: 'c-dur', hideMobile: true },
  { key: 'blanks', label: 'Blancs', cls: 'c-blanks', hideMobile: true },
  { key: 'format', label: 'Format / débit', cls: 'c-fmt' },
  { key: 'path', label: 'Fichier', cls: 'c-path', hideMobile: true },
  { key: 'status', label: 'Statut', cls: 'c-status' },
];
