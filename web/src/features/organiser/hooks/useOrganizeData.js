import { useEffect, useEffectEvent, useMemo, useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { useJob } from '../../../core/jobs/useJobs';
import { connectivity, linkState } from '../../../core/offline/connectivity';
import { useLatest } from '../../../lib/hooks/useLatest';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { fetchEntries, fetchNameTags, fetchOrganizeState, fetchPlan, fetchTouched } from '../organiser-api';
import { mergeEntries } from '../organize-checks';
import { isTerminal } from '../organize-labels';
import { DEFAULT_OPTIONS, displayedOptions, optionsInput } from '../organize-options';

/**
 * Données de la page tenues côté serveur : session d'analyse, fichiers lus (reçus par lots pendant l'analyse),
 * plan, options et cases posées à la main. Chargées à l'ouverture et à chaque reconnexion ; quand l'analyse se
 * termine (ou s'arrête), la liste finale fait foi et le plan se calcule.
 *
 * Les traitements asynchrones reçoivent explicitement la session, les fichiers et les options qu'ils viennent
 * d'obtenir (l'état React n'est relu qu'au rendu suivant) ; après une attente, ils relisent `live`.
 *
 * @param {(sessionId: string | undefined) => void} onLoaded appelé à la fin de chaque chargement réussi.
 */
export function useOrganizeData(onLoaded) {
  const [loaded, setLoaded] = useState(false);
  const [defaultRoot, setDefaultRoot] = useState('');
  const [session, setSession] = useState(null);
  const [entries, setEntries] = useState([]);
  const [plan, setPlan] = useState(null);
  const [options, setOptions] = useState(DEFAULT_OPTIONS);
  const [nameTags, setNameTags] = useState(null);
  const [preview, setPreview] = useState(null);
  /** Cases posées à la main (index → coché) ; le reste suit la sélection par défaut du plan. */
  const [touched, setTouched] = useState(() => new Map());
  const [message, setMessage] = useState('');
  /** Analyse dont les lots sont écoutés ; un nouvel objet rouvre l'abonnement. */
  const [batch, setBatch] = useState(null);

  const job = useJob(session?.jobId);
  const scanning = session?.state === 'running' && (!job || job.status === 'QUEUED' || job.status === 'RUNNING');
  // Valeurs relues par les traitements asynchrones (après une attente, celles du rendu d'origine sont périmées).
  const live = useLatest({ session, entries, options, plan, defaultRoot });

  const loadEntries = async s => {
    const items = await fetchEntries(s.id);
    setEntries(items);
    return items;
  };

  /** Recalcule le plan à partir des fichiers déjà lus : ni relecture du disque, ni modification des cases. */
  const loadPlan = async (s, list, o) => {
    if (!s || list.length === 0) {
      setPlan(null);
      return;
    }
    const p = await fetchPlan(s.id, optionsInput(o));
    setPlan(p);
    setPreview(p.preview);
    setMessage('');
  };

  const loadPlanAndChecks = async (s, list, o) => {
    if (!s) return;
    const [, map] = await Promise.all([loadPlan(s, list, o), fetchTouched(s.id)]);
    setTouched(map);
  };

  const listenBatches = s => setBatch({ jobId: s.jobId });

  /** Vide l'analyse affichée (nouveau dossier, nouvelle analyse) et cesse d'écouter ses lots. */
  const clearAnalysis = () => {
    setEntries([]);
    setPlan(null);
    setTouched(new Map());
    setPreview(null);
    setBatch(null);
  };

  /** Suite du chargement, une fois l'état enregistré et les balises reçus. */
  const applyLoaded = async ([state, tags]) => {
    setNameTags(tags);
    setDefaultRoot(state.defaultRoot);
    const o = displayedOptions(state.options, typeof window !== 'undefined' && window.innerWidth < 900);
    setOptions(o);
    setSession(state.session);
    if (state.session) {
      const list = await loadEntries(state.session);
      if (state.session.state !== 'running') await loadPlanAndChecks(state.session, list, o);
      else listenBatches(state.session);
    }
    onLoaded(state.session?.id);
    setLoaded(true);
  };

  const loadFailed = e => {
    setMessage(
      linkState(connectivity.state) === 'online'
        ? `Chargement impossible : ${e instanceof Error ? e.message : e}`
        : 'Hors ligne : affichage de la dernière analyse connue.',
    );
    setLoaded(true);
  };

  /** Analyse terminée : la session fraîche fait foi (si c'est toujours celle affichée), le plan se calcule. */
  const finishAnalysis = async state => {
    const fresh = state.session;
    if (!session || !fresh || fresh.id !== session.id) return;
    setSession(fresh);
    const list = await loadEntries(fresh);
    if (list.length === 0) {
      setPlan(null);
      setMessage('Aucun fichier audio trouvé dans ce dossier.');
      return;
    }
    await loadPlanAndChecks(fresh, list, live.current.options);
  };

  // Chargement à l'ouverture, puis à chaque reconnexion.
  const onFetched = useEffectEvent(applyLoaded);
  const onLoadFailed = useEffectEvent(loadFailed);
  useEffect(() => {
    const load = () => {
      Promise.all([fetchOrganizeState(), fetchNameTags()])
        .then(r => onFetched(r))
        .catch(e => onLoadFailed(e));
    };
    load();
    return connectivity.onReconnect(load);
  }, []);

  // L'analyse s'est terminée (ou arrêtée) : la liste finale fait foi, le plan se calcule.
  const jobStatus = job?.status ?? '';
  const onAnalysisDone = useEffectEvent(finishAnalysis);
  useEffect(() => {
    if (!isTerminal(jobStatus)) return;
    fetchOrganizeState()
      .then(state => onAnalysisDone(state))
      .catch(() => {
        // Hors ligne : la reconnexion relance le chargement.
      });
  }, [jobStatus]);

  const batches = useMemo(() => (batch ? jobs.items({ type: 'organize.analyze' }) : null), [batch]);
  useSubscription(batches, d => {
    const item = d?.jobItem;
    if (!item || item.jobId !== batch?.jobId || !Array.isArray(item.data)) return;
    setEntries(list => mergeEntries(list, item.data));
  });

  return {
    loaded,
    defaultRoot,
    session,
    setSession,
    entries,
    setEntries,
    plan,
    setPlan,
    options,
    setOptions,
    nameTags,
    preview,
    setPreview,
    touched,
    setTouched,
    message,
    setMessage,
    job,
    scanning,
    live,
    loadEntries,
    loadPlan,
    loadPlanAndChecks,
    listenBatches,
    clearAnalysis,
  };
}
