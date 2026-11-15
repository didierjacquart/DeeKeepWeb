import { player } from '../../../core/player/player';
import { useLatest } from '../../../lib/hooks/useLatest';
import { progressOf, statusText } from '../organize-labels';
import { folderQueue } from '../organize-tree';
import { useAnalysis } from './useAnalysis';
import { useApplyPlan } from './useApplyPlan';
import { useChecks } from './useChecks';
import { useFindDuplicates } from './useFindDuplicates';
import { useLookupResume } from './useLookupResume';
import { useOptionEditing } from './useOptionEditing';
import { useOrganizeData } from './useOrganizeData';
import { useTagLookup } from './useTagLookup';
import { useTreeView } from './useTreeView';

/**
 * Toute la logique de la page Organiser : données serveur, arbre, cases, analyse, rangement, doublons, tags.
 * La page n'a plus qu'à dessiner ce que ce hook rend.
 */
export function useOrganizer() {
  const lookup = useLookupResume();
  const data = useOrganizeData(lookup.restore);
  const view = useTreeView({ entries: data.entries, plan: data.plan, rootName: data.session?.root ?? '' });
  const checks = useChecks({ ...data, rowByIndex: view.rowByIndex });
  const applier = useApplyPlan({ data, checkedSet: checks.checkedSet });
  const { busy, applying, applyJob } = applier;
  const busyRef = useLatest(busy);
  const analysis = useAnalysis({
    data,
    busyRef,
    resetFolds: view.resetFolds,
    clearResult: () => applier.setResult(null),
  });
  const findDuplicates = useFindDuplicates({ data, busy });
  const tags = useTagLookup({
    session: data.session,
    busy,
    lookupIndices: checks.lookupIndices,
    lookup,
    analyze: analysis.analyze,
  });
  const setOption = useOptionEditing(data);

  const play = entry => {
    const { track, queue } = folderQueue(view.visible, entry);
    player.play(track, queue);
  };

  const { scanning, job, session, plan } = data;
  return {
    ...data,
    view,
    checks,
    busy,
    applying,
    result: applier.result,
    clearResult: () => applier.setResult(null),
    apply: applier.apply,
    stopApply: applier.stopApply,
    ...analysis,
    findDuplicates,
    ...tags,
    resume: lookup.resume,
    setOption,
    play,
    root: session?.root || data.defaultRoot,
    canApply: !busy && !!plan?.canApply,
    progress: progressOf(applying ? applyJob : job),
    statusText: statusText({ applying, applyJob, scanning, job, session, message: data.message, plan }),
  };
}
