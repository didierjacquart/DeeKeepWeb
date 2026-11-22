#!/bin/sh
# Envoi des e-mails (codes d'inscription et de mot de passe oublié, avis d'inscription) par
# un compte Gmail. Il faut un « mot de passe d'application » Google (validation en deux étapes
# activée, puis https://myaccount.google.com/apppasswords), saisi ici au clavier : jamais en
# argument ni dans l'historique. Un e-mail d'essai part vers l'adresse saisie.
#     sudo sh /opt/deekeep/bin/deekeep-mail-setup.sh
set -eu

CONF=/etc/deekeep/secrets.json
[ "$(id -u)" -eq 0 ] || { echo "À lancer avec sudo." >&2; exit 1; }
[ -f "$CONF" ] || { echo "Configuration absente ($CONF) : lancez d'abord install.sh." >&2; exit 1; }

# L'adresse se lit ici : Python reçoit son code par l'entrée standard, il ne peut pas y lire le clavier
# (le mot de passe, lui, passe par getpass, qui lit directement le terminal).
CURRENT=$(python3 -I -c 'import json,sys; print((json.load(open(sys.argv[1])).get("Mail") or {}).get("User") or "")' "$CONF")
printf "Adresse Gmail d'envoi [%s] : " "$CURRENT"
read -r ADDRESS < /dev/tty
ADDRESS=${ADDRESS:-$CURRENT}

python3 -I - "$CONF" "$ADDRESS" <<'PY'
import getpass, grp, json, os, smtplib, sys
from email.message import EmailMessage

path, user = sys.argv[1], sys.argv[2].strip()
with open(path, encoding="utf-8") as f:
    data = json.load(f)
if "@" not in user:
    sys.exit("Adresse invalide.")
pwd = getpass.getpass("Mot de passe d'application (16 lettres, saisie masquée) : ").replace(" ", "")
if len(pwd) < 8:
    sys.exit("Mot de passe d'application trop court.")

print("Essai d'envoi…")
msg = EmailMessage()
msg["From"] = f"DeeKeep <{user}>"
msg["To"] = user
msg["Subject"] = "DeeKeep : envoi des e-mails configuré"
msg.set_content("Les codes d'inscription et de mot de passe oublié partiront de cette adresse.")
try:
    with smtplib.SMTP("smtp.gmail.com", 587, timeout=20) as smtp:
        smtp.starttls()
        smtp.login(user, pwd)
        smtp.send_message(msg)
except Exception as e:
    sys.exit(f"Échec de l'envoi, rien n'est modifié : {e}")

data["Mail"] = {"Host": "smtp.gmail.com", "Port": 587, "User": user, "Password": pwd, "From": user, "FromName": "DeeKeep"}
tmp = path + ".new"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
os.chown(tmp, 0, grp.getgrnam("deekeep").gr_gid)
os.chmod(tmp, 0o640)
os.replace(tmp, path)
print(f"Envoi configuré depuis {user} ; un e-mail d'essai vient de partir.")
PY

systemctl restart deekeep@Gateway
echo "Passerelle redémarrée : inscription et mot de passe oublié sont disponibles."
