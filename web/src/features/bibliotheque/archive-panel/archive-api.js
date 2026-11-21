import { endpoint, serverEvent } from '../../../core/api/endpoints';

/** Appels serveur de l'archive (configuration, jauge, copies, retrait). */

export const ARCHIVE_CONFIG = endpoint('ArchiveConfig', 'archiveConfig', 'GET /api/archive/config');
/** Variables : `root` (emplacement saisi, pas encore enregistré ; facultatif). */
export const ARCHIVE_USAGE = endpoint('ArchiveUsage', 'archiveUsage', 'GET /api/archive/usage');
export const ARCHIVE_PLAN = endpoint('ArchivePlan', 'archivePlan', 'POST /api/archive/plan');
export const UPDATE_CONFIG = endpoint('UpdateArchiveConfig', 'updateArchiveConfig', 'PUT /api/archive/config');
export const TEST = endpoint('TestArchive', 'testArchive', 'POST /api/archive/test');
export const ARCHIVE = endpoint('ArchiveTracks', 'archiveTracks', 'POST /api/archive/archive');
export const RESTORE = endpoint('RestoreTracks', 'restoreTracks', 'POST /api/archive/restore');
export const REMOVE = endpoint('RemoveFromArchive', 'removeFromArchive', 'POST /api/archive/remove');
export const MEASURE = endpoint('MeasureArchive', 'measureArchive', 'POST /api/archive/measure');
export const CHECK = endpoint('CheckArchive', 'checkArchive', 'POST /api/archive/check');
export const ARCHIVE_UPDATED = serverEvent('ArchiveUpdated', 'archiveUpdated', 'archive.updated');
