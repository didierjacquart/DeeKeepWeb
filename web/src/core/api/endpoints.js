/**
 * Déclaration des appels serveur. Une opération a un **nom** (cache hors ligne,
 * rafraîchissement après une action, tests) et rend un objet `{ champ: résultat }` :
 *
 *   const HISTORY = endpoint('History', 'history', 'GET /api/library/history');
 *   const data = await api.query(HISTORY);        // → { history: [...] }
 *
 * Les variables remplissent les `{marques}` du chemin ; le reste part en paramètres
 * d'URL (GET, DELETE) ou en corps JSON (POST, PUT). `body` et `params` permettent
 * de construire autrement le corps ou les paramètres ; `result` de remodeler la réponse.
 */

/**
 * @typedef {object} EndpointOptions
 * @property {(vars: Record<string, any>) => unknown} [body] Corps JSON construit à partir des variables.
 * @property {(vars: Record<string, any>) => Record<string, unknown>} [params] Paramètres d'URL construits à partir des variables.
 * @property {(data: any) => any} [result] Transformation de la réponse.
 */

/**
 * @typedef {object} RequestPart
 * @property {string} field
 * @property {'GET' | 'POST' | 'PUT' | 'DELETE'} method
 * @property {string} path
 * @property {EndpointOptions} options
 */

/**
 * @typedef {object} Operation
 * @property {string} name
 * @property {'request'} kind
 * @property {RequestPart[]} parts
 */

/**
 * @typedef {object} EventOperation
 * @property {string} name
 * @property {'event'} kind
 * @property {string} field
 * @property {string} type Type d'événement des services (« job.updated »…).
 * @property {(data: any, vars: Record<string, any>) => boolean} [filter]
 */

/**
 * Un appel HTTP.
 * @param {string} name
 * @param {string} field Champ du résultat.
 * @param {string} route « MÉTHODE /chemin » (ex. `POST /api/library/tracks/query`).
 * @param {EndpointOptions} [options]
 * @returns {Operation}
 */
export function endpoint(name, field, route, options = {}) {
  const [method, path] = route.trim().split(/\s+/, 2);
  return { name, kind: 'request', parts: [{ field, method: /** @type {any} */ (method), path, options }] };
}

/**
 * Plusieurs appels lancés ensemble, résultats réunis dans un même objet.
 * @param {string} name
 * @param {...Operation} operations
 * @returns {Operation}
 */
export function combine(name, ...operations) {
  return { name, kind: 'request', parts: operations.flatMap(o => o.parts) };
}

/**
 * Événement poussé par le serveur (flux `/api/events`).
 * @param {string} name
 * @param {string} field Champ sous lequel la charge utile est rendue.
 * @param {string} type
 * @param {(data: any, vars: Record<string, any>) => boolean} [filter]
 * @returns {EventOperation}
 */
export function serverEvent(name, field, type, filter) {
  return { name, kind: 'event', field, type, filter };
}

function queryString(params) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null) continue;
    if (Array.isArray(value)) for (const v of value) search.append(key, String(v));
    else search.append(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

/**
 * Requête HTTP d'une partie d'opération.
 * @param {RequestPart} part
 * @param {Record<string, any>} [variables]
 * @returns {import('./http').HttpRequest}
 */
export function buildRequest(part, variables = {}) {
  const vars = variables ?? {};
  const used = new Set();
  const [rawPath, fixedQuery = ''] = part.path.split('?');
  const path = rawPath.replace(/\{(\w+)\}/g, (_, key) => {
    used.add(key);
    return encodeURIComponent(String(vars[key] ?? ''));
  });
  const rest = Object.fromEntries(Object.entries(vars).filter(([k, v]) => !used.has(k) && v !== undefined));
  const readOnly = part.method === 'GET' || part.method === 'DELETE';
  const params = part.options.params ? part.options.params(vars) : readOnly ? rest : {};
  let url = path + queryString(params);
  if (fixedQuery) url += (url.includes('?') ? '&' : '?') + fixedQuery;
  if (readOnly) return { method: part.method, url };
  const body = part.options.body ? part.options.body(vars) : rest;
  return { method: part.method, url, body: body ?? {} };
}
