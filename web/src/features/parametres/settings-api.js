import { gql } from '@apollo/client';
import { JOB_FIELDS } from '../../core/jobs/jobs';

export const SETTINGS_Q = gql`
  query Settings {
    settings
    databaseInfo {
      path
      sizeBytes
      tables {
        table
        rows
        deleted
      }
      historyCount
      coverBytes
      downloadDir
      localDownloadDir
      presenceReady
      presenceFiles
      desktopDataDir
    }
  }
`;

export const UPDATE = gql`
  mutation UpdateSettings($values: Any!) {
    updateSettings(values: $values)
  }
`;

export const RESCAN = gql`mutation RescanLibrary { rescanLibrary { ${JOB_FIELDS} } }`;

export const IMPORT = gql`mutation ImportDesktopData($dataDir: String) { importDesktopData(dataDir: $dataDir) { ${JOB_FIELDS} } }`;

export const SETTINGS_CHANGED = gql`
  subscription SettingsChanged {
    settingsChanged
  }
`;
