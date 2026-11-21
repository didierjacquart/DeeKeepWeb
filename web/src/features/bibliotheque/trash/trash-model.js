/**
 * Temps restant avant l'effacement définitif d'un fichier de la corbeille.
 * @param {number | null} expiresAt secondes Unix ; null = purge automatique désactivée
 * @param {number} [now] millisecondes (tests)
 */
export function daysLeft(expiresAt, now = Date.now()) {
  if (expiresAt == null) return 'conservé';
  const days = Math.ceil((expiresAt * 1000 - now) / 86_400_000);
  if (days <= 0) return 'effacé sous peu';
  return days === 1 ? '1 jour' : `${days} jours`;
}
