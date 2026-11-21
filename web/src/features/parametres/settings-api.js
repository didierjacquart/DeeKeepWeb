import { combine, endpoint, serverEvent } from '../../core/api/endpoints';

/** Réglages (objet JSON) et informations sur la base. */
export const SETTINGS_Q = combine(
  'Settings',
  endpoint('Settings', 'settings', 'GET /api/settings'),
  endpoint('Settings', 'databaseInfo', 'GET /api/library/database'),
);

/** Variables : `values` (réglages à enregistrer) ; rend les réglages complets. */
export const UPDATE = endpoint('UpdateSettings', 'updateSettings', 'PUT /api/settings', { body: v => v.values });

export const RESCAN = endpoint('RescanLibrary', 'rescanLibrary', 'POST /api/library/rescan');

export const IMPORT = endpoint('ImportDesktopData', 'importDesktopData', 'POST /api/library/import-desktop');

export const SETTINGS_CHANGED = serverEvent('SettingsChanged', 'settingsChanged', 'settings.changed');
