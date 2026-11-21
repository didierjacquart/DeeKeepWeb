import { useState } from 'react';
import { api } from '../../../core/api/api';
import { useWatch } from '../../../core/api/useWatch';
import { notify } from '../../../core/ui/notify';
import { SAVE_TRASH_SETTINGS, TRASH_SETTINGS } from '../../bibliotheque/trash/trash-api';
import { retentionHelp, parseRetention } from '../trash-retention';

/**
 * Corbeille : nombre de jours pendant lesquels un titre supprimé depuis l'application reste
 * récupérable (bibliothèque et disque USB). 0 désactive l'effacement automatique.
 */
export function TrashSettingsCard() {
  const watched = useWatch(TRASH_SETTINGS);
  const saved = watched.data?.trashSettings?.retentionDays ?? null;
  // Saisie en cours ; null = on affiche la valeur enregistrée.
  const [draft, setDraft] = useState(null);
  const value = draft ?? (saved === null ? '' : String(saved));
  const parsed = parseRetention(value);

  const save = async () => {
    if (draft === null) return;
    if (parsed === null) {
      notify.error('Durée invalide : un nombre de jours entre 0 et 3650.');
      return;
    }
    if (parsed === saved) {
      setDraft(null);
      return;
    }
    const r = await api.mutate(
      SAVE_TRASH_SETTINGS,
      { retentionDays: parsed },
      { label: 'Durée de conservation de la corbeille', queueable: false, refetch: ['TrashSettings'] },
    );
    if (r.error) notify.error(r.error);
    else {
      setDraft(null);
      notify.success(parsed === 0 ? 'Corbeille : effacement automatique désactivé.' : `Corbeille : ${parsed} jour(s).`);
    }
  };

  if (watched.error && saved === null) {
    return <div className="dk-card form dk-error-text">Réglage de la corbeille indisponible : {watched.error}</div>;
  }

  return (
    <div className="dk-card form">
      <div className="row">
        <label htmlFor="trash-days">Conserver les titres supprimés :</label>
        <span className="inline">
          <input
            id="trash-days"
            className="dk-input trash-days"
            type="number"
            min="0"
            max="3650"
            step="1"
            disabled={saved === null}
            value={value}
            onChange={e => setDraft(e.target.value)}
            onBlur={() => void save()}
            onKeyDown={e => {
              if (e.key === 'Enter') void save();
              if (e.key === 'Escape') setDraft(null);
            }}
          />{' '}
          jour(s)
        </span>
      </div>
      <p className="dk-help">{retentionHelp(parsed ?? saved)}</p>
    </div>
  );
}
