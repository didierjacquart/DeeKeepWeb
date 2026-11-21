import { endpoint, serverEvent } from '../../core/api/endpoints';

/** État de la synchronisation du disque USB (présence, place, tâche en cours, dernier bilan). */
export const SYNC_STATUS = endpoint('UsbSyncStatus', 'status', 'GET /api/sync/status');

/** « Synchroniser » : tâche usbsync.run (celle qui tourne déjà le cas échéant). */
export const SYNC_RUN = endpoint('UsbSyncRun', 'run', 'POST /api/sync/run');

/** Fin ou avancement d'une synchronisation : l'état est relu. */
export const SYNC_JOB = serverEvent('UsbSyncJob', 'jobUpdated', 'job.updated', job => job.type === 'usbsync.run');
