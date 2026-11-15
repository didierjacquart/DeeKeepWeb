import { describe, expect, it } from 'vitest';
import { albumNamedTracks, albumTracks, artistTracks, groupOfSuggestion } from './listen-groups';

const t = (path, fields = {}) => ({ path, onDisk: true, archiveState: 'none', ...fields });

const library = [
  t('M/Daft Punk/Discovery/10 - Veridis Quo.mp3', { artist: 'Daft Punk', album: 'Discovery', year: 2001 }),
  t('M/Daft Punk/Discovery/2 - Aerodynamic.mp3', { artist: 'Daft Punk', album: 'Discovery', year: 2001 }),
  t('M/Daft Punk/Discovery/1 - One More Time.mp3', { artist: 'Daft Punk', album: 'Discovery', year: 2001 }),
  t('M/Daft Punk/Homework/01 - Revolution 909.mp3', { artist: 'DAFT PUNK', album: 'Homework', year: 1997 }),
  t('M/Daft Punk/Homework/02 - Da Funk.mp3', { artist: 'Daft Punk', album: 'Homework', year: 1997, onDisk: false }),
  t('M/Queen/Greatest Hits/01 - Bohemian Rhapsody.mp3', { artist: 'Queen', album: 'Greatest Hits' }),
  t('M/ABBA/Greatest Hits/01 - SOS.mp3', { artist: 'ABBA', album: 'Greatest Hits' }),
  t('M/Divers/Now 5/01 - a.mp3', { artist: 'Moby', album: 'Now 5' }),
  t('M/Divers/Now 5/02 - b.mp3', { artist: 'Air', album: 'Now 5' }),
  t('M/Mix/03 - feat.mp3', { artist: 'Guest', albumArtist: 'Daft Punk', album: 'Mix', year: 2005 }),
];

const paths = list => list.map(x => x.path.split('/').pop());

describe('écoute d’un album ou d’un artiste', () => {
  it('lit un album dans l’ordre des pistes (numéros naturels), titres absents sautés', () => {
    expect(paths(albumTracks(library, library[0]))).toEqual([
      '1 - One More Time.mp3',
      '2 - Aerodynamic.mp3',
      '10 - Veridis Quo.mp3',
    ]);
    expect(paths(albumTracks(library, library[3]))).toEqual(['01 - Revolution 909.mp3']);
  });

  it('ne mélange pas deux albums homonymes d’artistes différents, garde une compilation entière', () => {
    expect(paths(albumTracks(library, library[5]))).toEqual(['01 - Bohemian Rhapsody.mp3']);
    expect(paths(albumTracks(library, library[7]))).toEqual(['01 - a.mp3', '02 - b.mp3']);
    expect(albumTracks(library, t('x.mp3', { artist: 'Queen' }))).toEqual([]);
  });

  it('lit un artiste par année puis album, artiste d’album compris, sans tenir compte de la casse', () => {
    expect(paths(artistTracks(library, 'daft punk'))).toEqual([
      '01 - Revolution 909.mp3',
      '1 - One More Time.mp3',
      '2 - Aerodynamic.mp3',
      '10 - Veridis Quo.mp3',
      '03 - feat.mp3',
    ]);
  });

  it('écoute une copie d’archive à jour', () => {
    const archived = t('M/Z/z.mp3', { artist: 'Z', album: 'Z', onDisk: false, archiveState: 'ok' });
    expect(artistTracks([archived], 'Z')).toEqual([archived]);
  });

  it('reconnaît les propositions d’artiste et d’album de la recherche', () => {
    expect(groupOfSuggestion(library, { label: 'Artiste', value: 'Queen' })).toMatchObject({
      kind: 'artist',
      name: 'Queen',
      tracks: [library[5]],
    });
    expect(groupOfSuggestion(library, { label: 'Titre', value: 'SOS' })).toBeNull();
    const album = groupOfSuggestion(library, { label: 'Album', value: 'greatest hits' });
    expect(paths(album.tracks)).toEqual(['01 - SOS.mp3', '01 - Bohemian Rhapsody.mp3']);
    expect(albumNamedTracks(library, '')).toEqual([]);
    expect(groupOfSuggestion(library, { label: 'Genre', value: 'Rock' })).toBeNull();
    expect(groupOfSuggestion(library, { label: 'Artiste', value: 'Inconnu' })).toBeNull();
    expect(groupOfSuggestion(library, null)).toBeNull();
  });
});
