import { notify } from '../../../core/ui/notify';
import { Menu } from '../../../shared/menu/Menu';
import { MenuItem } from '../../../shared/menu/MenuItem';

async function copyPath(entry) {
  if (!entry) return;
  try {
    await navigator.clipboard.writeText(entry.path);
    notify.info('Chemin copié.');
  } catch {
    notify.info(entry.path);
  }
}

/**
 * Menu contextuel d'un fichier de l'arbre.
 * @param {{ ctx: { x: number, y: number, entry: any } | null, onClose: () => void, onPlay: (entry: any) => void }} props
 */
export function FileMenu({ ctx, onClose, onPlay }) {
  return (
    <Menu at={ctx} onClose={onClose}>
      <MenuItem onClick={() => ctx && onPlay(ctx.entry)}>Écouter dans DeeKeep</MenuItem>
      <MenuItem onClick={() => void copyPath(ctx?.entry ?? null)}>Copier le chemin</MenuItem>
    </Menu>
  );
}
