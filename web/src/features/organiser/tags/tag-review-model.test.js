import { describe, expect, it } from 'vitest';
import {
  buildRows,
  currentTags,
  fieldLabel,
  fillsText,
  foundText,
  foundTip,
  isSure,
  matchText,
  reviewStateText,
  sourceText,
  tagsOf,
  writeItems,
  writeQuestion,
} from './tag-review-model';
import { emptyReview, reviewReducer } from './tag-review-state';

const files = [
  { dbPath: 'm/a.mp3', path: 'D:/m/a.mp3', title: 'a', titleTagged: false, artist: '', album: '', coreMissing: true },
  { dbPath: 'm/b.mp3', path: 'D:/m/b.mp3', title: 'B', titleTagged: true, artist: 'X', album: '', coreMissing: false },
  { dbPath: 'm/c.mp3', path: 'D:/m/c.mp3', title: 'c', titleTagged: false, artist: '', album: '' },
];
const proposal = {
  proposed: { title: 'Alpha', artist: 'Inna', album: 'Hot', genre: 'Pop' },
  missing: ['title', 'genre', 'date'],
  contradicted: ['artist'],
  score: 0.95,
  confidence: 'sure',
  source: 'Deezer',
  note: 'isrc',
};
const results = new Map([
  ['m/a.mp3', { status: 'found', proposal }],
  ['D:/m/b.mp3', { status: 'none', proposal: null }],
]);
const rows = buildRows(files, results);

describe('buildRows', () => {
  it('rend une ligne par fichier : trouvée, sans réponse ou en attente', () => {
    expect(rows.map(r => r.state)).toEqual(['found', 'none', 'pending']);
    expect(rows[0]).toMatchObject({
      sure: true,
      fills: [
        ['title', 'Alpha'],
        ['genre', 'Pop'],
      ],
      corrects: [['artist', 'Inna']],
    });
  });
});

describe('textes des colonnes', () => {
  it('isSure suit la confiance, sinon le score', () => {
    expect(isSure({ confidence: 'High', score: 0 })).toBe(true);
    expect(isSure({ confidence: 'probable', score: 0.99 })).toBe(false);
    expect(isSure({ score: 0.8 })).toBe(true);
    expect(isSure({ score: 0.5 })).toBe(false);
  });

  it('tags actuels, trouvés et infobulle', () => {
    expect(currentTags(files[0])).toBe('(aucun)');
    expect(currentTags(files[1])).toBe('B · X');
    expect(foundText(rows[0])).toBe('Alpha · Inna · Hot');
    expect(foundText(rows[1])).toBe('aucune correspondance trouvée');
    expect(foundText(rows[2])).toBe('en attente…');
    expect(foundTip(rows[0])).toBe('Titre : Alpha\nArtiste : Inna\nAlbum : Hot\nSource : Deezer\nisrc');
    expect(foundTip(rows[1])).toBe('');
  });

  it('correspondance, champs comblés et source', () => {
    expect(matchText(rows[0])).toBe('sûre (0.95) — isrc');
    expect(matchText(rows[1])).toBe('le catalogue ne reconnaît pas cet enregistrement');
    expect(matchText(rows[2])).toBe('');
    expect(fillsText(rows[0], false)).toBe('titre · genre = Pop   (⟳ artiste ignoré)');
    expect(fillsText(rows[0], true)).toBe('titre · genre = Pop   ⟳ artiste');
    expect(fillsText(rows[1], false)).toBe('—');
    expect(sourceText(rows[0], false)).toBe('Deezer');
    expect(sourceText({ state: 'none', file: files[0] }, true)).toBe('Deezer + MusicBrainz + AcoustID');
    expect(sourceText(rows[1], true)).toBe('Deezer');
    expect(fieldLabel('albumartist')).toBe('artiste album');
    expect(fieldLabel('bpm')).toBe('bpm');
  });

  it('ligne d’état pendant et après la recherche', () => {
    const base = { rows, examined: 2, total: 3 };
    expect(reviewStateText({ ...base, running: true, stopping: false })).toBe('Recherche en cours… 2 / 3 fichiers');
    expect(reviewStateText({ ...base, running: true, stopping: true })).toBe(
      'Arrêt de la recherche… 2 / 3 fichiers examinés',
    );
    expect(reviewStateText({ ...base, running: false })).toBe(
      '1 correspondance(s) sûre(s) · 0 probable(s) · 1 sans réponse · 1 non examiné(s) — recherche arrêtée',
    );
  });
});

describe('écriture', () => {
  it('tagsOf et writeItems n’ajoutent les champs contredits que sur demande', () => {
    expect(tagsOf(rows[0], false)).toEqual([
      { field: 'title', value: 'Alpha' },
      { field: 'genre', value: 'Pop' },
    ]);
    expect(writeItems([rows[0]], true)).toEqual([
      {
        path: 'm/a.mp3',
        fields: [
          { field: 'title', value: 'Alpha' },
          { field: 'genre', value: 'Pop' },
          { field: 'artist', value: 'Inna' },
        ],
        overwriteExisting: true,
      },
    ]);
  });

  it('writeQuestion annonce les fichiers non interrogés et les remplacements', () => {
    expect(writeQuestion([rows[0]], false, 0)).toBe(
      "Écrire 2 tag(s) dans 1 fichier(s) ?\nSeuls des champs vides sont renseignés ; aucun tag existant n'est remplacé.",
    );
    const question = writeQuestion([rows[0]], true, 4);
    expect(question).toContain("4 fichier(s) n'ont pas été interrogés");
    expect(question).toContain('Dont 1 tag(s) existant(s) remplacé(s)');
  });
});

describe('reviewReducer', () => {
  it('range les propositions et coche d’office les sûres', () => {
    let s = emptyReview();
    s = reviewReducer(s, { type: 'proposal', key: 'a', status: 'found', proposal });
    s = reviewReducer(s, {
      type: 'proposal',
      key: 'b',
      status: 'found',
      proposal: { ...proposal, confidence: 'probable' },
    });
    s = reviewReducer(s, { type: 'proposal', key: 'c', status: 'none', proposal: { proposed: null } });
    expect([...s.results.keys()]).toEqual(['a', 'b', 'c']);
    expect(s.results.get('c')).toEqual({ status: 'none', proposal: null });
    expect([...s.checked]).toEqual(['a']);
  });

  it('coche, décoche et remplace les cases', () => {
    let s = emptyReview();
    s = reviewReducer(s, { type: 'toggle', key: 'a' });
    expect([...s.checked]).toEqual(['a']);
    s = reviewReducer(s, { type: 'check', keys: ['b', 'c'] });
    expect([...s.checked]).toEqual(['b', 'c']);
    expect(reviewReducer(s, { type: 'inconnu' })).toBe(s);
  });
});
