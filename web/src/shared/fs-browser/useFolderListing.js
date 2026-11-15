import { useEffect, useState } from 'react';
import { browseFolder } from './fs-browser-api';

/**
 * Dossier affiché par le navigateur : `go(chemin)` en charge un autre ; une
 * réponse arrivée après un changement de dossier est ignorée. Un échec garde
 * le dossier précédent à l'écran (l'erreur s'affiche au-dessus).
 */
export function useFolderListing(start, audioOnly) {
  // Le compteur relance la requête même quand on redemande le même chemin (après une erreur).
  const [request, setRequest] = useState({ path: start ?? null, n: 0 });
  const [result, setResult] = useState({ listing: null, error: null });

  useEffect(() => {
    let cancelled = false;
    browseFolder(request.path, audioOnly).then(
      listing => !cancelled && setResult({ listing, error: null }),
      e => !cancelled && setResult(r => ({ ...r, error: e instanceof Error ? e.message : String(e) })),
    );
    return () => {
      cancelled = true;
    };
  }, [request, audioOnly]);

  return { ...result, go: path => setRequest(r => ({ path, n: r.n + 1 })) };
}
