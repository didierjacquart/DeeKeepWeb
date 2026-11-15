import { useMemo } from 'react';
import { useStore } from '../../../lib/store';
import { cx } from '../../../lib/cx';
import { ProgressBar } from '../../../shared/components/ProgressBar';
import { archiveActions } from './archive-actions';
import { chosenPaths, selectConfigured } from './archive-model';
import { ArchiveBar } from './components/ArchiveBar';
import { ArchiveSettings } from './components/ArchiveSettings';
import { useArchiveSettings } from './hooks/useArchiveSettings';
import { useArchiveWork } from './hooks/useArchiveWork';
import { useCopyCommands } from './hooks/useCopyCommands';
import { useLastFailure } from './hooks/useLastFailure';
import { usePanelOpen } from './hooks/usePanelOpen';
import { usePanelTasks } from './hooks/usePanelTasks';
import { useWorkLine } from './hooks/useWorkLine';
import './archive-panel.scss';

/**
 * Volet « Archive » de la Bibliothèque : emplacement du fichier d'archive,
 * jauge de place, Tester / Mesurer / Enregistrer, et les actions Archiver,
 * Restaurer, Arrêter sur les titres cochés (sinon affichés).
 */
export function ArchivePanel({ selected = [], visible = [] }) {
  const configured = useStore(archiveActions, selectConfigured);
  const config = useStore(archiveActions, s => s.config);
  const { busy, currentWork: work } = useArchiveWork();
  const [open, setOpen] = usePanelOpen();
  const settings = useArchiveSettings();
  const tasks = usePanelTasks(settings);
  const [line, setLine] = useWorkLine(work, settings.requestRefresh);
  const [lastFailure, setLastFailure] = useLastFailure();
  const chosen = useMemo(() => chosenPaths(selected, visible), [selected, visible]);
  const commands = useCopyCommands({ chosen, visible, busy, setLine, setLastFailure, openPanel: () => setOpen(true) });

  const anyActive = busy || tasks.measuring || tasks.checking;
  const archiveFile = settings.usage?.archiveFile ?? config?.archiveFile ?? null;

  return (
    <div className="dk-archive-panel">
      <ArchiveBar
        open={open}
        onToggle={() => setOpen(!open)}
        busy={busy}
        configured={configured}
        anyActive={anyActive}
        onRestore={commands.restore}
        onStop={commands.stop}
        onArchive={commands.archive}
      />

      {work && busy && (
        <ProgressBar
          value={work.total ? work.progress : null}
          label={work.total ? `${work.done + work.failed} / ${work.total}` : ''}
        />
      )}
      {line && <p className={cx('state', line.error && 'dk-error-text')}>{line.text}</p>}
      {lastFailure && <p className="state dk-error-text">{lastFailure}</p>}

      {open && <ArchiveSettings settings={settings} tasks={tasks} archiveFile={archiveFile} />}
    </div>
  );
}
