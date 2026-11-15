import { useState } from 'react';
import { useHistoryData } from './hooks/useHistoryData';
import { useHistoryView } from './hooks/useHistoryView';
import { useSearchCovers } from './hooks/useSearchCovers';
import { DownloadsTab } from './components/DownloadsTab';
import { HistoryTabs } from './components/HistoryTabs';
import { SearchesTab } from './components/SearchesTab';
import './history-page.scss';

/**
 * Historique d'utilisation : recherches effectuées (relançables) et téléchargements réussis
 * (écoute, jaquette agrandissable, purge des introuvables, vidage confirmé).
 */
export function HistoryPage() {
  const [tab, setTab] = useState('searches');
  const { history, searches, loading, stale } = useHistoryData();
  const view = useHistoryView(history, searches);
  const coverOf = useSearchCovers(view.shownSearches);

  return (
    <div className="dk-history-page">
      <section className="dk-page">
        <h1 className="dk-page-title">Historique d'utilisation</h1>

        <HistoryTabs tab={tab} onChange={setTab} searchCount={searches.length} downloadCount={history.length} />

        {stale && <p className="dk-help dk-error-text">Hors ligne : historique servi par le cache local.</p>}

        {tab === 'searches' ? (
          <SearchesTab view={view} coverOf={coverOf} count={searches.length} loading={loading} />
        ) : (
          <DownloadsTab view={view} count={history.length} loading={loading} />
        )}
      </section>
    </div>
  );
}
