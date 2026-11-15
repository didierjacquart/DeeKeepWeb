/**
 * Nom d'artiste ou d'album : lien d'exploration quand Deezer donne son identifiant, texte sinon.
 * @param {{ label: string | null, enabled: boolean, tip?: string, onOpen: () => void }} props
 */
export function ExploreLink({ label, enabled, tip, onOpen }) {
  if (!enabled) return label;
  return (
    <button type="button" className="sd-link-a" data-tip={tip} onClick={onOpen}>
      {label}
    </button>
  );
}
