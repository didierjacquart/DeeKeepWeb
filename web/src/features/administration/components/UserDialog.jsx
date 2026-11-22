import { useState } from 'react';
import { PAGES } from '../../../core/auth/permissions';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { emptyForm, formError, formFromUser, toBody, togglePage } from '../user-form';

/**
 * Création ou modification d'un compte : adresse e-mail et mot de passe (création seulement),
 * rôle, pages ouvertes, droits d'action, désactivation. `onSubmit(corps)` rend le
 * message d'erreur du serveur, affiché sans fermer la boîte, ou null.
 * @param {{ user?: import('../admin-api').AdminUser, onSubmit: (body: object) => Promise<string | null>, close: (r?: boolean) => void }} props
 */
export function UserDialog({ user, onSubmit, close }) {
  const creating = !user;
  const [form, setForm] = useState(() => (user ? formFromUser(user) : emptyForm()));
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const admin = form.role === 'admin';
  const set = fields => setForm(f => ({ ...f, ...fields }));

  const submit = async e => {
    e.preventDefault();
    const invalid = formError(form, creating);
    if (invalid) {
      setError(invalid);
      return;
    }
    setBusy(true);
    const failure = await onSubmit(toBody(form, creating));
    setBusy(false);
    if (failure) setError(failure);
    else close(true);
  };

  return (
    <form className="dk-user-dialog" onSubmit={submit}>
      <DialogTitle>{creating ? 'Nouveau compte' : `Compte « ${user.name} »`}</DialogTitle>
      <DialogContent className="content">
        {creating && (
          <>
            <label className="field">
              <span>Adresse e-mail</span>
              <input
                className="dk-input"
                type="email"
                name="email"
                autoComplete="off"
                autoCapitalize="none"
                value={form.name}
                onChange={e => set({ name: e.target.value })}
                data-autofocus
              />
            </label>
            <label className="field">
              <span>Mot de passe</span>
              <input
                className="dk-input"
                type="password"
                autoComplete="new-password"
                value={form.password}
                onChange={e => set({ password: e.target.value })}
              />
            </label>
            <label className="field">
              <span>Confirmer le mot de passe</span>
              <input
                className="dk-input"
                type="password"
                autoComplete="new-password"
                value={form.confirm}
                onChange={e => set({ confirm: e.target.value })}
              />
            </label>
          </>
        )}
        <label className="field">
          <span>Rôle</span>
          <select className="dk-select" value={form.role} onChange={e => set({ role: e.target.value })}>
            <option value="user">Utilisateur</option>
            <option value="admin">Administrateur</option>
          </select>
        </label>
        {admin && (
          <p className="dk-help">Un administrateur voit toutes les pages, a tous les droits et gère les comptes.</p>
        )}
        <fieldset className="group" disabled={admin}>
          <legend>Pages ouvertes</legend>
          {PAGES.map(p => (
            <label key={p.key} className="dk-check">
              <input
                type="checkbox"
                checked={admin || form.pages.includes(p.key)}
                onChange={() => set({ pages: togglePage(form.pages, p.key) })}
              />
              {p.label}
            </label>
          ))}
        </fieldset>
        <fieldset className="group" disabled={admin}>
          <legend>Droits</legend>
          <label className="dk-check">
            <input
              type="checkbox"
              checked={admin || form.canDownload}
              onChange={e => set({ canDownload: e.target.checked })}
            />
            Télécharger (extraire, retélécharger)
          </label>
          <label className="dk-check">
            <input
              type="checkbox"
              checked={admin || form.canDelete}
              onChange={e => set({ canDelete: e.target.checked })}
            />
            Supprimer (fichiers, archive, corbeille)
          </label>
        </fieldset>
        <label className="dk-check">
          <input type="checkbox" checked={form.disabled} onChange={e => set({ disabled: e.target.checked })} />
          Compte désactivé (connexion refusée)
        </label>
        {error && (
          <p className="dk-error-text" role="alert">
            {error}
          </p>
        )}
      </DialogContent>
      <DialogActions>
        <button type="button" className="control-btn" onClick={() => close()}>
          Annuler
        </button>
        <button type="submit" className="primary-btn" disabled={busy}>
          {creating ? 'Créer' : 'Enregistrer'}
        </button>
      </DialogActions>
    </form>
  );
}
