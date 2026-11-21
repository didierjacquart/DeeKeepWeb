import { api } from '../../core/api/api';
import { endpoint, serverEvent } from '../../core/api/endpoints';

/** Toutes les lignes (titres sans défaut compris), totaux et seuils courants. */
export const QUALITY_ENTRIES = endpoint(
  'QualityEntries',
  'qualityEntries',
  'GET /api/quality/entries?includeClean=true&limit=20000',
);

export const QUALITY_UPDATED = serverEvent('QualityUpdated', 'qualityUpdated', 'quality.updated');
export const UPDATE_THRESHOLDS = endpoint(
  'UpdateQualityThresholds',
  'updateQualityThresholds',
  'PUT /api/quality/thresholds',
  {
    body: v => v.thresholds,
  },
);
export const START_SCAN = endpoint('StartQualityScan', 'startQualityScan', 'POST /api/quality/scan');
export const REANALYZE = endpoint('Reanalyze', 'reanalyze', 'POST /api/quality/reanalyze');
export const REDOWNLOAD = endpoint('RedownloadQuality', 'redownloadQuality', 'POST /api/quality/redownload');
export const SET_FOLDER = endpoint('SetQualityFolder', 'setQualityFolder', 'PUT /api/quality/folder', {
  result: r => r?.folder,
});
export const CLEAR = endpoint('ClearQuality', 'clearQuality', 'POST /api/quality/entries/clear');

/** Liste et seuils, lus « cache puis réseau » : la liste s'affiche hors ligne. */
export const watchQualityEntries = () => api.watch(QUALITY_ENTRIES);

/** Lignes ajoutées, modifiées ou retirées au fil de l'analyse. */
export const qualityUpdates = () => api.subscribe(QUALITY_UPDATED);

export const setQualityFolder = folder =>
  api.mutate(SET_FOLDER, { folder }, { label: 'Modifier le dossier analysé', queueable: false });

export const saveQualityThresholds = thresholds =>
  api.mutate(
    UPDATE_THRESHOLDS,
    { thresholds },
    { label: 'Modifier les seuils de qualité', refetch: ['QualityEntries'] },
  );

export const startQualityScan = (folder, deep) =>
  api.mutate(START_SCAN, { folder, deep }, { label: 'Analyser la qualité', queueable: false });

export const clearQuality = () => api.mutate(CLEAR, {}, { label: 'Vider la liste de qualité', queueable: false });

export const redownloadQuality = paths =>
  api.mutate(REDOWNLOAD, { paths }, { label: `Retélécharger ${paths.length} titre(s)`, queueable: false });

export const reanalyzeTrack = path => api.mutate(REANALYZE, { paths: [path] }, { label: 'Réanalyser un titre' });
