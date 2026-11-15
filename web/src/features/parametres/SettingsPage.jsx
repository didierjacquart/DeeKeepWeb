import { useSettings } from './hooks/useSettings';
import { AccountCard } from './components/AccountCard';
import { DatabaseCard } from './components/DatabaseCard';
import { ExportSettingsCard } from './components/ExportSettingsCard';
import './settings-page.scss';

/**
 * Paramètres d'exportation et de rangement, base de données, compte. Chaque changement est
 * enregistré aussitôt (comme l'original) ; hors ligne, il part dans la file d'actions en attente.
 */
export function SettingsPage() {
  const { settings, db, stale, save } = useSettings();

  return (
    <section className="dk-page dk-settings-page">
      <h1 className="dk-page-title">Paramètres d'exportation</h1>

      {stale && (
        <p className="dk-help dk-error-text">
          Hors ligne : réglages servis par le cache local. Vos changements partiront au retour de la connexion.
        </p>
      )}

      <ExportSettingsCard settings={settings} db={db} save={save} />

      <h2 className="sub">Base de données</h2>
      <DatabaseCard db={db} />

      <h2 className="sub">Compte</h2>
      <AccountCard />
    </section>
  );
}
