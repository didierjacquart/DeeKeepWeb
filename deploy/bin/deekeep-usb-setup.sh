#!/bin/sh
# Bibliothèque musicale sur le NAS, copie synchronisée sur un disque USB du serveur.
#
#     sudo sh /opt/deekeep/bin/deekeep-usb-setup.sh //192.168.1.159/music 0F6A-107E
#
# Prérequis : install.sh et deekeep-nas-setup.sh déjà passés (identifiants du NAS dans
# /etc/deekeep/smb-nas). Le disque USB est désigné par l'UUID de sa partition
# (lsblk -o NAME,UUID,FSTYPE,SIZE).
#
# Ce que fait le script :
#  - monte le partage musique du NAS sur /mnt/deekeep-music (racine de la bibliothèque) ;
#  - monte le disque USB sur /mnt/deekeep-usb à la demande : il peut être débranché et
#    rebranché, le service le retrouve et lance une synchronisation ;
#  - fait correspondre les chemins de la base (« D:\ ») à la bibliothèque du NAS ;
#  - renseigne la section Sync de la configuration.
set -eu

SHARE=${1:?"Partage musique attendu, ex. //192.168.1.159/music"}
USB_UUID=${2:?"UUID de la partition du disque USB attendu (lsblk -o NAME,UUID)"}
DB_ROOT=${3:-'D:\'}
MUSIC=/mnt/deekeep-music
USB=/mnt/deekeep-usb
CONF=/etc/deekeep
CRED=$CONF/smb-nas

[ "$(id -u)" -eq 0 ] || { echo "À lancer avec sudo." >&2; exit 1; }
id deekeep >/dev/null 2>&1 || { echo "Lancez d'abord install.sh." >&2; exit 1; }
[ -f "$CRED" ] || { echo "Identifiants du NAS absents ($CRED) : voir deekeep-nas-setup.sh." >&2; exit 1; }

DUID=$(id -u deekeep)
DGID=$(id -g deekeep)
FSTYPE=$(blkid -s TYPE -o value "/dev/disk/by-uuid/$USB_UUID" 2>/dev/null || true)
[ -n "$FSTYPE" ] || { echo "Aucune partition d'UUID $USB_UUID : le disque est-il branché ?" >&2; exit 1; }

echo "== Montages"
mkdir -p "$MUSIC" "$USB"
# Le bureau a pu monter le disque ailleurs (/media/…) : on le libère.
for target in $(findmnt -rno TARGET -S "UUID=$USB_UUID" 2>/dev/null); do
    [ "$target" = "$USB" ] || umount "$target"
done

case "$FSTYPE" in
    vfat|exfat) USB_OPTS="uid=$DUID,gid=$DGID,umask=0002,utf8,flush" ;;
    ntfs|ntfs3) FSTYPE=ntfs3; USB_OPTS="uid=$DUID,gid=$DGID,umask=0002" ;;
    *) USB_OPTS="defaults" ;;
esac
# Montage à la demande : débrancher le disque ne bloque pas le démarrage, le rebrancher le remonte.
USB_OPTS="$USB_OPTS,nofail,noauto,x-systemd.automount,x-systemd.idle-timeout=300,x-systemd.device-timeout=5s,x-gvfs-hide"
# closetimeo=0 : sans fermeture différée, le NAS garderait l'heure de la copie au lieu de la date d'origine.
MUSIC_OPTS="credentials=$CRED,uid=$DUID,gid=$DGID,file_mode=0664,dir_mode=0775,iocharset=utf8,vers=3.0,closetimeo=0,_netdev,nofail,x-systemd.mount-timeout=30"

grep -v "[[:space:]]$MUSIC[[:space:]]" /etc/fstab | grep -v "[[:space:]]$USB[[:space:]]" > /etc/fstab.deekeep-tmp || true
echo "$SHARE $MUSIC cifs $MUSIC_OPTS 0 0" >> /etc/fstab.deekeep-tmp
echo "UUID=$USB_UUID $USB $FSTYPE $USB_OPTS 0 0" >> /etc/fstab.deekeep-tmp
mv /etc/fstab.deekeep-tmp /etc/fstab
systemctl daemon-reload
mountpoint -q "$MUSIC" && umount "$MUSIC"
mount "$MUSIC"
systemctl restart "$(systemd-escape -p --suffix=automount "$USB")"
ls "$USB" >/dev/null
mountpoint -q "$MUSIC" || { echo "Le partage musique ne s'est pas monté." >&2; exit 1; }
su -s /bin/sh deekeep -c "touch '$MUSIC/.deekeep-w' && rm '$MUSIC/.deekeep-w'" || { echo "Le partage musique n'accepte pas l'écriture." >&2; exit 1; }
su -s /bin/sh deekeep -c "touch '$USB/.deekeep-w' && rm '$USB/.deekeep-w'" || { echo "Le disque USB n'accepte pas l'écriture." >&2; exit 1; }
# L'utilisateur du bureau garde l'accès en écriture au disque (groupe deekeep).
for u in $(getent group sudo | cut -d: -f4 | tr ',' ' '); do usermod -aG deekeep "$u"; done

echo "== Services liés aux montages"
mkdir -p /etc/systemd/system/deekeep@.service.d
cat > /etc/systemd/system/deekeep@.service.d/music.conf <<EOF
# Bibliothèque sur le NAS, disque USB monté à la demande.
[Unit]
RequiresMountsFor=$MUSIC
After=remote-fs.target

[Service]
ReadWritePaths=$MUSIC $USB
EOF
systemctl daemon-reload

echo "== Configuration"
python3 - "$CONF/appsettings.json" "$MUSIC" "$USB" "$DB_ROOT" <<'PY'
import json, sys
path, music, usb, db_root = sys.argv[1:5]
with open(path, encoding="utf-8") as f:
    conf = json.load(f)
conf["PathMap"] = {"Mappings": [{"From": db_root, "To": music + "/"}]}
library = conf.setdefault("Library", {})
roots = library.get("BrowseRoots") or []
for r in (music, usb):
    if r not in roots:
        roots.append(r)
library["BrowseRoots"] = roots
library["DefaultDownloadDir"] = music
sync = conf.setdefault("Sync", {})
sync["UsbRoot"] = usb
sync["LibraryRoot"] = music
with open(path, "w", encoding="utf-8") as f:
    json.dump(conf, f, ensure_ascii=False, indent=2)
    f.write("\n")
print(f"   PathMap {db_root} -> {music}/ ; Sync : {usb} <-> {music}")
PY

echo ""
echo "Bibliothèque : $MUSIC ($SHARE) ; disque USB : $USB."
echo "Redémarrez : sudo systemctl restart deekeep.target — la première synchronisation part toute seule."
