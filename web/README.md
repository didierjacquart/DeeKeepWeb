# Client web DeeKeep

Application React 19 en JavaScript (`.jsx` / `.js`, types documentés en JSDoc),
construite avec Vite, React Router et Apollo Client, installable en PWA.
L'organisation du code, le socle, les règles d'écriture et de test sont décrits
dans `docs/CONVENTIONS.md` (section « Client React »).

## Développement

```bash
npm start
```

Serveur de développement sur `http://localhost:4200/`, avec un proxy vers la
passerelle (`http://127.0.0.1:5000`) pour `/graphql` (WebSocket compris),
`/auth`, `/media`, `/archive-media`, `/covers`, `/img` et `/preview`. Le script
`run-dev.ps1` à la racine lance les services, la passerelle et ce serveur.

## Qualité

| Commande | Rôle |
|---|---|
| `npm test` | Tests Vitest + React Testing Library (`npm run test:watch` pour les relancer à chaque modification). |
| `npm run lint` | ESLint (règles React, hooks du compilateur React 19, accessibilité). |
| `npm run format` | Prettier sur le code et les styles (`format:check` pour vérifier sans écrire). |
| `npm run check` | Lint, format, tests puis build : à passer avant chaque commit. |

## Build

```bash
npm run build
```

Produit `dist/` : coquille, scripts découpés par page et service worker
(`sw.js`, Workbox) qui garde l'application utilisable hors ligne.
`deploy/publish.ps1` copie `dist/` dans le `wwwroot` de la passerelle.
