#!/bin/sh
# Mot de passe perdu : redonne un mot de passe à un compte existant, saisi au
# clavier (jamais en argument ni dans l'historique). Le compte garde son
# identifiant, donc sa bibliothèque personnelle ; il est aussi réactivé.
# Même haché que la passerelle (Auth.HashPassword : PBKDF2-SHA256, 210 000 tours).
#     sudo sh /opt/deekeep/bin/deekeep-reset-password.sh [nom]   (défaut : deekeep)
set -eu

DATA=${DEEKEEP_DATA:-/mnt/deekeep-db}
DB="$DATA/deekeep_web.db"
NAME=${1:-deekeep}

[ -f "$DB" ] || { echo "Base introuvable : $DB (partage NAS monté ?)" >&2; exit 1; }

# La passerelle est arrêtée le temps de l'écriture (base sur partage SMB).
systemctl stop deekeep@Gateway
trap 'systemctl start deekeep@Gateway' EXIT

sudo -u deekeep python3 -I - "$DB" "$NAME" <<'PY'
import base64, getpass, hashlib, os, sqlite3, sys

db, name = sys.argv[1], sys.argv[2]
conn = sqlite3.connect(db, timeout=30)
row = conn.execute("SELECT name, role FROM users WHERE name = ? COLLATE NOCASE", (name,)).fetchone()
if row is None:
    known = ", ".join(r[0] for r in conn.execute("SELECT name FROM users ORDER BY name"))
    sys.exit(f"Compte « {name} » introuvable. Comptes : {known or 'aucun'}")
print(f"Compte « {row[0]} » ({row[1]})")
pwd = getpass.getpass("Nouveau mot de passe : ")
if len(pwd) < 8 or not pwd.strip():
    sys.exit("Le mot de passe doit compter au moins 8 caractères.")
if getpass.getpass("Confirmer : ") != pwd:
    sys.exit("Les deux saisies diffèrent.")
iterations = 210_000
salt = os.urandom(16)
digest = hashlib.pbkdf2_hmac("sha256", pwd.encode("utf-8"), salt, iterations, 32)
stored = "$".join(["pbkdf2-sha256", str(iterations),
                   base64.b64encode(salt).decode(), base64.b64encode(digest).decode()])
with conn:
    conn.execute("UPDATE users SET password_hash = ?, disabled = 0 WHERE name = ? COLLATE NOCASE", (stored, name))
print("Mot de passe changé.")
PY
