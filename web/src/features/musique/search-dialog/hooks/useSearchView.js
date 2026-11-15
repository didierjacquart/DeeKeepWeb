import { useMemo } from 'react';
import { isDuplicate, presenceCounts } from '../search-model';
import { allVisibleChecked, sortedEntities, visibleTracks } from '../track-list';

/** Ce que la fenêtre affiche, dérivé de son état : lignes filtrées et triées, compteurs. */
export function useSearchView(state) {
  const { tracks, entities, filter, duplicatesOnly, sortColumn, sortAsc, entitySort, picked } = state;

  const visible = useMemo(
    () => visibleTracks(tracks, { filter, duplicatesOnly, sortColumn, sortAsc }),
    [tracks, filter, duplicatesOnly, sortColumn, sortAsc],
  );
  const shownEntities = useMemo(() => sortedEntities(entities, entitySort), [entities, entitySort]);
  const { present, possible } = useMemo(() => presenceCounts(tracks), [tracks]);
  const dedupCandidates = useMemo(() => tracks.filter(t => isDuplicate(t) && t.presence.files.length > 0), [tracks]);
  const allChecked = useMemo(() => allVisibleChecked(visible, picked), [visible, picked]);

  return { visible, shownEntities, present, possible, dedupCandidates, allChecked };
}
