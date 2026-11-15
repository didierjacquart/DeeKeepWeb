const MARGIN = 8;

/**
 * Position d'un panneau flottant ancré en (x, y) : sous le point, ou décalé pour
 * rester dans la fenêtre. `flipX` : abscisse où se rabattre à gauche (sous-menu).
 *
 * @param {{ x: number, y: number }} at
 * @param {{ width: number, height: number }} size
 * @param {{ width: number, height: number }} viewport
 * @param {number} [flipX]
 */
export function placeInViewport(at, size, viewport, flipX) {
  let { x, y } = at;
  if (x + size.width > viewport.width - MARGIN) {
    x = flipX != null ? flipX - size.width : Math.max(MARGIN, viewport.width - MARGIN - size.width);
  }
  if (y + size.height > viewport.height - MARGIN) y = Math.max(MARGIN, viewport.height - MARGIN - size.height);
  return { x, y };
}

/** Applique {@link placeInViewport} directement au style de l'élément mesuré. */
export function placeElement(el, at, flipX) {
  const { width, height } = el.getBoundingClientRect();
  const { x, y } = placeInViewport(
    at,
    { width, height },
    { width: window.innerWidth, height: window.innerHeight },
    flipX,
  );
  el.style.left = `${x}px`;
  el.style.top = `${y}px`;
}
