/**
 * Assemble des noms de classes CSS en ignorant les valeurs fausses.
 * @param {...(string | false | null | undefined)} names
 */
export function cx(...names) {
  return names.filter(Boolean).join(' ');
}
