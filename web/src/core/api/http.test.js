import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, send, setForbiddenHandler } from './http';

afterEach(() => {
  vi.unstubAllGlobals();
  setForbiddenHandler(() => undefined);
});

describe('send', () => {
  it('signale un refus (403) avec le message du serveur', async () => {
    const forbidden = vi.fn();
    setForbiddenHandler(forbidden);
    const body = { error: "Accès refusé : vous n'avez pas le droit de supprimer.", code: 'FORBIDDEN' };
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(JSON.stringify(body), { status: 403 }))),
    );
    const error = await send({ method: 'POST', url: '/api/library/delete-files', body: {} }).catch(e => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('FORBIDDEN');
    expect(error.transient).toBe(false);
    expect(forbidden).toHaveBeenCalledWith(body.error);
  });

  it('ne signale rien pour une autre erreur', async () => {
    const forbidden = vi.fn();
    setForbiddenHandler(forbidden);
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(new Response(JSON.stringify({ error: 'x', code: 'INVALID_ARGUMENT' }), { status: 400 })),
      ),
    );
    await expect(send({ method: 'GET', url: '/api/x' })).rejects.toThrow('x');
    expect(forbidden).not.toHaveBeenCalled();
  });
});
