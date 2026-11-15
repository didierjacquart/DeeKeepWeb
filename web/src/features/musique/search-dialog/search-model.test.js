import { describe, expect, it } from 'vitest';
import {
  checkTip,
  crumbFor,
  dedupItems,
  entityKind,
  entityLabel,
  isEntitiesView,
  needsPresenceRetry,
  noResultMessage,
  popularityBars,
  presenceCounts,
  presenceTip,
} from './search-model';

const presence = (state, files = [], reason = null) => ({ key: 'k', state, reason, files });
const track = (id, state, extra = {}) => ({ id, title: `T${id}`, presence: presence(state), ...extra });

describe('pastilles de présence', () => {
  it('infobulle : raison puis cinq fichiers au plus', () => {
    const files = Array.from({ length: 7 }, (_, i) => ({ path: `f${i}.mp3` }));
    const t = { presence: presence('PRESENT', files, 'Même titre') };
    expect(presenceTip(t)).toBe(
      ['Même titre', '• f0.mp3', '• f1.mp3', '• f2.mp3', '• f3.mp3', '• f4.mp3', '… et 2 autre(s)'].join('\n'),
    );
    expect(presenceTip({ presence: presence('ABSENT') })).toBe('');
  });

  it('infobulle de la case : la raison si déjà présent, sinon l’invitation', () => {
    expect(checkTip({ presence: presence('PRESENT', [], 'Déjà là') })).toBe('Déjà là');
    expect(checkTip({ presence: presence('POSSIBLE', [], 'x') })).toBe('Ajouter ce titre à la file de téléchargement');
  });

  it('compteurs ✓ et ≈', () => {
    const list = [track(1, 'PRESENT'), track(2, 'POSSIBLE'), track(3, 'POSSIBLE'), track(4, 'ABSENT')];
    expect(presenceCounts(list)).toEqual({ present: 1, possible: 2 });
  });

  it('relance tant que l’index de présence n’a pas répondu', () => {
    const req = { type: 'browse', kind: 'ALBUM', id: 1 };
    expect(needsPresenceRetry({ tracks: [track(1, 'PENDING')], entities: [] }, req)).toBe(true);
    expect(needsPresenceRetry({ tracks: [track(1, 'ABSENT')], entities: [] }, req)).toBe(false);
    expect(needsPresenceRetry({ error: 'x', tracks: [track(1, 'PENDING')], entities: [] }, req)).toBe(false);
  });
});

describe('libellés et navigation', () => {
  it('jauge de popularité sur 7 barres', () => {
    expect(popularityBars({ rank: 0 }).filter(Boolean)).toHaveLength(0);
    expect(popularityBars({ rank: 500_000 }).filter(Boolean)).toHaveLength(4);
    expect(popularityBars({ rank: 5_000_000 })).toEqual(Array(7).fill(true));
  });

  it("fil d'Ariane et genre d'exploration", () => {
    expect(crumbFor('ARTIST', 'Air')).toBe('Artiste : Air — top 50');
    expect(crumbFor('ALBUM', 'Moon Safari')).toBe('Album : Moon Safari');
    expect(crumbFor('PLAYLIST', 'Chill')).toBe('Playlist : Chill');
    expect(entityKind({ kind: 'ARTIST' })).toBe('ARTIST');
    expect(entityKind({ kind: 'PLAYLIST' })).toBe('PLAYLIST');
    expect(entityKind({ kind: 'OTHER' })).toBe('ALBUM');
    expect(entityLabel({ kind: 'ARTIST' })).toBe('Voir le top 50');
    expect(entityLabel({ kind: 'ALBUM' })).toBe("Ouvrir l'album");
  });

  it('vue des entités : recherche qui en trouve, ou exploration sans pistes', () => {
    const search = { type: 'search', query: 'air', kind: 'ALL' };
    const browse = { type: 'browse', kind: 'ARTIST', id: 1 };
    expect(isEntitiesView({ entities: [{}], tracks: [track(1)] }, search)).toBe(true);
    expect(isEntitiesView({ entities: [{}], tracks: [track(1)] }, browse)).toBe(false);
    expect(isEntitiesView({ entities: [{}], tracks: [] }, browse)).toBe(true);
    expect(isEntitiesView({ entities: [], tracks: [] }, search)).toBe(false);
  });

  it('aucun résultat : message selon le type de recherche', () => {
    expect(noResultMessage({ type: 'search', kind: 'ARTIST' }).text).toBe('Aucun résultat trouvé.');
    expect(noResultMessage({ type: 'search', kind: 'ALL' }).text).toBe('Aucun morceau trouvé pour cette recherche.');
  });

  it('éléments du dédoublonnage', () => {
    const t = {
      id: 9,
      title: 'T',
      artist: 'A',
      album: null,
      presence: presence('PRESENT', [{ path: 'p', reason: 'r', bitrate: 320, fileSize: 1, ext: 'mp3', extra: 1 }]),
    };
    expect(dedupItems([t])).toEqual([
      {
        key: '9',
        title: 'T',
        artist: 'A',
        album: null,
        files: [{ path: 'p', reason: 'r', bitrate: 320, fileSize: 1, ext: 'mp3' }],
      },
    ]);
  });
});
