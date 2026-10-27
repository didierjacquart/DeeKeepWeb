#!/bin/sh
# Sauvegarde à chaud des bases SQLite (API de sauvegarde de SQLite : cohérente
# même pendant les écritures en WAL), compressée, sept jours conservés.
# L'archive audio (plusieurs gigaoctets) n'est pas recopiée : elle EST la copie.
set -eu

DATA=${DEEKEEP_DATA:-/var/lib/deekeep}
DEST=${DEEKEEP_BACKUP_DIR:-/var/lib/deekeep/backups}
KEEP_DAYS=${DEEKEEP_BACKUP_DAYS:-7}
STAMP=$(date +%Y%m%d-%H%M)

mkdir -p "$DEST"
for db in deekeep_cache.db deekeep_web.db; do
    [ -f "$DATA/$db" ] || continue
    out="$DEST/${db%.db}-$STAMP.db"
    sqlite3 "$DATA/$db" ".timeout 30000" ".backup '$out'"
    # Une sauvegarde qui ne passe pas le contrôle d'intégrité est supprimée et signalée.
    if [ "$(sqlite3 "$out" 'PRAGMA quick_check;')" != "ok" ]; then
        logger -t deekeep-backup "sauvegarde de $db corrompue, supprimée"
        rm -f "$out"
        continue
    fi
    gzip -f "$out"
done
find "$DEST" -name '*.db.gz' -mtime +"$KEEP_DAYS" -delete
logger -t deekeep-backup "sauvegarde terminée dans $DEST"
