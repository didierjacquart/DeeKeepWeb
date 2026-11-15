import { notify } from '../../../core/ui/notify';
import { openFsBrowser } from '../../../shared/fs-browser/open-fs-browser';
import { readStr } from '../settings-values';

/** Dossier de sortie (tel que vu par le client, et sur le serveur s'il diffère). */
export function DownloadDirRow({ settings, db, save }) {
  const chooseDownloadDir = async () => {
    const picked = await openFsBrowser({
      start: db?.localDownloadDir ?? undefined,
      mode: 'folder',
      title: "Dossier d'exportation",
    });
    if (!picked?.length) return;
    await save({ download_dir: picked[0] }, 'Changer le dossier de sortie');
    notify.success(`Musique enregistrée dans : ${picked[0]}`);
  };

  return (
    <div className="row">
      <span>
        Dossier de sortie actuel :{' '}
        <strong className="path">{db?.downloadDir ?? readStr(settings, 'download_dir')}</strong>
        {db?.localDownloadDir && db.localDownloadDir !== db.downloadDir && (
          <span className="dk-faint"> (sur le serveur : {db.localDownloadDir})</span>
        )}
      </span>
      <button className="control-btn" onClick={() => void chooseDownloadDir()}>
        Modifier le dossier
      </button>
    </div>
  );
}
