import { describe, expect, it } from 'vitest';
import { can, canSeePage, firstAllowedPath, isAdmin } from './permissions';

const session = (fields = {}) => ({ authEnabled: true, authenticated: true, user: 'u', needsSetup: false, ...fields });

describe('permissions', () => {
  it('donne tout à un administrateur, sans authentification ou sans session', () => {
    for (const s of [session({ role: 'admin', pages: [] }), session({ authEnabled: false }), null, session()]) {
      expect(isAdmin(s)).toBe(true);
      expect(canSeePage(s, 'organiser')).toBe(true);
      expect(canSeePage(s, 'administration')).toBe(true);
      expect(can(s, 'download')).toBe(true);
      expect(can(s, 'delete')).toBe(true);
    }
  });

  it('limite un utilisateur à ses pages et à ses droits', () => {
    const s = session({ role: 'user', pages: ['qualite'], canDownload: true, canDelete: false });
    expect(isAdmin(s)).toBe(false);
    expect(canSeePage(s, 'qualite')).toBe(true);
    expect(canSeePage(s, 'musique')).toBe(false);
    expect(canSeePage(s, 'administration')).toBe(false);
    expect(can(s, 'download')).toBe(true);
    expect(can(s, 'delete')).toBe(false);
    expect(can(s, 'autre')).toBe(false);
  });

  it('choisit la première page permise, dans l’ordre de la navigation', () => {
    expect(firstAllowedPath(session({ role: 'user', pages: ['parametres', 'organiser'] }))).toBe('/organiser');
    expect(firstAllowedPath(session({ role: 'user', pages: [] }))).toBe('/aucun-acces');
    expect(firstAllowedPath(session({ role: 'admin', pages: [] }))).toBe('/musique');
  });
});
