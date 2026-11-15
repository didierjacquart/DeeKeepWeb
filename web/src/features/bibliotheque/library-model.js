/**
 * Champs de la recherche : titre, artiste (de la piste ou de l'album) et album ; libellé des
 * propositions. L'ordre sert de priorité quand une même valeur a plusieurs natures.
 */
export const SEARCH_FIELDS = [
  ['artist', 'Artiste'],
  ['albumArtist', 'Artiste'],
  ['album', 'Album'],
  ['title', 'Titre'],
];

export const MIN_SUGGEST_CHARS = 2;
export const MAX_SUGGESTIONS = 8;
export const FILTER_DELAY_MS = 220;

/**
 * Port de <c>library_index.canonical</c> : minuscules sans accent ni ponctuation, « & » → « and »,
 * « $ » → « s », marques de copie et numéro de piste de tête retirés. Les lettres hors a-z0-9
 * disparaissent comme dans l'original (mêmes rapprochements que le serveur).
 */
export function canonical(text) {
  let t = (text ?? '').normalize('NFKD').toLowerCase().replace(/ß/g, 'ss').replace(/\p{M}/gu, '');
  t = t.replace(/&/g, ' and ').replace(/\$/g, 's');
  t = t.replace(/[([]\s*(?:copy|copie|\d+)\s*[)\]]/g, ' ');
  t = t.replace(/^\s*\d{1,3}\s*[-._]\s*/, ' ');
  return t
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function basename(path) {
  return path.split(/[\\/]/).pop() ?? path;
}

/** Valeur d'un champ de recherche ; un titre sans tag prend le nom du fichier (sans extension). */
export function fieldValue(track, field) {
  const raw = String(track[field] ?? '').trim();
  if (raw || field !== 'title') return raw;
  return basename(track.path).replace(/\.[^.]+$/, '');
}

export function toRow(track) {
  const parts = SEARCH_FIELDS.map(([f]) => fieldValue(track, f));
  return { track, hay: canonical(parts.join(' ')), cloud: { ok: 0, stale: 1, none: 2 }[track.archiveState] };
}

/** `_selectable` : présent, ou case « absents » cochée, ou copie d'archive à jour. */
export function selectable(t, includeMissing) {
  return t.onDisk || includeMissing || t.archiveState === 'ok';
}

/** `_build_value_index` : une valeur compte une fois par titre, sous sa nature la plus prioritaire. */
export function buildValueIndex(rows, includeMissing) {
  const counts = new Map();
  for (const { track } of rows) {
    if (!selectable(track, includeMissing)) continue;
    const best = new Map();
    SEARCH_FIELDS.forEach(([field, label], p) => {
      const raw = fieldValue(track, field);
      const key = raw ? canonical(raw) : '';
      if (!key) return;
      const cur = best.get(key);
      if (!cur || p < cur.p) best.set(key, { p, raw, label });
    });
    for (const [key, b] of best) {
      const slot = counts.get(key);
      if (!slot) counts.set(key, { label: b.label, value: b.raw, key, count: 1, priority: b.p });
      else if (b.p < slot.priority)
        Object.assign(slot, { label: b.label, value: b.raw, priority: b.p, count: slot.count + 1 });
      else slot.count++;
    }
  }
  return [...counts.values()].sort(
    (a, b) => b.count - a.count || a.priority - b.priority || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0),
  );
}

/**
 * Propositions pour la frappe, les plus fréquentes d'abord dans chaque rang : valeurs qui
 * commencent par la frappe, puis dont un mot commence par elle (« punk » → « Daft Punk »),
 * puis qui la contiennent.
 */
export function suggestionsFor(values, text) {
  const needle = canonical(text.trim());
  if (!needle) return [];
  const starts = [];
  const words = [];
  const contains = [];
  for (const item of values) {
    if (item.key.startsWith(needle)) starts.push(item);
    else if (item.key.includes(' ' + needle)) words.push(item);
    else if (item.key.includes(needle)) contains.push(item);
    if (starts.length >= MAX_SUGGESTIONS) break;
  }
  return [...starts, ...words, ...contains].slice(0, MAX_SUGGESTIONS);
}

/** Caractères repliés un à un (minuscule, sans accent) : même longueur que le texte, pour s'y repérer. */
export function foldChars(text) {
  return Array.from(text ?? '', c => Array.from(c.normalize('NFD'))[0].toLowerCase());
}

/**
 * Saisie prédictive : la suite de la valeur proposée quand elle commence par la frappe (casse et
 * accents ignorés), sinon null. « daf » + « Daft Punk » → « t Punk ».
 */
export function completion(value, typed) {
  if (!typed || typed !== typed.trimStart()) return null;
  const v = Array.from(value);
  const t = foldChars(typed);
  if (v.length <= t.length) return null;
  const head = foldChars(v.slice(0, t.length).join(''));
  return head.every((c, i) => c === t[i]) ? v.slice(t.length).join('') : null;
}

/**
 * Complétion affichée dans le champ : celle de la proposition active, sinon de la première qui
 * prolonge la frappe.
 * @returns {{ suggestion: object, rest: string } | null}
 */
export function completionFor(suggestions, typed, activeIndex = -1) {
  const candidates = activeIndex >= 0 ? [suggestions[activeIndex]] : suggestions;
  for (const s of candidates) {
    const rest = s && completion(s.value, typed);
    if (rest) return { suggestion: s, rest };
  }
  return null;
}

/** Valeur découpée autour de la frappe (avant, correspondance, après) pour la surligner. */
export function highlightParts(value, typed) {
  const v = Array.from(value);
  const needle = foldChars(typed.trim());
  const folded = foldChars(value);
  if (!needle.length) return [value, '', ''];
  for (let i = 0; i + needle.length <= folded.length; i++) {
    if (needle.every((c, j) => folded[i + j] === c))
      return [v.slice(0, i).join(''), v.slice(i, i + needle.length).join(''), v.slice(i + needle.length).join('')];
  }
  return [value, '', ''];
}

export function suggestionText(s) {
  return `${s.label} · ${s.value}   (${s.count} titre${s.count > 1 ? 's' : ''})`;
}

/** Tri stable : durée et NUAGE numériques, le reste en forme canonique, vides en fin (ascendant). */
export function sortRows(rows, column, desc) {
  const numeric = column === 'duration' || column === 'cloud';
  const keyOf = r => {
    if (column === 'cloud') return r.cloud;
    if (column === 'duration') return r.track.duration ?? 0;
    const value = column === 'path' ? r.track.path : String(r.track[column] ?? '');
    return canonical(value) || '￿';
  };
  const keys = new Map(rows.map(r => [r, keyOf(r)]));
  return [...rows].sort((a, b) => {
    const ka = keys.get(a);
    const kb = keys.get(b);
    const c = numeric ? ka - kb : ka < kb ? -1 : ka > kb ? 1 : 0;
    return desc ? -c : c;
  });
}

/** Lignes retenues : sélectionnables et contenant tous les mots de la recherche, dans n'importe quel ordre. */
export function filterRows(rows, query, includeMissing) {
  const tokens = canonical(query.trim()).split(' ').filter(Boolean);
  return rows.filter(r => selectable(r.track, includeMissing) && tokens.every(t => r.hay.includes(t)));
}

/** Clic sur un en-tête : même colonne = sens inversé, autre colonne = ascendant. */
export function nextSort(sort, column) {
  if (!column) return sort;
  return sort.column === column ? { column, desc: !sort.desc } : { column, desc: false };
}

/** Flèche de l'en-tête trié. */
export function sortMark(sort, column) {
  return column && sort.column === column ? (sort.desc ? ' ▾' : ' ▴') : '';
}
