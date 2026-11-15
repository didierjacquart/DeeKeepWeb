import { LinkDot } from './LinkStatus';

/** En-tête mobile : menu, titre de la page, accès aux tâches de fond. */
export function Topbar({ title, onOpenMenu, onToggleJobs }) {
  return (
    <header className="topbar">
      <button className="icon-btn" onClick={onOpenMenu} aria-label="Menu">
        ☰
      </button>
      <span className="topbar-title">{title}</span>
      <span className="dk-spacer" />
      <button className="icon-btn" onClick={onToggleJobs} aria-label="Tâches">
        <LinkDot />
      </button>
    </header>
  );
}
