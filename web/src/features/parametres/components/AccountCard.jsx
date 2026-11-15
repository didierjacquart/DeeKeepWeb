import { useStore } from '../../../lib/store';
import { auth } from '../../../core/auth/auth';
import { PasswordChange } from './PasswordChange';

/** Compte : utilisateur connecté, déconnexion et mot de passe (si l'authentification est active). */
export function AccountCard() {
  const session = useStore(auth, s => s.session);

  const logout = async () => {
    await auth.logout();
    location.assign('/login');
  };

  return (
    <div className="dk-card form">
      {session?.authEnabled ? (
        <>
          <div className="row">
            <span>
              Utilisateur : <strong>{session.user}</strong>
            </span>
            <button className="control-btn" onClick={() => void logout()}>
              Se déconnecter
            </button>
          </div>
          <PasswordChange />
        </>
      ) : (
        <p className="dk-help">L'authentification est désactivée sur ce serveur.</p>
      )}
    </div>
  );
}
