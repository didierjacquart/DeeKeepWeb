import { Link } from 'react-router';
import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { auth } from '../../core/auth/auth';
import { notify } from '../../core/ui/notify';
import { confirm } from '../../shared/dialog/confirm';
import { openPasswordDialog, openUserDialog } from './components/open-user-dialogs';
import { useAdminUsers } from './hooks/useAdminUsers';
import { pagesSummary, rightsSummary, roleLabel } from './user-form';
import './administration-page.scss';

/**
 * Administration (administrateurs seulement) : comptes, rôle, pages ouvertes et
 * droits d'action de chacun. Le serveur applique ces droits à chaque requête et
 * refuse de laisser l'application sans administrateur actif.
 */
export function AdministrationPage() {
  const me = useStore(auth, s => s.session?.user);
  const { users, loading, error, stale, create, update, approve, resetPassword, remove } = useAdminUsers();
  // Comptes à valider en tête, puis les autres dans l'ordre du serveur.
  const pending = users.filter(u => u.pending);
  const sorted = [...pending, ...users.filter(u => !u.pending)];

  const validate = async user => {
    const failure = await approve(user);
    if (failure) notify.error(failure);
    else notify.success(`Compte « ${user.name} » validé.`);
  };

  const add = async () => {
    if (await openUserDialog({ onSubmit: create })) notify.success('Compte créé.');
  };

  const edit = async user => {
    if (await openUserDialog({ user, onSubmit: body => update(user.id, body) })) notify.success('Compte enregistré.');
  };

  const changePassword = async user => {
    if (await openPasswordDialog({ name: user.name, onSubmit: pwd => resetPassword(user.id, pwd) }))
      notify.success('Mot de passe changé.');
  };

  const del = async user => {
    const ok = await confirm({
      title: 'Supprimer le compte',
      message: `Supprimer le compte « ${user.name} » ?\nSes sessions ouvertes sont aussitôt fermées.`,
      confirm: 'Supprimer',
      danger: true,
    });
    if (!ok) return;
    const failure = await remove(user.id);
    if (failure) notify.error(failure);
    else notify.success('Compte supprimé.');
  };

  return (
    <section className="dk-page dk-administration-page">
      <h1 className="dk-page-title">Administration</h1>

      <div className="dk-actions">
        <span className="dk-faint">{users.length} compte(s)</span>
        <span className="dk-spacer"></span>
        <button className="primary-btn" onClick={() => void add()}>
          Nouveau compte
        </button>
      </div>

      {stale && (
        <p className="dk-help dk-error-text">Hors ligne : liste servie par le cache local, peut être périmée.</p>
      )}
      {error && !stale && <p className="dk-error-text">{error}</p>}
      {pending.length > 0 && (
        <p className="dk-help pending-note" role="status">
          {pending.length === 1
            ? '1 compte attend votre validation'
            : `${pending.length} comptes attendent votre validation`}{' '}
          : vérifiez l'adresse, ajustez au besoin ses pages et droits (Modifier), puis Valider.
        </p>
      )}

      <div className="dk-table" role="table" aria-label="Comptes">
        <div className="dk-thead row" role="row">
          <span role="columnheader">E-mail</span>
          <span role="columnheader">Rôle</span>
          <span role="columnheader" className="dk-hide-mobile">
            Pages
          </span>
          <span role="columnheader" className="dk-hide-mobile">
            Droits
          </span>
          <span role="columnheader">État</span>
          <span role="columnheader" aria-label="Actions"></span>
        </div>
        {sorted.map(u => {
          const self = !!me && u.name.toLowerCase() === me.toLowerCase();
          return (
            <div key={u.id} className={cx('dk-row row', u.disabled && !u.pending && 'dim', u.pending && 'pending')} role="row">
              <span role="cell" className="name">
                {u.name}
                {self && <span className="dk-chip info">vous</span>}
              </span>
              <span role="cell">{roleLabel(u.role)}</span>
              <span role="cell" className="dk-hide-mobile" data-tip={pagesSummary(u)}>
                {pagesSummary(u)}
              </span>
              <span role="cell" className="dk-hide-mobile">
                {rightsSummary(u)}
              </span>
              <span role="cell">
                {u.pending ? (
                  <span className="dk-chip warn">À valider</span>
                ) : (
                  <span className={cx('dk-chip', u.disabled ? 'err' : 'ok')}>{u.disabled ? 'Désactivé' : 'Actif'}</span>
                )}
              </span>
              <span role="cell" className="acts">
                {u.pending && (
                  <button className="primary-btn small" onClick={() => void validate(u)} aria-label={`Valider ${u.name}`}>
                    Valider
                  </button>
                )}
                <Link className="control-btn small" to={`/bibliotheque?utilisateur=${encodeURIComponent(u.id)}`}>
                  Ses titres
                </Link>
                <button className="control-btn small" onClick={() => void edit(u)} aria-label={`Modifier ${u.name}`}>
                  Modifier
                </button>
                <button
                  className="control-btn small"
                  onClick={() => void changePassword(u)}
                  aria-label={`Mot de passe de ${u.name}`}
                >
                  Mot de passe
                </button>
                <button
                  className="icon-btn"
                  onClick={() => void del(u)}
                  disabled={self}
                  aria-label={`Supprimer ${u.name}`}
                  data-tip={self ? 'Impossible de supprimer son propre compte' : 'Supprimer ce compte'}
                >
                  ✕
                </button>
              </span>
            </div>
          );
        })}
        {!users.length && <div className="dk-empty">{loading ? 'Chargement…' : 'Aucun compte.'}</div>}
      </div>
    </section>
  );
}
