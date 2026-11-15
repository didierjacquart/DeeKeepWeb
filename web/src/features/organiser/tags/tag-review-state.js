import { toggled } from '../organize-tree';
import { isSure } from './tag-review-model';

/**
 * État de la revue : propositions reçues (clé du fichier → statut et proposition) et cases « Écrire ».
 * Une correspondance sûre est cochée d'office à son arrivée.
 */
export const emptyReview = () => ({ results: new Map(), checked: new Set() });

export function reviewReducer(state, action) {
  switch (action.type) {
    case 'proposal': {
      const { key, status, proposal } = action;
      const found = proposal && proposal.proposed ? proposal : null;
      const results = new Map(state.results).set(key, { status, proposal: found });
      const checked = found && isSure(found) ? new Set(state.checked).add(key) : state.checked;
      return { results, checked };
    }
    case 'toggle':
      return { ...state, checked: toggled(state.checked, action.key) };
    case 'check':
      return { ...state, checked: new Set(action.keys) };
    default:
      return state;
  }
}
