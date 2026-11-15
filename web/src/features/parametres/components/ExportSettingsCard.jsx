import { DownloadDirRow } from './DownloadDirRow';
import { FolderOptions } from './FolderOptions';
import { FormatFields } from './FormatFields';
import { YoutubeAccount } from './YoutubeAccount';

/** Carte des réglages d'exportation : format, compte YouTube, rangement, clé AcoustID, dossier de sortie. */
export function ExportSettingsCard({ settings, db, save }) {
  return (
    <div className="dk-card form">
      <FormatFields settings={settings} save={save} />
      <YoutubeAccount settings={settings} save={save} startDir={db?.localDownloadDir} />
      <FolderOptions settings={settings} save={save} />

      <div className="row">
        <span>Clé AcoustID :</span>
        <span className="dk-help">
          Se règle côté serveur (fichier <code>secrets.json</code>, clé <code>Secrets:AcoustIdKey</code>) : elle n'est
          jamais stockée dans la base.
        </span>
      </div>

      <DownloadDirRow settings={settings} db={db} save={save} />
    </div>
  );
}
