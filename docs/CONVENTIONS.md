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

| Domaine | Service | Contrats | Passerelle GraphQL | Tests |
|---|---|---|---|---|
| Catalogue | `src/Services/DeeKeep.Catalog/` | `DeeKeep.Contracts/Catalog/` | `Gateway/Domains/Catalog/` | `tests/DeeKeep.Tests/Catalog/` |
| Bibliothèque | `src/Services/DeeKeep.Library/` sauf `Organize/` | `DeeKeep.Contracts/Library/` | `Gateway/Domains/Library/` | `tests/DeeKeep.Tests/Library/` |
| Organiser, doublons, tags | `src/Services/DeeKeep.Library/Organize/` | `DeeKeep.Contracts/Organize/` | `Gateway/Domains/Organize/` | `tests/DeeKeep.Tests/Organize/` |
| Téléchargement | `src/Services/DeeKeep.Downloader/` | `DeeKeep.Contracts/Downloader/` | `Gateway/Domains/Downloader/` | `tests/DeeKeep.Tests/Downloader/` |
| Qualité | `src/Services/DeeKeep.Quality/` | `DeeKeep.Contracts/Quality/` | `Gateway/Domains/Quality/` | `tests/DeeKeep.Tests/Quality/` |
| Archive | `src/Services/DeeKeep.Storage/` | `DeeKeep.Contracts/Storage/` | `Gateway/Domains/Storage/` | `tests/DeeKeep.Tests/Storage/` |

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

## Passerelle GraphQL (HotChocolate 16)

- Un dossier `src/Gateway/DeeKeep.Gateway/Domains/<Domaine>/` par domaine.
- Classes statiques partielles annotées `[QueryType]`, `[MutationType]`,
  `[SubscriptionType]` (générateur de sources : elles sont ajoutées
  automatiquement, rien à enregistrer). Modèle : `Domains/Jobs/JobTypes.cs`.
- Les résolveurs appellent les clients typés, injectés par paramètre **marqué `[Service]`** (sinon HotChocolate en fait un argument GraphQL).
- Vérifier le schéma : `dotnet test --project tests/DeeKeep.Tests -- --filter-class "DeeKeep.Tests.Gateway.SchemaTests"` (construit le schéma complet et écrit `docs/schema.graphql`). Les noms de types C# exposés doivent être uniques dans tout le schéma (préfixer si besoin).
- Abonnements : sujet `Topics.For(EventTypes.X)`, message `ServiceEvent`,
  désérialiser `evt.Data` avec `JsonDefaults.Options` (voir `JobSubscriptions`).
- Noms GraphQL en anglais (camelCase), descriptions (`/// <summary>`) en français.
- Les types `JsonNode` sont exposés en scalaire `Any`.
- Pas d'erreur fatale : une panne de service doit donner une erreur GraphQL
  partielle (déjà traduite par `ServiceErrorFilter`).
- Mutations destinées à la file hors ligne du client : idempotentes côté
  métier autant que possible (l'en-tête `X-Idempotency-Key` est géré par la passerelle).

## Style

Commentaires en français, sobres, qui expliquent le **pourquoi** (comme le
code existant de `DeeKeep.Common`). Pas de code mort, pas de TODO laissé sans
le signaler. Tests xunit v3 pour la logique métier portée (vecteurs produits
par le Python d'origine : `python -I -B` depuis `D:\000_Logiciel\DeezerMusic`,
sans y écrire).

## Client Angular (`web/`)

Angular 22 (composants autonomes, signaux, `OnPush`, contrôle de flux `@if/@for`),
Angular Material 22 (dialogues, menus, infobulles, snackbars uniquement),
Apollo Angular 14 / Apollo Client 4, graphql-ws. Build :
`cd web && npx ng build --configuration development` ; serveur de dev :
`npx ng serve` (proxy vers la passerelle sur 5000, voir `proxy.conf.json`).

### Socle (gelé — signaler un besoin au lieu de le modifier)

| Fichier | Rôle |
|---|---|
| `core/graphql/gql.service.ts` | `watch(query, vars)` → `Observable<QueryState>` (cache puis réseau, marche hors ligne) ; `query()` ; `mutate(m, vars, {label, queueable, refetch})` (clé d'idempotence, **mise en file hors ligne** si `queueable`) ; `subscribe()` |
| `core/jobs/jobs.service.ts` | Toutes les tâches de fond : `all`, `active`, `byType(...)`, `byGroup(key)`, `get(id)`, `items({jobId,type})` (résultats élément par élément), `cancel`, `retry`, `upsert`, `forget`. `JOB_FIELDS` pour les fragments. |
| `core/player/player.service.ts` | `play(track, queue)` (la liste affichée devient la file), `togglePreview(track)` (extrait 30 s, « un seul son à la fois »), `current`, `playing`… Type `PlayerTrack`. |
| `core/player/offline-audio.service.ts` | Épingler un titre pour l'écoute hors ligne (`pin(src)`, `isPinned`). |
| `core/offline/connectivity.service.ts` | `state()` online/reconnecting/offline, `resyncTick` (incrémenté à chaque reconnexion). |
| `core/ui/notify.service.ts` | `info/success/error` (snackbar). |
| `shared/ui.ts` | `ProgressBar` (`<dk-progress [value] [label] [large]>`), `PresenceBadge` (`<dk-presence [state] [tooltip]>`), `ConfirmService.ask({title,message,confirm,danger})`, URL : `mediaUrl(dbPath)`, `archiveMediaUrl(dbPath)`, `coverUrl(sha1)`, `previewUrl(deezerId)`, `imageUrl(url)` (passe par le cache serveur `/img`). |
| `shared/pipes.ts` | `duration`, `bytes`, `when` (+ fonctions `formatDuration`…). |
| `layout/shell.ts` | `NavBadges.history` (compteur de l'entrée Historique). |
| `styles.scss` | Thème « Neon Nocturne » : `.dk-page`, `.dk-page-title`, `.dk-actions`, `.dk-filters`, `.control-btn` / `.accent-btn` / `.primary-btn` / `.danger-btn` / `.icon-btn`, `.dk-input`, `.dk-select`, `.dk-check`, `.dk-card`, `.dk-table` + `.dk-thead` / `.dk-row` (grilles CSS), `.dk-chip`, `.dk-badge`, `.dk-cover`, `.dk-hide-mobile` / `.dk-hide-desktop`. |

### Règles

- Une page par dossier `src/app/features/<page>/` (le fichier `<page>.page.ts`
  existe, classe déjà routée : la remplacer). Composants, requêtes GraphQL
  (`gql\`...\``) et types TypeScript de la page dans ce dossier.
- Les libellés, messages et règles d'interface sont ceux de l'original
  (`docs/analyse/`, annexe B du 01 pour les messages). Barre d'actions :
  manipulation de liste à gauche, `accent-btn` avant-dernier, **un seul**
  `primary-btn`, en dernier ; `danger-btn` pour les suppressions de fichiers.
- **Mobile et ordinateur** : sous 900 px, les tableaux deviennent des listes de
  cartes lisibles au doigt (cibles de 40 px), les barres d'actions passent à
  la ligne, les colonnes secondaires se masquent. Tester les deux largeurs.
- Listes longues (bibliothèque : 5 000 lignes) : `cdk-virtual-scroll-viewport`
  (`@angular/cdk/scrolling`), filtrage et tri côté client sur les données en cache.
- **Asynchronisme** : une action longue lance une tâche et rend la main ; la
  page suit la tâche par `JobsService` (progression, Arrêter) et affiche les
  résultats au fil de l'eau (`JobsService.items`). Quitter la page ne l'arrête pas ;
  y revenir montre son état.
- **Déconnexion** : lecture par `GqlService.watch` (s'affiche depuis le cache) ;
  mutations par `GqlService.mutate` avec un `label` parlant ; `queueable: false`
  pour ce qui n'a pas de sens différé (ex. recherche Deezer, écoute). Afficher
  clairement quand une donnée est périmée (`stale`) ou quand une action est en
  attente. Se resynchroniser sur `connectivity.resyncTick()` si la page tient un
  état local.
- Aucune dépendance npm nouvelle sans le signaler.
