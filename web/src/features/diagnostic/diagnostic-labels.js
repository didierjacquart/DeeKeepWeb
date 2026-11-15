import { formatWhen } from '../../shared/format';

const LABELS = {
  catalog: 'Catalogue (Deezer, MusicBrainz, AcoustID)',
  library: 'Bibliothèque (titres, jaquettes, réglages)',
  downloader: 'Téléchargement (yt-dlp, ffmpeg)',
  quality: 'Qualité (analyse ffmpeg)',
  storage: 'Archive',
};

const LINK_LABELS = { online: 'Connecté', reconnecting: 'Reconnexion…' };

/** Nom lisible d'un microservice (son identifiant s'il est inconnu). */
export function serviceLabel(service) {
  return LABELS[service] ?? service;
}

/** Durée écoulée depuis le démarrage (secondes Unix) : « 2 j 3 h », « 3 h 12 min », « 7 min ». */
export function uptime(startedAt, now = Date.now()) {
  const s = Math.max(0, Math.floor(now / 1000 - startedAt));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  return d ? `${d} j ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
}

/** État de la liaison du client (online / reconnecting / offline) en clair. */
export function linkLabel(link) {
  return LINK_LABELS[link] ?? 'Hors ligne';
}

/** Infobulle d'un lien d'événements : sa dernière erreur, sinon depuis quand il est connecté. */
export function linkTip(link) {
  return (link.lastError ?? (link.since ? 'Connecté ' + formatWhen(link.since) : '')) || undefined;
}

/** Outils externes de tous les services, chacun marqué du service qui le déclare. */
export function toolsOf(services) {
  return services.flatMap(s => (s.info?.tools ?? []).map(t => ({ ...t, service: s.service })));
}

/** Version et détail d'un outil ; « Non détecté » s'il est absent sans explication. */
export function toolText(tool) {
  const version = tool.version ?? (tool.available || tool.detail ? '' : 'Non détecté');
  const detail = tool.detail ? (tool.version ? ' · ' : '') + tool.detail : '';
  return version + detail;
}
