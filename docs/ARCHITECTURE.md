# DeeKeep Web — Architecture

Réécriture web de l'application desktop **DeeKeep** (PyQt6, dossier
`D:\000_Logiciel\DeezerMusic`). Toutes les fonctionnalités de l'original sont
reprises ; la spécification détaillée de chacune est dans `docs/analyse/`.

## Contraintes

| Contrainte | Réponse |
|---|---|
| GraphQL | Passerelle **HotChocolate** : un seul schéma (requêtes, mutations, abonnements) pour le client |
| Angular | SPA Angular (composants autonomes, signaux), **PWA** installable sur mobile et ordinateur |
| Microservices .NET | 5 services métier + 1 passerelle, .NET 10, API minimales internes |
| Base SQLite existante | `deekeep_cache.db` conservée **telle quelle** : même schéma, même empreinte `h` — l'application desktop peut toujours la rouvrir. Turso n'étant plus utilisé, la synchronisation en ligne n'est pas reprise et `sync_outbox` n'est plus alimentée |
| Serveur Linux ARM64 peu puissant | systemd natif, pas de conteneurs, pas de courtier de messages, GC poste de travail, limites mémoire, parallélisme borné, publication ReadyToRun |
| Résilience | Polly (réessais, disjoncteur, délais) sur tous les appels sortants, tâches durables reprises au redémarrage, boîte d'envoi inter-services, sondes de santé, redémarrage systemd |
| Déconnexion | Cache GraphQL persistant (IndexedDB), file de mutations hors ligne rejouée à la reconnexion (idempotente), service worker, abonnements qui se reconnectent et resynchronisent, écoute hors ligne des titres épinglés |
| Asynchronisme | Toute opération longue est une **tâche** serveur (file durable, progression poussée par abonnement GraphQL, annulable) ; le client ne bloque jamais |

## Vue d'ensemble

```
 Navigateur / mobile (PWA Angular)
   │  HTTPS : /graphql (requêtes, mutations) · WebSocket graphql-ws (abonnements)
   │          /media/* (flux audio, Range) · /covers/* (jaquettes)
   ▼
 Caddy (TLS, compression)  ──►  Gateway :5000  (HotChocolate + YARP + fichiers Angular + auth)
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
  exposé en SSE ; la passerelle les relaie en abonnements GraphQL.
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
  (erreurs GraphQL partielles), le reste du schéma répond.
- systemd : `Restart=always`, `WatchdogSec`, `MemoryMax`, ordre de démarrage.
- Santé : `/health/live`, `/health/ready` sur chaque service, agrégés par la
  passerelle dans la page **Diagnostic**.

## Déconnexion (client)

- `apollo3-cache-persist` → IndexedDB : la bibliothèque s'affiche hors ligne.
- Lien Apollo « hors ligne » : une mutation émise sans réseau est mise en file
  (IndexedDB) avec un `clientMutationId` ; la passerelle la rend idempotente
  (table `idempotency`), la file est rejouée à la reconnexion.
- `graphql-ws` : reconnexion exponentielle infinie ; à chaque reconnexion, les
  requêtes actives sont rafraîchies (rattrapage des événements manqués) et les
  tâches en cours resouscrites.
- Service worker Angular : coquille, jaquettes (cache LRU), **titres épinglés
  pour l'écoute hors ligne** (Cache Storage).
- Indicateur permanent (pied de barre latérale / en-tête mobile) : en ligne,
  hors ligne, reconnexion, opérations en attente.

## Sécurité

Application mono-utilisateur exposée sur le réseau : connexion par mot de passe
(haché PBKDF2 dans `deekeep_web.db`), cookie HttpOnly + jeton pour le WebSocket.
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
  web/                      application Angular
  deploy/                   systemd, Caddyfile, scripts d'installation ARM64
  docs/
```
