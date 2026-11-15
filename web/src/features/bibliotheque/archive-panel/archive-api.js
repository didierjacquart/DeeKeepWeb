import { gql } from '@apollo/client';
import { JOB_FIELDS } from '../../../core/jobs/jobs';

/** Documents GraphQL de l'archive (configuration, jauge, copies, retrait). */

const CONFIG_FIELDS = 'root prefix archiveFile configured';
const USAGE_FIELDS = `objectCount storedBytes archiveFileBytes diskFreeBytes diskTotalBytes tracksArchived tracksStale
  tracksNotArchived bytesToArchive tracksOnDisk bytesOnDisk sameVolume configured archiveFile error`;

export const ARCHIVE_CONFIG = gql`query ArchiveConfig { archiveConfig { ${CONFIG_FIELDS} } }`;
export const ARCHIVE_USAGE = gql`query ArchiveUsage($root: String) { archiveUsage(root: $root) { ${USAGE_FIELDS} } }`;
export const ARCHIVE_PLAN = gql`
  query ArchivePlan($paths: [String!]!) {
    archivePlan(paths: $paths) {
      requested
      onDisk
      restorable
      restorableElsewhere
      configured
    }
  }
`;
export const UPDATE_CONFIG = gql`mutation UpdateArchiveConfig($root: String, $prefix: String) { updateArchiveConfig(root: $root, prefix: $prefix) { ${CONFIG_FIELDS} } }`;
export const TEST = gql`
  mutation TestArchive($root: String) {
    testArchive(root: $root) {
      ok
      message
    }
  }
`;
export const ARCHIVE = gql`mutation ArchiveTracks($paths: [String!]!) { archiveTracks(paths: $paths) { ${JOB_FIELDS} } }`;
export const RESTORE = gql`mutation RestoreTracks($paths: [String!]!) { restoreTracks(paths: $paths) { ${JOB_FIELDS} } }`;
export const REMOVE = gql`
  mutation RemoveFromArchive($paths: [String!]!) {
    removeFromArchive(paths: $paths)
  }
`;
export const MEASURE = gql`mutation MeasureArchive($root: String) { measureArchive(root: $root) { ${JOB_FIELDS} } }`;
export const CHECK = gql`mutation CheckArchive { checkArchive { ${JOB_FIELDS} } }`;
export const ARCHIVE_UPDATED = gql`
  subscription ArchiveUpdated {
    archiveUpdated {
      paths
      reason
    }
  }
`;
