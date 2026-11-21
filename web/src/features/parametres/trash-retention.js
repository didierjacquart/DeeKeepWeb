/** Saisie de la durée de conservation : entier de 0 à 3650, sinon null. */
export function parseRetention(text) {
  const t = String(text ?? '').trim();
  if (!/^\d+$/.test(t)) return null;
  const n = Number(t);
  return n <= 3650 ? n : null;
}

/** Explication sous le champ, selon la durée choisie. */
export function retentionHelp(days) {
  if (days == null) return 'Lecture du réglage…';
  if (days === 0)
    return "Effacement automatique désactivé : les titres supprimés restent dans la corbeille jusqu'à ce que vous la vidiez (Bibliothèque → Corbeille).";
  return `Un titre supprimé depuis l'application reste récupérable ${days} jour(s) dans la corbeille (bibliothèque et disque USB), puis il est effacé. 0 = jamais d'effacement automatique.`;
}
