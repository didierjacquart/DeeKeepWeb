import { combine, endpoint } from '../../core/api/endpoints';

/** État des services et réglages (versions d'outils, dossier de dépôt…). */
export const SERVICES_Q = combine(
  'Services',
  endpoint('Services', 'services', 'GET /api/services'),
  endpoint('Settings', 'settings', 'GET /api/settings'),
);

export const UPDATE_YTDLP = endpoint('UpdateYtDlp', 'updateYtDlp', 'POST /api/downloads/tools/update-ytdlp');
