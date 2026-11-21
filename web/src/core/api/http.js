/**
 * Appels HTTP de l'API JSON de la passerelle (`/api/...`).
 * Une réponse en erreur porte `{ error, code }` ; elle devient une {@link ApiError}.
 */

export class ApiError extends Error {
  /**
   * @param {string} message
   * @param {number} status 0 = serveur injoignable
   * @param {string} code ex. NETWORK, SERVICE_UNAVAILABLE, INVALID_ARGUMENT
   */
  constructor(message, status, code) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }

  /** Échec passager (réseau coupé, service redémarrant) : l'action peut attendre et être rejouée. */
  get transient() {
    return this.status === 0 || this.status === 502 || this.status === 503 || this.status === 504;
  }
}

/** Échec passager, quelle que soit la forme de l'erreur. */
export function isTransient(e) {
  if (e instanceof ApiError) return e.transient;
  return /fetch|network|Failed|Load failed/i.test(e instanceof Error ? e.message : String(e));
}

let onUnauthorized = () => undefined;

/** Réaction à une session expirée (renvoi vers l'écran de connexion), fixée au démarrage. */
export function setUnauthorizedHandler(handler) {
  onUnauthorized = handler;
}

export function notifyUnauthorized() {
  onUnauthorized();
}

let onForbidden = () => undefined;

/**
 * Réaction à un refus du serveur (403 : page ou droit retiré au compte), fixée au
 * démarrage : message à l'utilisateur et relecture de ses droits.
 * @param {(message: string) => void} handler
 */
export function setForbiddenHandler(handler) {
  onForbidden = handler;
}

/**
 * @typedef {object} HttpRequest
 * @property {'GET' | 'POST' | 'PUT' | 'DELETE'} method
 * @property {string} url
 * @property {unknown} [body]
 */

/**
 * Envoie une requête et rend le JSON de la réponse (null si vide).
 * @param {HttpRequest} request
 * @param {Record<string, string>} [headers]
 */
export async function send(request, headers = {}) {
  const hasBody = request.body !== undefined;
  let response;
  try {
    response = await fetch(request.url, {
      method: request.method,
      credentials: 'include',
      headers: { Accept: 'application/json', ...(hasBody ? { 'Content-Type': 'application/json' } : {}), ...headers },
      body: hasBody ? JSON.stringify(request.body) : undefined,
    });
  } catch {
    throw new ApiError('Serveur injoignable', 0, 'NETWORK');
  }
  if (response.status === 401) {
    onUnauthorized();
    throw new ApiError('Session expirée : reconnectez-vous.', 401, 'UNAUTHORIZED');
  }
  const text = await response.text();
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }
  if (!response.ok) {
    const message =
      (data && typeof data === 'object' && data.error) || (typeof data === 'string' && data) || response.statusText;
    const code = (data && typeof data === 'object' && data.code) || `HTTP_${response.status}`;
    if (response.status === 403) onForbidden(message || 'Accès refusé.');
    throw new ApiError(message || `Erreur ${response.status}`, response.status, code);
  }
  return data;
}
