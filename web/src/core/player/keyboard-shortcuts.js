/** Raccourcis du lecteur : Espace, ← →, ↑ ↓, P / N. */
const ACTIONS = {
  ' ': p => p.toggle(),
  ArrowLeft: p => p.seekBy(-5),
  ArrowRight: p => p.seekBy(5),
  ArrowUp: p => p.setVolume(p.state.volume + 0.05),
  ArrowDown: p => p.setVolume(p.state.volume - 0.05),
  p: p => p.previous(),
  P: p => p.previous(),
  n: p => p.next(),
  N: p => p.next(),
};

/** Hors des champs de saisie et sans modificateur : la touche appartient au lecteur. */
export function isPlayerShortcut(e) {
  const target = e.target;
  if (target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(target.tagName))) return false;
  return !(e.ctrlKey || e.metaKey || e.altKey);
}

/**
 * Applique le raccourci de la touche au lecteur s'il joue quelque chose.
 * @returns {boolean} vrai si la touche a été prise en charge (son effet par défaut est alors annulé).
 */
export function handlePlayerKey(player, e) {
  const action = ACTIONS[e.key];
  if (!action || !player.current || !isPlayerShortcut(e)) return false;
  action(player);
  e.preventDefault();
  return true;
}
