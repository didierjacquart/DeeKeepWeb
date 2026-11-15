import { describe, expect, it } from 'vitest';
import { asPlayerTrack, cloudText, cloudTip, playTip, playable } from './track-info';

const t = fields => ({ path: '/m/Artiste/01 Titre.flac', onDisk: true, archiveState: 'none', ...fields });

describe('playable / playTip', () => {
  it('écoute le disque, sinon la copie d’archive à jour', () => {
    expect(playable(t())).toBe(true);
    expect(playTip(t())).toBe('Écouter ce titre');
    expect(playable(t({ onDisk: false, archiveState: 'ok' }))).toBe(true);
    expect(playTip(t({ onDisk: false, archiveState: 'ok' }))).toBe(
      "Écouter la copie de l'archive (fichier absent du disque)",
    );
    expect(playable(t({ onDisk: false, archiveState: 'stale' }))).toBe(false);
    expect(playTip(t({ onDisk: false }))).toBe("Fichier absent du disque, et pas de copie d'archive");
  });
});

describe('cloudTip / cloudText', () => {
  it('décrit l’état de l’archive', () => {
    expect(cloudTip(t({ archiveState: 'stale' }))).toBe(
      "Une copie existe dans l'archive, mais le fichier local a changé depuis.",
    );
    expect(cloudTip(t())).toBe("Ce titre n'est pas dans l'archive.");
    expect(cloudTip(t({ archiveState: 'ok' }))).toBe("Copie conservée dans l'archive.");
    expect(cloudText(t({ archiveState: 'ok', archiveBytes: 3_000_000 }))).toBe('✓ 3 Mo');
    expect(cloudText(t({ archiveState: 'stale' }))).toBe('à renvoyer');
    expect(cloudText(t())).toBe('—');
  });
});

describe('asPlayerTrack', () => {
  it('lit le fichier du disque, avec jaquette et contexte', () => {
    const p = asPlayerTrack(
      t({ title: 'Titre', artist: 'A', album: 'B', coverSha1: 'abc', duration: 61, format: 'FLAC', year: 2001 }),
    );
    expect(p).toEqual({
      id: '/m/Artiste/01 Titre.flac',
      title: 'Titre',
      artist: 'A',
      album: 'B',
      src: '/media/track?path=%2Fm%2FArtiste%2F01%20Titre.flac',
      cover: '/covers/abc',
      duration: 61,
      context: 'FLAC · 2001',
    });
  });

  it("lit l'archive quand le fichier manque ; titre tiré du nom de fichier", () => {
    const p = asPlayerTrack(t({ onDisk: false, archiveState: 'ok', title: null }));
    expect(p.src).toBe('/archive-media/track?path=%2Fm%2FArtiste%2F01%20Titre.flac');
    expect(p.title).toBe('01 Titre.flac');
    expect(p.cover).toBeNull();
    expect(p.context).toBeNull();
  });
});
