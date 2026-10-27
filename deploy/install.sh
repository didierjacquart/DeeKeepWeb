#!/bin/sh
# Installation / mise à jour de DeeKeep sur un serveur Debian ou Raspberry Pi OS
# (ARM64 ou x64). À lancer en root depuis le dossier du paquet décompressé :
#     sudo ./install.sh
# Une mise à jour conserve /etc/deekeep (configuration, secrets) et /var/lib/deekeep (bases).
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)
PREFIX=/opt/deekeep
DATA=/var/lib/deekeep
CONF=/etc/deekeep
DOTNET_CHANNEL=10.0

[ "$(id -u)" -eq 0 ] || { echo "À lancer avec sudo." >&2; exit 1; }

case "$(uname -m)" in
    aarch64|arm64) ARCH=arm64; YTDLP_ASSET=yt-dlp_linux_aarch64; DENO_ASSET=deno-aarch64-unknown-linux-gnu.zip ;;
    x86_64) ARCH=x64; YTDLP_ASSET=yt-dlp_linux; DENO_ASSET=deno-x86_64-unknown-linux-gnu.zip ;;
    *) echo "Architecture non prise en charge : $(uname -m)" >&2; exit 1 ;;
esac

echo "== Paquets système"
apt-get update -qq
apt-get install -y -qq ffmpeg libchromaprint-tools sqlite3 curl unzip ca-certificates caddy >/dev/null

echo "== Runtime ASP.NET Core $DOTNET_CHANNEL"
if ! /usr/share/dotnet/dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App $DOTNET_CHANNEL"; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    sh /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --runtime aspnetcore --install-dir /usr/share/dotnet --architecture "$ARCH"
    ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
fi

echo "== Utilisateur et dossiers"
id deekeep >/dev/null 2>&1 || useradd --system --home-dir "$DATA" --shell /usr/sbin/nologin deekeep
mkdir -p "$PREFIX/tools" "$PREFIX/bin" "$DATA/backups" "$DATA/import" "$CONF" /var/cache/deekeep/catalog
chown -R deekeep:deekeep "$DATA" /var/cache/deekeep

echo "== Outils d'extraction (yt-dlp, deno)"
curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/latest/download/$YTDLP_ASSET" -o "$PREFIX/tools/yt-dlp.new"
chmod 755 "$PREFIX/tools/yt-dlp.new" && mv "$PREFIX/tools/yt-dlp.new" "$PREFIX/tools/yt-dlp"
if [ ! -x "$PREFIX/tools/deno" ]; then
    curl -fsSL "https://github.com/denoland/deno/releases/latest/download/$DENO_ASSET" -o /tmp/deno.zip
    unzip -o -q /tmp/deno.zip -d "$PREFIX/tools" && chmod 755 "$PREFIX/tools/deno"
fi
# yt-dlp se met à jour lui-même depuis l'application (page Diagnostic) : il doit pouvoir écrire son binaire.
chown deekeep:deekeep "$PREFIX/tools/yt-dlp"

echo "== Application"
systemctl stop deekeep.target 2>/dev/null || true
for svc in Gateway Catalog Library Downloader Quality Storage; do
    rm -rf "$PREFIX/$svc"
    cp -r "$HERE/app/$svc" "$PREFIX/$svc"
done
cp "$HERE"/bin/*.sh "$PREFIX/bin/" && chmod 755 "$PREFIX"/bin/*.sh
cp "$HERE/VERSION" "$PREFIX/VERSION"

echo "== Configuration"
if [ ! -f "$CONF/appsettings.json" ]; then
    cp "$HERE/config/appsettings.json" "$CONF/appsettings.json"
    echo "   -> $CONF/appsettings.json créé : adaptez PathMap (emplacement de la musique) et Storage:ArchiveRoot."
fi
if [ ! -f "$CONF/secrets.json" ]; then
    KEY=$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 40)
    cat > "$CONF/secrets.json" <<EOF
{
  "InternalKey": "$KEY",
  "Secrets": { "AcoustIdKey": "" }
}
EOF
    echo "   -> $CONF/secrets.json créé (clé interne aléatoire ; renseignez la clé AcoustID si vous en avez une)."
fi
chown -R root:deekeep "$CONF" && chmod 750 "$CONF" && chmod 640 "$CONF"/*.json
if [ ! -f /etc/caddy/Caddyfile.deekeep-installed ]; then
    cp "$HERE/config/Caddyfile" /etc/caddy/Caddyfile
    touch /etc/caddy/Caddyfile.deekeep-installed
    echo "   -> /etc/caddy/Caddyfile installé : choisissez le nom du site (voir commentaires)."
fi

if [ ! -f "$DATA/deekeep_cache.db" ]; then
    echo "   !! Copiez la base existante dans $DATA/deekeep_cache.db (puis chown deekeep:deekeep)."
    echo "      Sous Windows elle se trouve dans %APPDATA%\\DeeKeep\\deekeep_cache.db."
fi

echo "== Services"
cp "$HERE"/systemd/* /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now deekeep.target deekeep-health.timer deekeep-backup.timer
for svc in Catalog Library Downloader Quality Storage Gateway; do systemctl enable "deekeep@$svc" >/dev/null 2>&1; done
systemctl restart deekeep.target
systemctl reload caddy 2>/dev/null || systemctl restart caddy

echo ""
echo "DeeKeep $(cat "$PREFIX/VERSION") installé."
echo "État :       systemctl list-units 'deekeep*'"
echo "Journaux :   journalctl -u 'deekeep@*' -f"
echo "Ouvrez le site configuré dans Caddy : le premier accès demande de choisir le mot de passe."
