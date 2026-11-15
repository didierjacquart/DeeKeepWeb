import { gql } from '@apollo/client';
import { api } from '../../core/graphql/api';

export const EXPORT = gql`
  query ExportOptions {
    exportOptions {
      current {
        format
        quality
      }
    }
  }
`;

export const SCAN = gql`
  mutation ScanLocalFiles($paths: [String!]!) {
    scanLocalFiles(paths: $paths) {
      id
    }
  }
`;

export const MATCH = gql`
  query BestMatch($artist: String!, $title: String!) {
    bestMatch(artist: $artist, title: $title) {
      id
      title
      artist
      album
      coverSmall
      coverBig
    }
  }
`;

export const CONVERT = gql`
  mutation EnqueueConversions($items: [ConvertItemInput!]!, $batchId: String) {
    enqueueConversions(items: $items, batchId: $batchId) {
      id
    }
  }
`;

/** Format et qualité cibles définis dans les Paramètres (null s'ils manquent). */
export async function loadExportTarget() {
  const d = await api.query(EXPORT);
  return d.exportOptions?.current ?? null;
}

/** Lance l'analyse des fichiers du serveur ; rend l'identifiant de la tâche. */
export async function scanLocalFiles(paths) {
  const r = await api.mutate(SCAN, { paths }, { label: 'Lire les fichiers à réencoder', queueable: false });
  const id = r.data?.scanLocalFiles?.id;
  if (!id) throw new Error(r.error ?? 'Analyse impossible');
  return id;
}

/** Meilleure correspondance Deezer d'un titre (null si rien, ou en cas d'échec). */
export async function findBestMatch(artist, title) {
  try {
    const data = await api.query(MATCH, { artist, title }, true);
    return data.bestMatch ?? null;
  } catch {
    return null;
  }
}

/** Met en file la conversion des fichiers ; rend les tâches créées, dans l'ordre des fichiers. */
export async function enqueueConversions(items, batchId) {
  const r = await api.mutate(
    CONVERT,
    {
      items: items.map(i => ({ localPath: i.path, deezerId: i.match?.id ?? null })),
      batchId,
    },
    { label: `Convertir ${items.length} fichier(s)`, queueable: false },
  );
  const created = r.data?.enqueueConversions;
  if (!created) throw new Error(r.error ?? 'Conversion impossible');
  return created;
}
