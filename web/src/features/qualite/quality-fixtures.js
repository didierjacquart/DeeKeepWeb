/** Données d'essai des tests de l'écran Qualité (lignes et seuils tels que le serveur les rend). */

let uid = 0;

/** Ligne complète ; les champs absents prennent une valeur neutre (titre sain). */
export function makeRow(fields = {}) {
  uid += 1;
  return {
    uid,
    path: `/music/${uid}.flac`,
    title: `Titre ${uid}`,
    artist: 'Artiste',
    album: 'Album',
    format: 'FLAC',
    bitrate: 900,
    duration: 200,
    referenceDuration: 200,
    referenceState: 'found',
    score: 100,
    defects: [],
    cells: { score: '100', defects: '—', status: '' },
    tooltips: {},
    error: null,
    onDisk: true,
    updatedAt: 1,
    status: null,
    statusDetail: null,
    redownloadJobId: null,
    ...fields,
  };
}

export const defect = (id, label = id) => ({ id, label, severity: 1, detail: '' });

export function makeThresholds(fields = {}) {
  return {
    hissDb: -80,
    cracklePct: 0.004,
    clippingPct: 0.2,
    gapDensityPct: 30,
    durationExcess: 5,
    durationGap: 10,
    tailSeconds: 10,
    headSeconds: 3,
    deep: true,
    ...fields,
  };
}
