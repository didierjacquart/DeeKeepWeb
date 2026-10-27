#!/bin/sh
# Contrôle de santé : un service qui tourne mais ne répond plus (blocage,
# épuisement mémoire sans plantage) est relancé après trois échecs de suite.
# Les compteurs vivent dans /run (remis à zéro au redémarrage de la machine).
set -u

STATE=/run/deekeep-health
mkdir -p "$STATE"

check() {
    name=$1
    port=$2
    if curl -fsS --max-time 10 "http://127.0.0.1:$port/health/live" >/dev/null 2>&1; then
        rm -f "$STATE/$name"
        return
    fi
    # Un service arrêté volontairement n'est pas relancé par ce contrôle.
    # (return 0 : sinon le contrôle lui-même finirait en échec.)
    systemctl is-active --quiet "deekeep@$name" || systemctl is-failed --quiet "deekeep@$name" || return 0
    count=$(( $(cat "$STATE/$name" 2>/dev/null || echo 0) + 1 ))
    echo "$count" > "$STATE/$name"
    if [ "$count" -ge 3 ]; then
        logger -t deekeep-health "deekeep@$name ne répond plus depuis $count contrôles : redémarrage"
        systemctl reset-failed "deekeep@$name" 2>/dev/null
        systemctl restart "deekeep@$name"
        rm -f "$STATE/$name"
    fi
}

check Catalog 5101
check Library 5102
check Downloader 5103
check Quality 5104
check Storage 5105
check Gateway 5000
