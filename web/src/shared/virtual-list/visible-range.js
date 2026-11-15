/**
 * Lignes à dessiner d'une liste à hauteur fixe : celles de la zone visible,
 * plus `buffer` pixels de part et d'autre.
 *
 * @param {{ count: number, itemHeight: number, scrollTop: number, height: number, buffer: number }} p
 * @returns {{ start: number, end: number }} indices [start, end)
 */
export function visibleRange({ count, itemHeight, scrollTop, height, buffer }) {
  const total = count * itemHeight;
  // Liste raccourcie (filtre) : la position ne peut dépasser la nouvelle fin.
  const top = Math.min(scrollTop, Math.max(0, total - height));
  const start = Math.max(0, Math.floor((top - buffer) / itemHeight));
  const end = Math.min(count, Math.ceil((top + height + buffer) / itemHeight));
  return { start, end };
}
