# Conventions de développement

À lire avant d'écrire du code. Complète `docs/ARCHITECTURE.md`. Les
spécifications fonctionnelles de l'original sont dans `docs/analyse/` : elles
font foi pour le comportement (constantes, seuils, regex, libellés).

## Outils

- .NET : `export PATH="/c/Program Files/dotnet:$PATH" DOTNET_NOLOGO=1` (bash).
- Node : `export PATH="/c/Program Files/nodejs:$PATH"`.
- Build : `dotnet build DeeKeep.slnx` ; tests : `dotnet test --project tests/DeeKeep.Tests`.
- Configuration de développement : `DEEKEEP_CONFIG=D:/000_Logiciel/DeeKeepWeb/config/dev/appsettings.json`
  et `ASPNETCORE_ENVIRONMENT=Development`. Elle pointe sur **une copie** de la
  base (`.data/deekeep_cache.db`). Ne jamais ouvrir en écriture la base réelle
  `%APPDATA%\DeeKeep\deekeep_cache.db` ni rien modifier sous `D:\000_Logiciel\DeezerMusic`.
- Ports : gateway 5000, catalog 5101, library 5102, downloader 5103, quality 5104, storage 5105.

## Frontières (travail en parallèle)

Chaque agent n'écrit que dans **ses** dossiers :

| Domaine | Service | Contrats | API de la passerelle | Tests |
|---|---|---|---|---|
| Catalogue | `src/Services/DeeKeep.Catalog/` | `DeeKeep.Contracts/Catalog/` | `Gateway/Api/CatalogApi.cs` | `tests/DeeKeep.Tests/Catalog/` |
| Bibliothèque | `src/Services/DeeKeep.Library/` sauf `Organize/` | `DeeKeep.Contracts/Library/` | `Gateway/Api/LibraryApi.cs` | `tests/DeeKeep.Tests/Library/` |
| Organiser, doublons, tags | `src/Services/DeeKeep.Library/Organize/` | `DeeKeep.Contracts/Organize/` | `Gateway/Api/OrganizeApi.cs` | `tests/DeeKeep.Tests/Organize/` |
| Téléchargement | `src/Services/DeeKeep.Downloader/` | `DeeKeep.Contracts/Downloader/` | `Gateway/Api/DownloaderApi.cs` | `tests/DeeKeep.Tests/Downloader/` |
| Qualité | `src/Services/DeeKeep.Quality/` | `DeeKeep.Contracts/Quality/` | `Gateway/Api/QualityApi.cs` | `tests/DeeKeep.Tests/Quality/` |
| Archive | `src/Services/DeeKeep.Storage/` | `DeeKeep.Contracts/Storage/` | `Gateway/Api/StorageApi.cs` | `tests/DeeKeep.Tests/Storage/` |

Les fichiers de contrats existants (`*Contracts.cs`) sont **partagés** : on
peut **ajouter** des membres (champs en fin de record avec valeur par défaut,
nouvelles méthodes, nouveaux types) mais pas renommer ni supprimer ce qui
existe — d'autres agents s'en servent déjà. Un besoin de modification d'un
contrat d'un autre domaine se signale dans le compte rendu final, il ne se fait
pas soi-même.

`DeeKeep.Common` (sauf ajout de fichiers neufs dans un sous-dossier à son nom,
ex. `Common/Downloader/` — à éviter) et `DeeKeep.Gateway/Infrastructure`,
`Program.cs` sont **gelés** : signaler un besoin au lieu de les modifier.

Si `dotnet build DeeKeep.slnx` échoue à cause du travail en cours d'un autre
agent, construire seulement ses propres projets
(`dotnet build src/Services/DeeKeep.X`) et le signaler.

## Service

- `Program.cs` appelle `AddXService(configuration)` / `MapXService()` définis
  dans `XService.cs` : y enregistrer ses classes, gestionnaires de tâches
  (`services.AddJobHandler<THandler>(type)`), abonnements
  (`services.AddEventSubscription(producer, handler, durable: true, types...)`),
  et ses points d'API minimaux sous **`/api/...`** (protégés par la clé
  interne). Flux binaires publics (via le proxy de la passerelle) : `/media`,
  `/covers` (Library), `/img`, `/preview` (Catalog), `/archive-media` (Storage).
- Les points d'API correspondent **exactement** aux méthodes du client typé
  du domaine (`DeeKeep.Contracts/<Domaine>/*Contracts.cs`).
- Sérialisation JSON : `JsonDefaults.Options` (camelCase, énumérations en texte).
- Journalisation sobre : `ILogger`, pas de `Console.WriteLine`.

## Données

- **Lire** n'importe quelle table de la base existante est permis
  (`DeeKeepDb.Open(readOnly: true)` + Dapper). **Écrire** seulement dans les
  tables dont le service est propriétaire (voir ARCHITECTURE.md ; `searches`
  appartient à Catalog), toujours par `DeeKeepDb.UpsertMany` (ligne
  **complète** : lire-modifier-écrire) ou `MarkDeleted` (pierre tombale) —
  jamais d'UPDATE partiel, jamais de DELETE physique, jamais de modification du
  DDL ou de `schema_meta`. L'empreinte `h` est recalculée par `UpsertMany`.
- Données propres à la version web : nouvelles tables dans `deekeep_web.db`
  (`WebDb.Open()`), préfixées par le nom du service (`catalog_cache`,
  `library_reservations`…), créées par le service au démarrage
  (`CREATE TABLE IF NOT EXISTS`).
- Chemins : la base stocke la forme d'origine (`D:\Artiste\Titre.mp3`). Toute
  lecture/écriture disque passe par `PathMap.ToLocal(dbPath)` et tout chemin
  enregistré par `PathMap.ToDb(localPath)`. Les comparaisons de chemins se
  font sans tenir compte de la casse (comme l'original sous Windows) via la
  clé canonique de chemin de `DeeKeep.Common.Text`.
- Paramètres : `app_settings` (JSON). Lecture directe autorisée
  (`DeeKeepDb.AllSettings()`), écriture via `LibraryClient.SetSettingsAsync`.
  Les clés et défauts sont listés dans `docs/analyse/05-donnees-synchronisation.md`.
  Les secrets (clé AcoustID…) viennent de la configuration (`Secrets:AcoustIdKey`),
  jamais de la base.

## Asynchronisme et résilience

- Toute opération de plus d'une seconde environ est une **tâche**
  (`JobStore.Enqueue` + `IJobHandler`), jamais un appel HTTP qui attend.
  Progression par `JobContext.Report`, résultat élément par élément par
  `JobContext.Item` (événement `job.item`, données = DTO du contrat).
  Écrire les résultats au fil de l'eau en base : un arrêt ne perd rien.
- Annulation : respecter le `CancellationToken` partout ; les outils externes
  passent par `ProcessRunner` (tue l'arbre de processus).
- Erreur passagère (réseau, 5xx, 429, verrou) → `TransientJobException`
  (remise en file avec attente) ; erreur définitive → `PermanentJobException`.
- Tâches idempotentes : une tâche reprise après redémarrage doit pouvoir
  recommencer sans dégât (vérifier ce qui est déjà fait).
- Effets de bord inter-services qui doivent aboutir : `OutboxQueue.Enqueue(target, method, path, body)`.
  Appels synchrones (lecture) : clients typés injectés (déjà résilients).
- Événements publiés : `EventLog.Publish(EventTypes.X, dto, key)`. Types
  communs dans `DeeKeep.Contracts.EventTypes` ; en ajouter dans un fichier de
  son domaine si besoin (`public static class CatalogEvents`).
- Appels externes (Deezer, MusicBrainz, AcoustID, YouTube…) : `HttpClient`
  nommé avec la résilience standard (déjà par défaut) **et** un limiteur de
  débit (`System.Threading.RateLimiting`) quand l'original en avait un.
- Machine cible : Raspberry Pi ARM64. Concurrence par défaut prudente
  (`Jobs:<service>:Concurrency:<type>`), outils lourds en priorité basse
  (déjà géré par `ProcessRunner`), pas de chargement de fichiers entiers en
  mémoire (flux), caches bornés.

## API de la passerelle (REST + SSE)

- Un fichier `src/Gateway/DeeKeep.Gateway/Api/<Domaine>Api.cs` par domaine,
  méthode d'extension `Map<Domaine>Api(this RouteGroupBuilder api)` appelée par
  `ApiSetup.MapDeeKeepApi` ; routes sous `/api/<domaine>/...` (API minimales).
- Lectures en `GET` (paramètres d'URL), actions en `POST`/`PUT` (corps JSON,
  records `…Body` déclarés dans le fichier du domaine). Les points d'API
  appellent les clients typés, injectés par paramètre.
- JSON du client (`ApiSetup.ClientJson`) : propriétés en camelCase,
  énumérations en MAJUSCULES (`QUEUED`, `PRESENT`).
- Erreurs : `ApiErrorFilter` rend `{ error, code }` — `SERVICE_UNAVAILABLE`
  (503, le client met l'action en file hors ligne), `SERVICE_ERROR` (422),
  `INVALID_ARGUMENT` (400), `INVALID_OPERATION` (409).
- Événements : `GET /api/events` (Server-Sent Events) relaie tous les
  événements des services (`event: job.updated`, `data: {…}`) ; `EventHub`
  remet les charges utiles au format du client.
- Actions destinées à la file hors ligne du client : idempotentes côté
  métier autant que possible (l'en-tête `X-Idempotency-Key` est géré par la passerelle).

## Style

Commentaires en français, sobres, qui expliquent le **pourquoi** (comme le
code existant de `DeeKeep.Common`). Pas de code mort, pas de TODO laissé sans
le signaler. Tests xunit v3 pour la logique métier portée (vecteurs produits
par le Python d'origine : `python -I -B` depuis `D:\000_Logiciel\DeezerMusic`,
sans y écrire).

## Client React (`web/`)

React 19 en **JavaScript** (`.jsx` pour les composants, `.js` pour le reste ;
types documentés en JSDoc là où ils aident), React Router 8 (routeur de
données, pages chargées à la demande), `fetch` et `EventSource` (aucune
bibliothèque réseau), rxjs, Vite 8, SCSS, PWA par `vite-plugin-pwa`
(Workbox). Aucune bibliothèque de composants : dialogues, menus, infobulles,
messages et listes virtuelles sont des primitives maison (`src/shared/`).

| Commande (`cd web`) | Rôle |
|---|---|
| `npm start` | Serveur de dev (port 4200, proxy vers la passerelle sur 5000, voir `vite.config.js`). |
| `npm test` / `npm run test:watch` | Vitest + React Testing Library (jsdom). |
| `npm run lint` | ESLint 9 : `@eslint/js`, `react`, `react-hooks` (règles du compilateur React 19), `jsx-a11y`, `react-refresh`. |
| `npm run format` / `format:check` | Prettier (`.prettierrc` : 120 colonnes, guillemets simples). |
| `npm run build` | Build de production dans `web/dist`. |
| `npm run check` | Tout ce qui précède : à passer avant chaque commit. |

### Organisation

```
src/
  main.jsx              démarrage (cache restauré, routeur, service worker)
  app/                  routes.jsx (table des routes), Root.jsx, RouteError.jsx
  lib/                  store.js, cx.js, hooks/ (un hook par fichier)
  core/                 services (singletons) : auth, api, jobs, offline, player, ui
  shared/               primitives d'interface : components/, dialog/, menu/, tooltip/,
                        virtual-list/, fs-browser/, format.js, urls.js
  layout/               shell/ (coquille, navigation), jobs-panel/, AppLogo, nav-badges
  features/<page>/      une page : <Nom>Page.jsx, components/, hooks/, <page>-api.js, scss, tests
  test/                 setup.js, render.jsx, mock-api.js, media.js
```

### Socle (gelé — signaler un besoin au lieu de le modifier)

Les services sont des **singletons** de module (`export const player = …`),
dont la classe est aussi exportée pour les tests ; ils dérivent de `Store`
(`lib/store.js`) : état immuable dans `service.state`, méthodes d'action sur
l'instance. Un composant lit un service avec `useStore(service)` ou
`useStore(service, s => s.champ)` — le sélecteur rend une partie de l'état ou
une valeur simple, jamais un objet construit à la volée (dériver ensuite avec
`useMemo`). Importer un module n'ouvre aucune connexion : le flux d'événements
s'ouvre au premier abonnement.

| Fichier | Rôle |
|---|---|
| `lib/store.js` | `Store` (`state`, `setState`, `subscribe`) et le hook `useStore`. |
| `lib/cx.js` | `cx(...classes)`. |
| `lib/hooks/` | `useMediaQuery` (`useMobile()`, < 900 px), `useSubscription(observable, onNext)` (observable mémorisé par l'appelant, `null` = rien), `useInterval(cb, ms)`, `useDebounced(v, ms)`, `useEscape(cb, actif)`, `useLatest(v)` (relire l'état après un `await` dans un gestionnaire). |
| `core/api/endpoints.js` | Déclaration des appels : `endpoint(nom, champ, 'GET /api/...', {body, params, result})`, `combine(nom, ...endpoints)`, `serverEvent(nom, champ, type, filtre)`. Le résultat est rendu sous `{ champ: … }`. |
| `core/api/api.js` | `api.watch(op, vars)` → `Observable<QueryState>` (cache puis réseau, marche hors ligne) ; `api.query()` ; `api.mutate(m, vars, {label, queueable, refetch})` (clé d'idempotence, **mise en file hors ligne** si `queueable`) ; `api.subscribe()` ; `api.refetchActive()`. |
| `core/api/useWatch.js` | `useWatch(op, vars)` → `QueryState` (`null` suspend la requête). |
| `core/jobs/jobs.js` | `jobs` : toutes les tâches de fond (`state.all`, `state.active`, `get(id)`, `items({jobId,type})`, `cancel`, `retry`, `upsert`, `forget`, `load`), `isActive`, typedef `Job`. |
| `core/jobs/useJobs.js` | `useJobsByType(...types)`, `useJobsByGroup(key)`, `useJob(id)`. |
| `core/player/player.js` | `player` : `play(track, queue)` (la liste affichée devient la file), `togglePreview(track)` (extrait 30 s, « un seul son à la fois »), `current`… ; la position est dans `player.clock` (store à part). `currentTrack(state)`, typedef `PlayerTrack`. |
| `core/player/offline-audio.js` | `offlineAudio` : `pin(src)`, `unpin`, `isPinned`, état `pinned`. |
| `core/offline/connectivity.js` | `connectivity`, `linkState(state)` → online/reconnecting/offline ; `state.resyncTick` (incrémenté à chaque reconnexion). |
| `core/offline/offline-queue.js` | `offlineQueue` : actions en attente (`state.items`, `remove`). |
| `core/ui/notify.js` | `notify.info/success/error`. |
| `core/auth/auth.js` | `auth` (`state.session` : nom, `role`, `pages`, `canDownload`, `canDelete` ; `login`, `setup`, `changePassword`, `logout`), `requireSession` et `requirePage(clé)` (gardes des routes), `redirectHome`. |
| `core/auth/permissions.js`, `usePermissions.js` | `canSeePage(session, clé)`, `can(session, 'download' \| 'delete')`, `firstAllowedPath` ; hook `usePermissions()` → `{ admin, canDownload, canDelete, canSee }` pour masquer ce que le serveur refuserait (403 : `notify.error` + droits relus, fixé dans `main.jsx`). |
| `shared/components/` | `<ProgressBar value label large />`, `<PresenceBadge state tooltip />`. |
| `shared/dialog/` | `openDialog(close => <Contenu close={close} />, {width, height, maxWidth, maxHeight, panelClass, autoFocus, disableClose})` → `Promise<résultat \| undefined>` (`dialog-stack.js`) ; `<DialogTitle>`, `<DialogContent>`, `<DialogActions>` (`DialogParts.jsx`) ; `confirm({title,message,confirm,danger})` → `Promise<boolean>` (`confirm.jsx`) ; `data-autofocus` désigne le champ qui prend le focus. |
| `shared/menu/` | `<Menu at={{x,y} \| null} onClose>` (menu contextuel), `<MenuItem onClick disabled>`, `<SubMenu label>`, `<MenuButton className label>` (menu déroulant sous un bouton). |
| `shared/tooltip/` | Infobulles : attribut `data-tip="…"` sur n'importe quel élément (retours à la ligne respectés). |
| `shared/virtual-list/VirtualList.jsx` | `<VirtualList items itemHeight renderItem itemKey className />` : hauteur de ligne fixe, seules les lignes visibles sont dessinées ; la classe donne la hauteur du conteneur. |
| `shared/fs-browser/open-fs-browser.jsx` | `openFsBrowser({mode, start, audioOnly, title})` → chemins choisis sur le serveur. |
| `shared/format.js`, `shared/urls.js` | `formatDuration`, `formatBytes`, `formatWhen` ; `mediaUrl(dbPath)`, `archiveMediaUrl`, `coverUrl(sha1)`, `previewUrl(deezerId)`, `imageUrl(url)` (passe par le cache serveur `/img`). |
| `layout/nav-badges.js` | `navBadges.setHistory(n)` (compteur de l'entrée Historique). |
| `styles.scss` | Thème « Neon Nocturne » : `.dk-page`, `.dk-page-title`, `.dk-actions`, `.dk-filters`, `.control-btn` / `.accent-btn` / `.primary-btn` / `.danger-btn` / `.icon-btn`, `.dk-input`, `.dk-select`, `.dk-check`, `.dk-card`, `.dk-table` + `.dk-thead` / `.dk-row` (grilles CSS), `.dk-chip`, `.dk-badge`, `.dk-cover`, `.dk-hide-mobile` / `.dk-hide-desktop`. |

### Règles d'écriture

- **Découpage** : un composant par fichier `PascalCase.jsx` (export nommé) ;
  un fichier ne mélange pas composants et autres exports (règle
  `react-refresh`). La logique d'une page (chargement, abonnements, actions)
  vit dans des hooks `hooks/useXxx.js` ; les déclarations d'appels
  serveur dans `<page>-api.js` ; les calculs purs (tri, filtres, libellés,
  conversion de lignes) dans des modules `.js` testables sans rendu. Viser
  moins de 200 lignes par fichier.
- **Styles** : un fichier `.scss` par composant ou par page, importé par lui,
  dont toutes les règles sont imbriquées sous la classe racine
  (`.dk-<composant> { … }`) : les noms courts ne fuient pas d'une page à l'autre.
- **Hooks** (règles `react-hooks` du compilateur React) : pas de `ref.current`
  lu ou écrit pendant le rendu ; pas de `setState` synchrone dans un effet (état
  dérivé calculé au rendu, ou motif « ajuster l'état quand une prop change ») ;
  dans un effet, les rappels qui ne doivent pas le relancer passent par
  `useEffectEvent` ; dépendances complètes. Tout abonnement, minuterie ou
  écouteur se ferme dans le nettoyage de son effet.
- **Accessibilité** (`jsx-a11y`) : un élément cliquable est un `<button>` (ou
  porte rôle, `tabIndex` et clavier) ; icônes seules avec `aria-label` ; champs
  nommés (`<label>` ou `aria-label`).
- Les libellés, messages et règles d'interface sont ceux de l'original
  (`docs/analyse/`, annexe B du 01 pour les messages). Barre d'actions :
  manipulation de liste à gauche, `accent-btn` avant-dernier, **un seul**
  `primary-btn`, en dernier ; `danger-btn` pour les suppressions de fichiers.
- **Mobile et ordinateur** : sous 900 px, les tableaux deviennent des listes de
  cartes lisibles au doigt (cibles de 40 px), les barres d'actions passent à
  la ligne, les colonnes secondaires se masquent.
- Listes longues (bibliothèque : 5 000 lignes) : `VirtualList`, filtrage et
  tri côté client sur les données en cache (`useMemo`).
- **Asynchronisme** : une action longue lance une tâche et rend la main ; la
  page suit la tâche par `jobs` (progression, Arrêter) et affiche les
  résultats au fil de l'eau (`jobs.items`). Quitter la page ne l'arrête pas ;
  y revenir montre son état.
- **Déconnexion** : lecture par `useWatch` / `api.watch` (s'affiche depuis le
  cache) ; mutations par `api.mutate` avec un `label` parlant ;
  `queueable: false` pour ce qui n'a pas de sens différé (ex. recherche Deezer,
  écoute). Afficher clairement quand une donnée est périmée (`stale`) ou quand
  une action est en attente. Se resynchroniser sur `connectivity.state.resyncTick`
  si la page tient un état local.
- Aucune dépendance npm nouvelle sans le signaler.

### Tests

Vitest + React Testing Library + `user-event`, fichiers `*.test.js(x)` à côté
du code testé.

- Calculs purs : tests unitaires directs (vecteurs d'entrée / sortie).
- Composants et pages : rendus avec `renderWithRouter(ui)` (`test/render.jsx`,
  qui fournit aussi `user` et les couches communes), interrogés par rôle et
  libellé (`getByRole('button', { name: 'Extraire' })`), comme un utilisateur.
- Serveur : `mockApi({ NomOperation: réponse })` (`test/mock-api.js`) remplace
  `api` opération par opération ; `emit(nom, données)` pousse un événement
  d'abonnement, `mutations(nom)` liste les variables envoyées. Aucun test
  n'ouvre de connexion réseau.
- `test/setup.js` fournit `matchMedia` (`setMobile(true)` de `test/media.js`),
  `ResizeObserver`, l'audio simulé et IndexedDB en mémoire, et vide les
  singletons (boîtes ouvertes, message, tâches) après chaque test.
