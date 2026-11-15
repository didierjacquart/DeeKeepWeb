import { useMemo } from 'react';
import { resetChecks, saveChecks } from '../organiser-api';
import { effectiveChecked, lookupIndicesOf, withChecks } from '../organize-checks';
import { dirState } from '../organize-tree';

/**
 * Cases de l'arbre : case effective de chaque fichier, cases posées à la main (enregistrées côté serveur),
 * fichiers cochés à compléter.
 */
export function useChecks({ session, entries, plan, touched, setTouched, rowByIndex }) {
  const checkedSet = useMemo(() => effectiveChecked(entries, touched, rowByIndex), [entries, touched, rowByIndex]);
  const lookupIndices = useMemo(() => lookupIndicesOf(plan?.rows ?? [], checkedSet), [plan, checkedSet]);

  const setChecks = async (indices, value) => {
    if (!session || indices.length === 0) return;
    setTouched(m => withChecks(m, indices, value));
    await saveChecks(session.id, indices, value);
  };

  const toggleFile = entry => void setChecks([entry.index], !checkedSet.has(entry.index));

  // Un dossier partiel se coche ; l'état partiel ne se propage pas.
  const toggleDir = d => void setChecks(d.files, dirState(d, checkedSet) !== 'all');

  const checkAll = on =>
    void setChecks(
      entries.map(e => e.index),
      on,
    );

  const checkPending = async () => {
    if (!session) return;
    setTouched(new Map());
    await resetChecks(session.id);
  };

  return { checkedSet, lookupIndices, toggleFile, toggleDir, checkAll, checkPending };
}
