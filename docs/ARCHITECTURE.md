# DeeKeep Web — Architecture

Réécriture web de l'application desktop **DeeKeep** (PyQt6, dossier
`D:\000_Logiciel\DeezerMusic`). Toutes les fonctionnalités de l'original sont
reprises ; la spécification détaillée de chacune est dans `docs/analyse/`.

## Contraintes

| Contrainte | Réponse |
|---|---|
| API | Passerelle **REST** : routes JSON `/api/...` par domaine, événements poussés par un flux **SSE** (`/api/events`) |
| React | SPA React 19 (composants fonctions, hooks, Vite), **PWA** installable sur mobile et ordinateur |
| Microservices .NET | 5 services métier + 1 passerelle, .NET 10, API minimales internes |
| Base SQLite existante | `deekeep_cache.db` conservée **telle quelle** : même schéma, même empreinte `h` — l'application desktop peut toujours la rouvrir. Turso n'étant plus utilisé, la synchronisation en ligne n'est pas reprise et `sync_outbox` n'est plus alimentée |
| Serveur Linux ARM64 peu puissant | systemd natif, pas de conteneurs, pas de courtier de messages, GC poste de travail, limites mémoire, parallélisme borné, publication ReadyToRun |
| Résilience | Polly (réessais, disjoncteur, délais) sur tous les appels sortants, tâches durables reprises au redémarrage, boîte d'envoi inter-services, sondes de santé, redémarrage systemd |
| Déconnexion | Dernières réponses persistées (IndexedDB), file d'actions hors ligne rejouée à la reconnexion (idempotente), service worker, flux d'événements qui se reconnecte et resynchronisent, écoute hors ligne des titres épinglés |
| Asynchronisme | Toute opération longue est une **tâche** serveur (file durable, progression poussée par le flux d'événements, annulable) ; le client ne bloque jamais |

## Vue d'ensemble

```
 Navigateur / mobile (PWA React)
   │  HTTPS : /api/* (JSON : lectures, actions) · /api/events (SSE : événements)
   │          /media/* (flux audio, Range ; jaquette du fichier) · /covers/* (jaquettes)
   ▼
 Caddy (TLS, compression)  ──►  Gateway :5000  (API REST + SSE + YARP + fichiers React + auth)
                                   │  HTTP interne 127.0.0.1 (+ clé X-Internal-Key)
                                   │  SSE /internal/events (événements, reprise Last-Event-ID)
     ┌───────────────┬─────────────┼───────────────┬──────────────────┐
     ▼               ▼             ▼               ▼                  ▼
 Catalog :5101   Library :5102  Downloader :5103  Quality :5104   Storage :5105
 Deezer,         titres,        yt-dlp, ffmpeg,   analyse ffmpeg,  archive : seau S3
 MusicBrainz,    jaquettes,     extraction,       6 défauts,       (R2/B2) ou base SQLite
 AcoustID,       réglages,      conversion,       retéléchargement d'archive sans perte
 durées de       index de       retéléchargement
 référence,      présence,
 tags proposés   rangement,
                 doublons,
                 écriture de tags,
                 flux audio
     │               │              │                │                  │
     └───────────────┴──────┬───────┴────────────────┴──────────────────┘
                            ▼
          deekeep_cache.db (existante, WAL)      deekeep_web.db (tâches, événements,
          chaque table a UN service propriétaire  boîtes d'envoi, idempotence, utilisateurs)
```

### Propriété des tables (base existante)

| Table | Service propriétaire (seul à écrire) |
|---|---|
| `app_settings`, `searches`, `tracks`, `covers` | Library |
| `reference_durations` | Catalog |
| `quality` | Quality |
| `archive` | Storage |

Toute écriture conserve les colonnes techniques de l'original (`updated_at`,
`deleted` en pierre tombale, empreinte `h` recalculée à l'identique du Python).
`sync_state`, `sync_outbox` et la synchronisation Turso ne sont plus utilisées.

SQLite est partagée entre processus d'une même machine : WAL,
`busy_timeout = 5000`, transactions courtes, `BEGIN IMMEDIATE` pour les
écritures, réessai Polly sur `SQLITE_BUSY`.

## Briques communes (`src/BuildingBlocks`)

- **DeeKeep.Common** — accès SQLite (`Db`), `TableSpec`/`RowHash` (BLAKE2b 64 bits
  masqué à 30 bits, sérialisation canonique identique au Python),
  `DeeKeepDb.UpsertMany` (ligne complète + horodatage + empreinte `h`),
  `SettingsReader`, normalisation de texte (`Canonical`), correspondance de
  chemins Windows ↔ Linux (`PathMap`), exécution de processus annulable
  (`ProcessRunner` : ffmpeg, ffprobe, yt-dlp, fpcalc — tue l'arbre à
  l'annulation), **tâches durables** (`JobStore`, `JobWorker`), **événements**
  (`EventLog` + point SSE `/internal/events`, `EventSubscriber` avec curseur
  persistant), **boîte d'envoi** HTTP inter-services (`Outbox`), défauts de
  service (`AddServiceDefaults` : santé, journalisation, résilience, clé interne).
- **DeeKeep.Contracts** — DTO partagés (records) entre services et passerelle.

## Asynchronisme : tâches et événements

- Une tâche = ligne `jobs` (type, état, progression, message, charge utile
  JSON, résultat, tentatives). États : `Queued → Running → Succeeded | Failed |
  Cancelled`. Les tâches `Running` au démarrage repassent `Queued` : un
  redémarrage (coupure de courant, mise à jour) ne perd rien.
- Chaque service a ses ouvriers (`Channel<T>` + N consommateurs ; N borné par
  configuration : 2 téléchargements, 1 analyse qualité sur un Raspberry Pi).
- La progression part en **événements** (`job.progress`, `job.item`,
  `library.changed`, `track.updated`, `archive.updated`…) dans le journal du service,
  exposé en SSE ; la passerelle les relaie aux navigateurs par `/api/events`.
- Une tâche par lot (analyse de 3 000 fichiers) écrit ses résultats au fil de
  l'eau dans la base : le client les voit apparaître sans attendre la fin, comme
  dans l'original, et **Arrêter** n'en perd aucun.

## Résilience

- Appels sortants (Deezer, MusicBrainz, AcoustID, S3, inter-services) :
  `AddStandardResilienceHandler` — délai par tentative, réessais exponentiels
  avec gigue, disjoncteur, délai global ; limiteurs de débit dédiés (Deezer 5/s,
  MusicBrainz 1/s).
- Inter-services : appels synchrones via clients typés résilients ; effets de
  bord qui *doivent* aboutir (enregistrer un fichier extrait, invalider une
  mesure) via la **boîte d'envoi** durable, rejouée jusqu'au succès.
- Passerelle dégradée : un service indisponible ne fait tomber que ses champs
  (503 `SERVICE_UNAVAILABLE` sur ses routes), le reste de l'API répond.
- systemd : `Restart=always`, `WatchdogSec`, `MemoryMax`, ordre de démarrage.
- Santé : `/health/live`, `/health/ready` sur chaque service, agrégés par la
  passerelle dans la page **Diagnostic**.

## Déconnexion (client)

- Dernières réponses du serveur persistées dans IndexedDB : la bibliothèque s'affiche hors ligne.
- File hors ligne : une action émise sans réseau est mise en file
  (IndexedDB) avec un `clientMutationId` ; la passerelle la rend idempotente
  (table `idempotency`), la file est rejouée à la reconnexion.
- Flux SSE `/api/events` : reconnexion automatique infinie ; à chaque reconnexion, les
  requêtes actives sont rafraîchies (rattrapage des événements manqués) et les
  tâches en cours resouscrites.
- Service worker (Workbox) : coquille, jaquettes (cache LRU), **titres épinglés
  pour l'écoute hors ligne** (Cache Storage).
- Indicateur permanent (pied de barre latérale / en-tête mobile) : en ligne,
  hors ligne, reconnexion, opérations en attente.

## Sécurité

Application exposée sur le réseau : comptes nommés (table `users` de
`deekeep_web.db`, mots de passe hachés PBKDF2), cookie HttpOnly (aussi pour le
flux d'événements) revalidé à chaque requête contre la base (cache de 5 s) : un
compte supprimé, désactivé ou recréé perd aussitôt ses sessions.

Rôles et droits : un **administrateur** voit tout et gère les comptes (page
Administration, `/api/admin/...`) ; un **utilisateur** ne voit que les pages
cochées pour lui et n'a que les droits d'action donnés (**télécharger**,
**supprimer**). La passerelle applique ces droits après l'authentification
(`UseDeeKeepPermissions`) selon une table unique de règles préfixe + méthode →
page, droit ou administrateur (`Gateway/Api/Permissions.cs`, la première règle
qui correspond s'applique ; route sans règle : tout compte connecté). Refus :
403 `{ error, code: "FORBIDDEN" }`. Les flux binaires (`/media`, `/covers`…)
restent ouverts à tout compte connecté. Le client masque pages et boutons
refusés (`core/auth/permissions.js`) mais n'est pas la protection. Toute
nouvelle route sensible s'ajoute à la table. Authentification désactivée
(configuration de développement) : tout est permis.
Les services n'écoutent que sur `127.0.0.1` et exigent l'en-tête
`X-Internal-Key`. Les secrets (S3, AcoustID, ARL) restent hors de la base
synchronisée, dans `/etc/deekeep/secrets.json` ou des variables d'environnement.

## Arborescence

```
DeeKeepWeb/
  DeeKeep.slnx
  src/
    BuildingBlocks/DeeKeep.Common, DeeKeep.Contracts
    Gateway/DeeKeep.Gateway
    Services/DeeKeep.Catalog, DeeKeep.Library, DeeKeep.Downloader,
             DeeKeep.Quality, DeeKeep.Storage
  tests/DeeKeep.Tests
  web/                      application React
  deploy/                   systemd, Caddyfile, scripts d'installation ARM64
  docs/
```
