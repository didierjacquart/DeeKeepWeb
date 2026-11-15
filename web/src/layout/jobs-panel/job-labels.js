/** Nom lisible de chaque type de tâche de fond. */
const TYPE_LABELS = {
  download: 'Extraction',
  convert: 'Conversion',
  redownload: 'Retéléchargement',
  'quality.scan': 'Analyse qualité',
  'quality.reanalyze': 'Réanalyse',
  'quality.references': 'Durées de référence',
  'organize.analyze': 'Analyse de la bibliothèque',
  'organize.apply': 'Rangement',
  'tags.find': 'Recherche de tags',
  'tags.write': 'Écriture des tags',
  'library.scan': 'Lecture de la bibliothèque',
  'library.import': "Import de l'application desktop",
  'files.scan': 'Lecture de fichiers',
  'archive.upload': 'Archivage',
  'archive.restore': 'Restauration',
  'archive.check': "Vérification de l'archive",
  'archive.usage': "Mesure de l'archive",
  'tools.update': 'Mise à jour de yt-dlp',
};

const STATUS_TEXT = {
  QUEUED: 'en file',
  RUNNING: 'en cours',
  SUCCEEDED: 'terminée',
  FAILED: 'échec',
  CANCELLED: 'arrêtée',
};

const STATUS_CHIP = { QUEUED: '', RUNNING: 'info', SUCCEEDED: 'ok', FAILED: 'err', CANCELLED: 'warn' };

export const jobTypeLabel = type => TYPE_LABELS[type] ?? type;
export const jobStatusText = status => STATUS_TEXT[status];
export const jobStatusChip = status => STATUS_CHIP[status];
