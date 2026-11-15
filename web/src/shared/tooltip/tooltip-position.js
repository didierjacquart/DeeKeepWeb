const MARGIN = 8;

/** Position de la bulle : centrée sous l'élément, au-dessus s'il n'y a pas la place, dans la fenêtre. */
export function tooltipPosition(anchor, size, viewport) {
  const left = Math.min(
    Math.max(MARGIN, anchor.left + anchor.width / 2 - size.width / 2),
    viewport.width - MARGIN - size.width,
  );
  const below = anchor.bottom + MARGIN;
  const top =
    below + size.height > viewport.height - MARGIN ? Math.max(MARGIN, anchor.top - MARGIN - size.height) : below;
  return { left, top };
}
