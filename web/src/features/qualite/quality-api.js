import { gql } from '@apollo/client';
import { api } from '../../core/graphql/api';

const THRESHOLD_FIELDS =
  'hissDb cracklePct clippingPct gapDensityPct durationExcess durationGap tailSeconds headSeconds deep';
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
export const START_SCAN = gql`
  mutation StartQualityScan($folder: String, $deep: Boolean) {
    startQualityScan(folder: $folder, deep: $deep) {
      id
    }
  }
`;
export const REANALYZE = gql`
  mutation Reanalyze($paths: [String!]!) {
    reanalyze(paths: $paths) {
      id
    }
  }
`;
export const REDOWNLOAD = gql`
  mutation RedownloadQuality($paths: [String!]!) {
    redownloadQuality(paths: $paths) {
      requested
      skipped
      skippedPaths
      jobs {
        id
      }
    }
  }
`;
export const SET_FOLDER = gql`
  mutation SetQualityFolder($folder: String!) {
    setQualityFolder(folder: $folder)
  }
`;
export const CLEAR = gql`
  mutation ClearQuality {
    clearQuality
  }
`;

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
