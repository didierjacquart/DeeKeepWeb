import { describe, expect, it } from 'vitest';
import { linkLabel, linkTip, serviceLabel, toolText, toolsOf, uptime } from './diagnostic-labels';

describe('serviceLabel', () => {
  it('nomme les services connus', () => {
    expect(serviceLabel('catalog')).toBe('Catalogue (Deezer, MusicBrainz, AcoustID)');
    expect(serviceLabel('storage')).toBe('Archive');
  });

  it("rend l'identifiant d'un service inconnu", () => {
    expect(serviceLabel('gateway')).toBe('gateway');
  });
});

describe('uptime', () => {
  const now = 1_000_000 * 1000;

  it('compte en minutes, heures puis jours', () => {
    expect(uptime(1_000_000 - 7 * 60 - 30, now)).toBe('7 min');
    expect(uptime(1_000_000 - (3 * 3600 + 12 * 60), now)).toBe('3 h 12 min');
    expect(uptime(1_000_000 - (2 * 86400 + 3 * 3600 + 59 * 60), now)).toBe('2 j 3 h');
  });

  it('ne rend jamais une durée négative', () => {
    expect(uptime(1_000_000 + 600, now)).toBe('0 min');
  });
});

describe('linkLabel', () => {
  it("traduit l'état de la liaison", () => {
    expect(linkLabel('online')).toBe('Connecté');
    expect(linkLabel('reconnecting')).toBe('Reconnexion…');
    expect(linkLabel('offline')).toBe('Hors ligne');
  });
});

describe('linkTip', () => {
  it("montre la dernière erreur d'abord", () => {
    expect(linkTip({ lastError: 'refusé', since: 10 })).toBe('refusé');
  });

  it('montre depuis quand le lien est établi', () => {
    expect(linkTip({ lastError: null, since: Math.floor(Date.now() / 1000) })).toBe("Connecté à l'instant");
  });

  it('ne montre rien sans information', () => {
    expect(linkTip({ lastError: null, since: null })).toBeUndefined();
  });
});

describe('toolsOf', () => {
  it('rassemble les outils de chaque service', () => {
    const services = [
      { service: 'downloader', info: { tools: [{ name: 'yt-dlp' }, { name: 'ffmpeg' }] } },
      { service: 'catalog', info: null },
      { service: 'quality', info: { tools: [{ name: 'ffprobe' }] } },
    ];
    expect(toolsOf(services)).toEqual([
      { name: 'yt-dlp', service: 'downloader' },
      { name: 'ffmpeg', service: 'downloader' },
      { name: 'ffprobe', service: 'quality' },
    ]);
  });
});

describe('toolText', () => {
  it('rend la version et le détail', () => {
    expect(toolText({ available: true, version: '2025.10.1', detail: null })).toBe('2025.10.1');
    expect(toolText({ available: true, version: '7.1', detail: 'build gpl' })).toBe('7.1 · build gpl');
    expect(toolText({ available: true, version: null, detail: 'intégré' })).toBe('intégré');
  });

  it('signale un outil absent sans explication', () => {
    expect(toolText({ available: false, version: null, detail: null })).toBe('Non détecté');
    expect(toolText({ available: false, version: null, detail: 'introuvable dans le PATH' })).toBe(
      'introuvable dans le PATH',
    );
    expect(toolText({ available: true, version: null, detail: null })).toBe('');
  });
});
