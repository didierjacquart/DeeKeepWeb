import { ProgressBar } from '../../shared/components/ProgressBar';
import { ActionBar } from './components/ActionBar';
import { FolderBar } from './components/FolderBar';
import { OptionsPanel } from './components/OptionsPanel';
import { OrganizeTree } from './components/OrganizeTree';
import { ResultCard } from './components/ResultCard';
import { StatusLine } from './components/StatusLine';
import { useOrganizer } from './hooks/useOrganizer';
import './organize-page.scss';

/**
 * Page « Organiser la bibliothèque » : analyse d'un dossier (tâche serveur, arbre rempli au fil de l'eau),
 * plan de rangement recalculé à chaque changement d'option sans toucher aux cases, application, recherche des
 * doublons, compléter les tags. L'analyse, les cases et la revue des tags vivent côté serveur : rien ne se perd
 * à un rechargement ou une coupure.
 */
export function OrganizePage() {
  const o = useOrganizer();
  const { options, busy, scanning, view } = o;

  return (
    <section className="dk-page dk-organize-page">
      <h1 className="dk-page-title">Organiser la bibliothèque</h1>
      <p className="dk-help">
        Range les fichiers déjà présents selon leurs tags, comme le fait le téléchargement, en réunissant le plus de
        titres possible sous un même artiste. L'analyse ne modifie rien : le plan ci-dessous n'est appliqué qu'après
        validation, dossier par dossier ou fichier par fichier.
      </p>

      <FolderBar
        root={o.root}
        filtersOpen={options.filtersOpen}
        busy={busy}
        onToggleFilters={() => o.setOption('filtersOpen', !options.filtersOpen)}
        onChooseFolder={() => void o.chooseFolder()}
      />

      {!options.filtersOpen ? (
        <p className="dk-help summary">Filtres : {o.plan?.filtersSummary || 'voir le volet'}</p>
      ) : (
        <OptionsPanel
          options={options}
          nameTags={o.nameTags}
          preview={o.preview}
          onOption={o.setOption}
          canCheckPending={!!o.plan && !busy}
          onCheckPending={() => void o.checks.checkPending()}
        />
      )}

      <ActionBar o={o} />

      <StatusLine text={o.statusText} hideInPlace={view.hideInPlace} onToggleHide={view.toggleHideInPlace} />
      {(busy || (o.progress !== null && scanning)) && <ProgressBar value={o.progress} />}

      {o.result && <ResultCard text={o.result} onClose={o.clearResult} />}

      <OrganizeTree
        visible={view.visible}
        entryCount={o.entries.length}
        loaded={o.loaded}
        scanning={scanning}
        hasPlan={!!o.plan}
        view={view}
        checks={o.checks}
        onPlay={o.play}
      />
    </section>
  );
}
