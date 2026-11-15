/**
 * `window.matchMedia` pilotable : jsdom ne calcule aucune requête média.
 * Les tests choisissent la largeur avec {@link setMobile}.
 */
let mobile = false;
const lists = new Set();

function matches(query) {
  return query.includes('max-width: 899px') ? mobile : false;
}

export function installMatchMedia() {
  window.matchMedia = query => {
    const listeners = new Set();
    const list = {
      media: query,
      get matches() {
        return matches(query);
      },
      addEventListener: (_type, l) => listeners.add(l),
      removeEventListener: (_type, l) => listeners.delete(l),
      addListener: l => listeners.add(l),
      removeListener: l => listeners.delete(l),
      notify: () => listeners.forEach(l => l({ matches: list.matches, media: query })),
    };
    lists.add(list);
    return list;
  };
}

/** Bascule entre la mise en page mobile (< 900 px) et ordinateur. */
export function setMobile(value) {
  mobile = value;
  lists.forEach(l => l.notify());
}
