import { describe, expect, it } from 'vitest';
import { allPaths, defaultChecked, fileDeleteMessage, formatLabel } from './file-dedup';
import { buildTrackDedupGroups, deleteLabel, trackDeleteMessage } from './track-dedup-groups';

describe('buildTrackDedupGroups', () => {
  const items = [
    {
      key: 't1',
      title: 'Song',
      artist: 'Inna',
      album: 'Hot',
      files: [
        { path: 'D:/Music/Divers/song.mp3', bitrate: 128, fileSize: 3e6, reason: 'titre' },
        { path: 'D:/Music/Inna/song.mp3', bitrate: 320, fileSize: 8e6, reason: 'tags' },
      ],
    },
    // Même fichier (casse et séparateurs ignorés) : déjà montré sous la piste précédente.
    { key: 't2', title: 'Other', artist: 'X', files: [{ path: 'd:\\music\\inna\\SONG.mp3' }] },
    { key: 't3', title: 'Solo', files: [{ path: 'D:/x/solo.flac', ext: 'flac', fileSize: 1024 }] },
  ];
  const { groups, checked } = buildTrackDedupGroups(items);

  it('fait un groupe par piste, chaque fichier une seule fois', () => {
    expect(groups.map(g => g.key)).toEqual(['t1', 't3']);
    expect(groups[0]).toMatchObject({ title: 'Song', subtitle: 'Inna — Hot', reason: 'titre · tags' });
  });

  it('décrit chaque fichier et marque le meilleur exemplaire', () => {
    expect(groups[0].files[0]).toEqual({
      path: 'D:/Music/Divers/song.mp3',
      name: 'song.mp3',
      folder: 'D:/Music/Divers',
      quality: 'MP3 · 128 kbps · 2,9 Mo',
      qualityTip: '',
      reason: 'titre',
      keep: false,
    });
    expect(groups[0].files[1]).toMatchObject({
      keep: true,
      qualityTip: 'Meilleur exemplaire du groupe : conservé par défaut',
    });
  });

  it('coche tout sauf le meilleur exemplaire, rien si un seul fichier répond', () => {
    expect([...checked]).toEqual(['D:/Music/Divers/song.mp3']);
    expect(groups[1].files[0].keep).toBe(false);
  });

  it('accepte une liste absente', () => {
    expect(buildTrackDedupGroups(undefined)).toEqual({ groups: [], checked: new Set() });
  });

  it('libellés et compte rendu', () => {
    expect(deleteLabel(0)).toBe('Supprimer les fichiers cochés');
    expect(deleteLabel(3)).toBe('Supprimer les 3 fichier(s) cochés');
    const errors = Array.from({ length: 12 }, (_, i) => `e${i}`);
    const message = trackDeleteMessage({ deleted: ['a'], errors });
    expect(message).toMatch(/^1 fichier\(s\) supprimé\(s\)\.\n\nErreurs :\ne0\n/);
    expect(message).toMatch(/e9\n… et 2 autre\(s\)\.$/);
    expect(trackDeleteMessage({ deleted: [], errors: [] })).toBe('0 fichier(s) supprimé(s).');
  });
});

describe('file-dedup', () => {
  const data = {
    groups: [
      {
        number: 1,
        files: [
          { dbPath: 'a.mp3', keep: true },
          { dbPath: 'b.mp3', keep: false },
        ],
      },
      { number: 2, files: [{ dbPath: 'c.mp3', keep: false }] },
    ],
  };

  it('coche d’office tout sauf l’exemplaire gardé', () => {
    expect([...defaultChecked(data)]).toEqual(['b.mp3', 'c.mp3']);
    expect([...allPaths(data)]).toEqual(['a.mp3', 'b.mp3', 'c.mp3']);
  });

  it('formatLabel et fileDeleteMessage', () => {
    expect(formatLabel({ format: 'MP3', bitrate: 320 })).toBe('MP3 · 320 kbps');
    expect(formatLabel({ format: '' })).toBe('?');
    expect(fileDeleteMessage({ deleted: ['a', 'b'], errors: ['x'] })).toBe('2 fichier(s) supprimé(s).\n\nErreurs :\nx');
  });
});
