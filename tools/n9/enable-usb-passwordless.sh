#!/bin/sh
# Run once as root on a Nokia N9 after enabling Developer mode and USB SDK.
set -eu

if [ "$(id -u)" != 0 ]; then
    echo "Ouvrez Terminal sur le N9, lancez devel-su, puis relancez ce script." >&2
    exit 1
fi

config=/etc/ssh/sshd_config
sshd=/usr/sbin/sshd
if [ ! -f "$config" ] || [ ! -x "$sshd" ]; then
    echo "Le serveur SSH du mode développeur est introuvable." >&2
    exit 1
fi
if ! id developer >/dev/null 2>&1; then
    echo "Le compte developer est introuvable." >&2
    exit 1
fi
configured=0
if grep -q 'ResurectPhone USB sans mot de passe' "$config"; then
    if grep -q '^PermitEmptyPasswords yes$' "$config" &&
       grep -q '^Match User developer Address 192.168.2.0/24$' "$config" &&
       grep -q '^Match User developer Address \*,!192.168.2.0/24$' "$config"; then
        configured=1
    else
        echo "Une ancienne préparation ResurectPhone existe : vérification manuelle nécessaire." >&2
        exit 1
    fi
elif grep -q '^[[:space:]]*Match[[:space:]]' "$config"; then
    echo "La configuration SSH contient déjà des règles Match : vérification manuelle nécessaire." >&2
    exit 1
fi

# OpenSSH 5.1 n'accepte pas PermitEmptyPasswords dans Match. Vérifier qu'aucun
# autre compte n'a de mot de passe vide avant de l'autoriser globalement.
if awk -F: '$2 == "" && $1 != "developer" { found=1 } END { exit found ? 1 : 0 }' /etc/shadow; then
    :
else
    echo "Un autre compte sans mot de passe existe : aucune modification effectuée." >&2
    exit 1
fi

backup="/var/tmp/resurectphone-ssh-$(date +%Y%m%d%H%M%S)"
umask 077
mkdir "$backup"
cp -p "$config" "$backup/sshd_config"
cp -p /etc/shadow "$backup/shadow"

temporary="$backup/sshd_config.new"
if [ "$configured" = 0 ]; then
    {
        printf '%s\n' 'PermitEmptyPasswords yes'
        cat "$config"
        printf '\n%s\n' '# ResurectPhone USB sans mot de passe'
        printf '%s\n' 'Match User developer Address 192.168.2.0/24'
        printf '%s\n' '    PasswordAuthentication yes'
        printf '%s\n' 'Match User developer Address *,!192.168.2.0/24'
        printf '%s\n' '    PasswordAuthentication no'
        printf '%s\n' '    KbdInteractiveAuthentication no'
    } > "$temporary"
else
    cp -p "$config" "$temporary"
fi

if ! "$sshd" -t -f "$temporary"; then
    echo "Configuration SSH refusée. Le N9 n’a pas été modifié." >&2
    exit 1
fi

if [ "$configured" = 0 ]; then
    cp -p "$temporary" "$config"
fi
if ! passwd -d developer; then
    cp -p "$backup/sshd_config" "$config"
    cp -p "$backup/shadow" /etc/shadow
    echo "Impossible de retirer le mot de passe developer : configuration restaurée." >&2
    exit 1
fi

if [ -s /var/run/sshd.pid ]; then
    kill -HUP "$(cat /var/run/sshd.pid)" || true
fi

echo "Accès developer sans mot de passe autorisé depuis le réseau USB (192.168.2.0/24)."
echo "Le compte developer n'accepte pas de mot de passe hors du sous-réseau USB. Sauvegarde : $backup"
echo "Branchez le N9 à un PC et ouvrez ResurectPhone pour créer automatiquement sa clé."
