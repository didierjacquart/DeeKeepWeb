import { useStore } from '../lib/store';
import { auth } from '../core/auth/auth';

/** Compte sans aucune page ouverte : explication et déconnexion. */
export function NoAccess() {
  const user = useStore(auth, s => s.session?.user);

  const logout = async () => {
    await auth.logout();
    location.assign('/login');
  };

  return (
    <div className="dk-page">
      <h1 className="dk-page-title">Accès restreint</h1>
      <div className="dk-card">
        <p>
          Aucune page n'est ouverte au compte <strong>{user}</strong>. Demandez l'accès à un administrateur.
        </p>
        <button className="control-btn" onClick={() => void logout()}>
          Se déconnecter
        </button>
      </div>
    </div>
  );
}
