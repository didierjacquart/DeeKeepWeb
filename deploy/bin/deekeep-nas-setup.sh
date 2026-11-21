#!/bin/sh
# Place les bases SQLite de DeeKeep sur un partage SMB du NAS.
#
#     sudo sh /opt/deekeep/bin/deekeep-nas-setup.sh //192.168.1.159/web/Database
#
# Prérequis : DeeKeep installé (install.sh) et le fichier d'identifiants du NAS
# créé par l'administrateur (jamais versionné) :
#     sudo sh -c 'printf "username=UTILISATEUR\npassword=MOT_DE_PASSE\n" > /etc/deekeep/smb-nas'
#     sudo chmod 600 /etc/deekeep/smb-nas
#
# Ce que fait le script :
#  - monte le partage (fstab) avec les droits de l'utilisateur deekeep ;
#  - « nobrl » : les verrous restent gérés par le noyau du serveur. Ils protègent
#    les six services entre eux, mais PAS un autre ordinateur : l'application
#    desktop ne doit pas ouvrir cette base pendant que le serveur tourne ;
#  - les services ne démarrent jamais sans le partage (RequiresMountsFor) : sans
#    lui, SQLite créerait une base vide à côté ;
#  - pointe Database:MainPath et Database:WebPath vers le partage ;
#  - vérifie que SQLite en WAL fonctionne sur le partage (écritures concurrentes).
set -eu

SHARE=${1:?"Partage SMB attendu, ex. //192.168.1.159/web/Database"}
MOUNT=${2:-/mnt/deekeep-db}
CONF=/etc/deekeep
CRED=$CONF/smb-nas

[ "$(id -u)" -eq 0 ] || { echo "À lancer avec sudo." >&2; exit 1; }
id deekeep >/dev/null 2>&1 || { echo "Lancez d'abord install.sh." >&2; exit 1; }
[ -f "$CRED" ] || { echo "Créez d'abord $CRED (identifiants du NAS, voir l'en-tête du script)." >&2; exit 1; }
chown root:root "$CRED" && chmod 600 "$CRED"

echo "== Paquets"
apt-get install -y -qq cifs-utils python3 >/dev/null

echo "== Montage $SHARE -> $MOUNT"
mkdir -p "$MOUNT"
OPTS="credentials=$CRED,uid=$(id -u deekeep),gid=$(id -g deekeep),file_mode=0660,dir_mode=0770,iocharset=utf8,vers=3.0,nobrl,cache=strict,_netdev,nofail,x-systemd.mount-timeout=30"
# Une seule ligne pour ce point de montage : une relance remplace l'ancienne.
grep -v "[[:space:]]$MOUNT[[:space:]]" /etc/fstab > /etc/fstab.deekeep-tmp || true
echo "$SHARE $MOUNT cifs $OPTS 0 0" >> /etc/fstab.deekeep-tmp
mv /etc/fstab.deekeep-tmp /etc/fstab
systemctl daemon-reload
mountpoint -q "$MOUNT" && umount "$MOUNT"
mount "$MOUNT"
mountpoint -q "$MOUNT" || { echo "Le partage ne s'est pas monté." >&2; exit 1; }

echo "== Essai de SQLite (WAL, deux processus) sur le partage"
TEST="$MOUNT/.deekeep-wal-test.db"
rm -f "$TEST" "$TEST-wal" "$TEST-shm"
su -s /bin/sh deekeep -c "python3 - '$TEST'" <<'PY'
import multiprocessing, sqlite3, sys
path = sys.argv[1]
def work(n):
    db = sqlite3.connect(path, timeout=30)
    db.execute("PRAGMA journal_mode=WAL")
    for i in range(200):
        with db:
            db.execute("INSERT INTO t (who, i) VALUES (?, ?)", (n, i))
    db.close()
db = sqlite3.connect(path, timeout=30)
assert db.execute("PRAGMA journal_mode=WAL").fetchone()[0] == "wal", "WAL refusé"
db.execute("CREATE TABLE t (who INTEGER, i INTEGER)")
db.commit()
procs = [multiprocessing.Process(target=work, args=(n,)) for n in range(3)]
for p in procs: p.start()
for p in procs: p.join()
assert all(p.exitcode == 0 for p in procs), "un écrivain a échoué"
count = db.execute("SELECT COUNT(*) FROM t").fetchone()[0]
check = db.execute("PRAGMA integrity_check").fetchone()[0]
assert count == 600 and check == "ok", (count, check)
print("   SQLite WAL : OK (600 écritures concurrentes, intégrité vérifiée)")
PY
rm -f "$TEST" "$TEST-wal" "$TEST-shm"

echo "== Services liés au partage"
for unit in deekeep@.service deekeep-backup.service; do
    mkdir -p "/etc/systemd/system/$unit.d"
    cat > "/etc/systemd/system/$unit.d/nas-db.conf" <<EOF
# Bases SQLite sur le NAS : pas de démarrage sans le partage.
[Unit]
RequiresMountsFor=$MOUNT
After=remote-fs.target

[Service]
ReadWritePaths=$MOUNT
Environment=DEEKEEP_DATA=$MOUNT
EOF
done
systemctl daemon-reload

echo "== Configuration"
python3 - "$CONF/appsettings.json" "$MOUNT" <<'PY'
import json, sys
path, mount = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8") as f:
    conf = json.load(f)
db = conf.setdefault("Database", {})
db["MainPath"] = f"{mount}/deekeep_cache.db"
db["WebPath"] = f"{mount}/deekeep_web.db"
with open(path, "w", encoding="utf-8") as f:
    json.dump(conf, f, ensure_ascii=False, indent=2)
    f.write("\n")
print(f"   Database:MainPath = {db['MainPath']}")
PY

[ -f "$MOUNT/deekeep_cache.db" ] || echo "   !! $MOUNT/deekeep_cache.db absent : copiez-y la base existante."
echo ""
echo "Bases sur le NAS. Redémarrez : sudo systemctl restart deekeep.target"
