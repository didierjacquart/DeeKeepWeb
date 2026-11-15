import { useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { cx } from '../../lib/cx';
import { useMobile } from '../../lib/hooks/useMediaQuery';
import { useEscape } from '../../lib/hooks/useEscape';
import { offlineQueue } from '../../core/offline/offline-queue';
import { jobs } from '../../core/jobs/jobs';
import { PlayerBar } from '../../core/player/components/PlayerBar';
import { JobsPanel } from '../jobs-panel/JobsPanel';
import { isToolPage, pageTitle } from './nav-items';
import { Sidebar } from './Sidebar';
import { Topbar } from './Topbar';
import './shell.scss';

/**
 * Coquille de l'application : barre latérale fixe sur ordinateur (250 px),
 * tiroir sur mobile ; pied de barre avec l'état de la liaison, les actions en
 * attente et les tâches de fond ; lecteur en bas d'écran.
 */
export function Shell() {
  const mobile = useMobile();
  const { pathname } = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [toolsOpen, setToolsOpen] = useState(true);
  const [jobsOpen, setJobsOpen] = useState(false);
  const [shownPath, setShownPath] = useState(pathname);

  // Changement de page : une sous-page du groupe « Outils » le redéplie (comme
  // l'original) et, sur mobile, le tiroir se referme.
  if (pathname !== shownPath) {
    setShownPath(pathname);
    setDrawerOpen(false);
    if (isToolPage(pathname)) setToolsOpen(true);
  }

  // Chaque page s'ouvre en haut. Corps en accolades : scrollTo rend une promesse
  // dans les navigateurs récents, qu'un effet ne doit pas renvoyer.
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);

  // Tâches de fond et rejeu des actions hors ligne : dès que la session est ouverte.
  useEffect(() => {
    jobs.start();
    offlineQueue.start();
  }, []);

  useEscape(() => setDrawerOpen(false), drawerOpen);

  return (
    <div className={cx('dk-shell', mobile && 'mobile')}>
      {mobile && drawerOpen && <div className="backdrop" aria-hidden="true" onClick={() => setDrawerOpen(false)} />}
      <Sidebar
        open={!mobile || drawerOpen}
        toolsOpen={toolsOpen}
        onToggleTools={() => setToolsOpen(o => !o)}
        onOpenJobs={() => setJobsOpen(true)}
        onToggleJobs={() => setJobsOpen(o => !o)}
      />
      <div className="content">
        {mobile && (
          <Topbar
            title={pageTitle(pathname)}
            onOpenMenu={() => setDrawerOpen(true)}
            onToggleJobs={() => setJobsOpen(o => !o)}
          />
        )}
        <main className="main">
          <Outlet />
        </main>
      </div>
      {jobsOpen && <JobsPanel onClose={() => setJobsOpen(false)} />}
      <PlayerBar />
    </div>
  );
}
