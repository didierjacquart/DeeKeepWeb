import { gql } from '@apollo/client';
import { JOB_FIELDS } from '../../core/jobs/jobs.service';

/** Options de rangement (volet « Filtres »), clés org_* des Paramètres. */
export interface OrganizeOptions {
  artist: boolean;
  album: boolean;
  rename: boolean;
  pattern: string;
  divers: boolean;
  diversMin: number;
  keepAlbums: boolean;
  dedup: boolean;
  removeEmpty: boolean;
  reportUntagged: boolean;
  filtersOpen: boolean;
}

export interface OrganizeSession {
  id: string;
  root: string;
  rootDb: string;
  jobId: string | null;
  /** running, done, stopped, failed. */
  state: string;
  total: number;
  read: number;
  message: string | null;
}

export interface OrganizeEntry {
  index: number;
  path: string;
  dbPath: string;
  name: string;
  /** Dossier relatif à la racine, « / » comme séparateur ; vide pour la racine. */
  folder: string;
  title: string;
  titleTagged: boolean;
  artist: string;
  album: string;
  albumArtist: string;
  genre: string;
  date: string;
  track: string;
  format: string;
  needsExtras: boolean;
}

export type PlanStatus = 'move' | 'inplace' | 'album' | 'duplicate' | 'untagged';

export interface PlanRow {
  index: number;
  status: PlanStatus;
  action: string;
  tooltip: string | null;
  destination: string | null;
  missing: string[];
  defaultChecked: boolean;
  needsLookup: boolean;
}

export interface OrganizePlan {
  sessionId: string;
  options: OrganizeOptions;
  rows: PlanRow[];
  totalRows: number;
  stats: { analyzed: number; move: number; inPlace: number; untagged: number; duplicate: number; divers: number; fromDivers: number; album: number; albumDirs: number };
  statusText: string;
  filtersSummary: string;
  albumFolders: { folder: string; label: string }[];
  preview: { text: string; warning: boolean; unknownTokens: string[] };
  canApply: boolean;
}

export interface NameTags {
  categories: { name: string; items: { label: string; token: string }[] }[];
  presets: string[];
  defaultPattern: string;
}

export interface OrganizeState {
  session: OrganizeSession | null;
  defaultRoot: string;
  options: OrganizeOptions;
  targetFormat: string;
}

export const OPTIONS_FIELDS = 'artist album rename pattern divers diversMin keepAlbums dedup removeEmpty reportUntagged filtersOpen';
const SESSION_FIELDS = 'id root rootDb jobId state total read message';
export const ENTRY_FIELDS = 'index path dbPath name folder title titleTagged artist album albumArtist genre date track format needsExtras';

export const STATE = gql`query OrganizeState { organizeState { session { ${SESSION_FIELDS} } defaultRoot targetFormat options { ${OPTIONS_FIELDS} } } }`;
export const NAME_TAGS = gql`query OrganizeNameTags { organizeNameTags { defaultPattern presets categories { name items { label token } } } }`;
export const ENTRIES = gql`query OrganizeEntries($sessionId: UUID!) { organizeEntries(sessionId: $sessionId) { total items { ${ENTRY_FIELDS} } } }`;
export const PLAN = gql`
  query OrganizePlan($sessionId: UUID!, $options: OrganizeOptionsInput) {
    organizePlan(sessionId: $sessionId, options: $options) {
      sessionId totalRows statusText filtersSummary canApply options { ${OPTIONS_FIELDS} }
      stats { analyzed move inPlace untagged duplicate divers fromDivers album albumDirs }
      rows { index status action tooltip destination missing defaultChecked needsLookup }
      albumFolders { folder label }
      preview { text warning unknownTokens }
    }
  }
`;
export const PREVIEW = gql`query OrganizePreview($sessionId: UUID, $pattern: String!, $rename: Boolean!) { organizePreview(sessionId: $sessionId, pattern: $pattern, rename: $rename) { text warning unknownTokens } }`;
export const CHECKS = gql`query OrganizeChecks($sessionId: UUID!) { organizeChecks(sessionId: $sessionId) { checked unchecked } }`;
export const PROPOSALS = gql`query OrganizeProposals($jobId: UUID!) { organizeProposals(jobId: $jobId) { jobId itemKey status message path data at } }`;

export const START = gql`mutation StartOrganizeAnalysis($root: String!) { startOrganizeAnalysis(root: $root) { session { ${SESSION_FIELDS} } job { ${JOB_FIELDS} } } }`;
export const SAVE_OPTIONS = gql`mutation SaveOrganizeOptions($options: OrganizeOptionsInput!) { saveOrganizeOptions(options: $options) { ${OPTIONS_FIELDS} } }`;
export const SET_CHECKS = gql`mutation SetOrganizeChecks($sessionId: UUID!, $indices: [Int!]!, $value: Boolean!) { setOrganizeChecks(sessionId: $sessionId, indices: $indices, value: $value) { checked unchecked } }`;
export const RESET_CHECKS = gql`mutation ResetOrganizeChecks($sessionId: UUID!) { resetOrganizeChecks(sessionId: $sessionId) { checked unchecked } }`;
export const APPLY = gql`mutation ApplyOrganize($sessionId: UUID!, $indices: [Int!]!, $options: OrganizeOptionsInput) { applyOrganize(sessionId: $sessionId, indices: $indices, options: $options) { ${JOB_FIELDS} } }`;
export const DUPLICATES = gql`mutation FindOrganizeDuplicates($sessionId: UUID!) { findOrganizeDuplicates(sessionId: $sessionId) { ${JOB_FIELDS} } }`;
export const LOOKUP = gql`
  mutation StartOrganizeLookup($sessionId: UUID!, $indices: [Int!]!) {
    startOrganizeLookup(sessionId: $sessionId, indices: $indices) {
      job { ${JOB_FIELDS} }
      files { index path dbPath relative title titleTagged artist album coreMissing }
    }
  }
`;
export const WRITE_TAGS = gql`mutation WriteOrganizeTags($sessionId: UUID, $items: [OrganizeTagWriteInput!]!) { writeOrganizeTags(sessionId: $sessionId, items: $items) { ${JOB_FIELDS} } }`;
export const DELETE_FILES = gql`mutation DeleteOrganizeFiles($paths: [String!]!, $sessionId: UUID) { deleteOrganizeFiles(paths: $paths, sessionId: $sessionId) { deleted failed errors } }`;

/** Groupes de doublons : résultat de la tâche organize.duplicates. */
export interface DuplicateFile {
  index: number;
  path: string;
  dbPath: string;
  title: string;
  artist: string;
  format: string;
  bitrate: number;
  size: number;
  keep: boolean;
}
export interface DuplicatesResult {
  sessionId: string;
  groups: { number: number; files: DuplicateFile[] }[];
  fileCount: number;
  targetFormat: string;
}

export interface LookupFile {
  index: number;
  path: string;
  dbPath: string;
  relative: string;
  title: string;
  titleTagged: boolean;
  artist: string;
  album: string;
  coreMissing: boolean;
}
