#!/bin/sh
# ResurectPhone: persistent passwordless SDK access (USB, optionally Wi-Fi).
set -eu
PATH=/bin:/sbin:/usr/bin:/usr/sbin
LC_ALL=C
export PATH LC_ALL

test "$(id -u)" = 0 || { echo "Les droits administrateur du N9 sont nécessaires." >&2; exit 1; }
config=/etc/ssh/sshd_config
sshd=/usr/sbin/sshd
work=/var/lib/resurectphone
# No argument preserves the selected scope during automatic USB preparation.
scope=${1:-preserve}
case "$scope" in
    preserve) if [ -f "$work/wifi-sdk-enabled" ]; then scope=usb-wifi; else scope=usb; fi ;;
    usb|usb-wifi) ;;
    *) echo "Portee SDK inconnue." >&2; exit 1 ;;
esac
wifi_auth=no
if [ "$scope" = usb-wifi ]; then wifi_auth=yes; fi
test -f "$config"
test -x "$sshd"
command -v inotifywait >/dev/null 2>&1 || {
    echo "L'outil inotifywait est nécessaire pour conserver le réglage USB." >&2
    exit 1
}
id developer >/dev/null 2>&1

configured=0
if grep -q '^# ResurectPhone USB access v4$' "$config"; then
    configured=1
elif grep -qi '^[[:space:]]*Match[[:space:]]' "$config"; then
    echo "Des règles SSH personnalisées existent : vérification nécessaire." >&2
    exit 1
fi

# Harmattan stores the developer password in passwd; some variants use shadow.
accounts=/etc/passwd
if [ -f /etc/shadow ]; then accounts="$accounts /etc/shadow"; fi
if ! awk -F: '$2 == "" && $1 != "developer" { found=1 } END { exit found ? 1 : 0 }' $accounts; then
    echo "Un autre compte possède un mot de passe vide : aucune modification effectuée." >&2
    exit 1
fi

umask 077
mkdir -p "$work/backups"
chmod 755 "$work"
backup="$work/backups/usb-$(date +%Y%m%d%H%M%S)-$$"
mkdir "$backup"
cp -p "$config" "$backup/sshd_config"
cp -p /etc/passwd "$backup/passwd"
if [ -f /etc/shadow ]; then cp -p /etc/shadow "$backup/shadow"; fi
if [ -f "$work/usb-enabled" ]; then touch "$backup/was-enabled"; fi
if [ -f "$work/wifi-sdk-enabled" ]; then touch "$backup/was-wifi-enabled"; fi

cat > "$backup/restore.sh" <<'RESTORE'
#!/bin/sh
set -eu
PATH=/bin:/sbin:/usr/bin:/usr/sbin
export PATH
cd "$(dirname "$0")"
initctl stop resurectphone-usb-access >/dev/null 2>&1 || true
cp -p ./sshd_config /etc/ssh/sshd_config
rm -f /var/lib/resurectphone/usb-enabled /var/lib/resurectphone/wifi-sdk-enabled
if [ -f ./was-wifi-enabled ]; then touch /var/lib/resurectphone/wifi-sdk-enabled; fi
# Restore only developer's password, preserving changes to other accounts.
restore_password() {
    awk -F: 'NR == FNR { if ($1 == "developer") password=$2; next }
        { if ($1 == "developer") $2=password; print }' OFS=: "$1" "$2" > ./account.restore
    cat ./account.restore > "$2"
    rm -f ./account.restore
}
restore_password ./passwd /etc/passwd
if [ -f ./shadow ]; then restore_password ./shadow /etc/shadow; fi
if [ -f ./was-enabled ]; then
    touch /var/lib/resurectphone/usb-enabled
    initctl start resurectphone-usb-access
fi
sshd -t
if [ -s /var/run/sshd.pid ]; then kill -HUP "$(cat /var/run/sshd.pid)"; fi
echo "Configuration USB précédente restaurée."
RESTORE
chmod 700 "$backup/restore.sh"

if [ "$configured" = 0 ]; then
    {
        # OpenSSH 5.1 only accepts PermitEmptyPasswords in the global section.
        printf '%s\n' 'PermitEmptyPasswords yes' 'PermitRootLogin no'
        cat "$config"
        printf '\n%s\n' '# ResurectPhone USB access v4'
        printf '%s\n' 'Match User developer Address 192.168.2.0/24'
        printf '%s\n' '    PasswordAuthentication yes'
        printf '%s\n' 'Match User developer Address *,!192.168.2.0/24'
        printf '%s\n' '    PasswordAuthentication no'
        printf '%s\n' '    KbdInteractiveAuthentication no'
    } > "$backup/sshd_config.new"
else
    cp -p "$config" "$backup/sshd_config.new"
fi
# Rewrite only our managed non-USB block; keep all administrator rules intact.
awk -v allow="$wifi_auth" '
    /^# ResurectPhone USB access v4$/ { managed=1 }
    managed && /^Match User developer Address \*,!192[.]168[.]2[.]0\/24$/ { wifi=1; print; next }
    wifi && /^Match / { wifi=0 }
    wifi && /^[[:space:]]*PasswordAuthentication / { print "    PasswordAuthentication " allow; next }
    { print }
' "$backup/sshd_config.new" > "$backup/sshd_config.scoped"
mv "$backup/sshd_config.scoped" "$backup/sshd_config.new"

cat > "$backup/watcher.new" <<'WATCHER'
#!/bin/sh
set -eu
PATH=/bin:/sbin:/usr/bin:/usr/sbin
LC_ALL=C
export PATH LC_ALL
test -f /var/lib/resurectphone/usb-enabled || exit 0
ensure_empty() {
    storage=/etc/passwd
    if [ -f /etc/shadow ] &&
       awk -F: '$1 == "developer" && $2 == "x" { found=1 } END { exit found ? 0 : 1 }' /etc/passwd; then
        storage=/etc/shadow
    fi
    if ! awk -F: '$1 == "developer" && $2 == "" { empty=1 } END { exit empty ? 0 : 1 }' "$storage"; then
        passwd -d developer >/dev/null
    fi
}
ensure_empty
# The startup event closes the race between the first check and watch setup.
# No polling: the process sleeps until SDK changes an account file.
inotifywait -m -e close_write,moved_to,create --format '%f' /etc 2>&1 |
while IFS= read -r event; do
    case "$event" in
        'Watches established.'|passwd|shadow) ensure_empty ;;
    esac
done
# Let Upstart restart the watcher if its event stream ends.
exit 1
WATCHER

cat > "$backup/job.new" <<'JOB'
description "ResurectPhone N9 USB access"
start on started ssh
stop on stopping ssh
respawn
respawn limit 5 60
exec /bin/sh /usr/lib/resurectphone/maintain-usb-access.sh
JOB

"$sshd" -t -f "$backup/sshd_config.new"
"$sshd" -T -f "$backup/sshd_config.new" -C user=developer,host=usb-client,addr=192.168.2.14 |
    grep -q '^passwordauthentication yes$'
"$sshd" -T -f "$backup/sshd_config.new" -C user=developer,host=other-client,addr=192.168.3.14 |
    grep -q "^passwordauthentication $wifi_auth\$"
sh -n "$backup/watcher.new"

# Aegis protects /etc/init: package our own files with reference hashes.
# Build ar + BusyBox tar directly; Harmattan dpkg-deb --build requires GNU tar.
mkdir -p "$backup/data/usr/lib/resurectphone" "$backup/data/etc/init" "$backup/control"
cp "$backup/watcher.new" "$backup/data/usr/lib/resurectphone/maintain-usb-access.sh"
cp "$backup/job.new" "$backup/data/etc/init/resurectphone-usb-access.conf"
chmod 755 "$backup/data/usr/lib/resurectphone/maintain-usb-access.sh"
chmod 644 "$backup/data/etc/init/resurectphone-usb-access.conf"
chmod 755 "$backup/data" "$backup/data/usr" "$backup/data/usr/lib" \
    "$backup/data/usr/lib/resurectphone" "$backup/data/etc" "$backup/data/etc/init"
cat > "$backup/control/control" <<'CONTROL'
Package: resurectphone-n9
Version: 0.1.2
Architecture: all
Maintainer: ResurectPhone
Priority: optional
Section: misc
Description: Automatic USB access for ResurectPhone
CONTROL
(
    cd "$backup/data"
    for file in usr/lib/resurectphone/maintain-usb-access.sh etc/init/resurectphone-usb-access.conf; do
        hash=$(sha1sum "$file" | cut -d ' ' -f 1)
        # SDK refhashmake format; dpkg assigns the actual installation origin.
        printf 'S 15 com.nokia.maemo H 40 %s R %s %s\n' "$hash" "${#file}" "$file"
    done
) > "$backup/control/digsigsums"
(cd "$backup/control" && busybox tar -czf "$backup/control.tar.gz" .)
(cd "$backup/data" && busybox tar -czf "$backup/data.tar.gz" .)
printf '2.0\n' > "$backup/debian-binary"
(
    cd "$backup"
    printf '!<arch>\n'
    for file in debian-binary control.tar.gz data.tar.gz; do
        size=$(wc -c < "$file" | tr -d ' ')
        printf '%-16s%-12s%-6s%-6s%-8s%-10s`\n' "$file" 0 0 0 100644 "$size"
        cat "$file"
        if [ $((size % 2)) = 1 ]; then printf '\n'; fi
    done
) > "$backup/resurectphone-n9.deb"

changed=0
finished=0
finish() {
    result=$?
    if [ "$changed" = 1 ] && [ "$finished" = 0 ]; then
        set +e
        sh "$backup/restore.sh" >&2
        echo "Préparation interrompue : configuration restaurée." >&2
    fi
    exit "$result"
}
trap finish 0
trap 'exit 1' HUP INT TERM
changed=1
initctl stop resurectphone-usb-access >/dev/null 2>&1 || true
dpkg -i "$backup/resurectphone-n9.deb"
cat "$backup/sshd_config.new" > "$config"
touch "$work/usb-enabled"
if [ "$scope" = usb-wifi ]; then touch "$work/wifi-sdk-enabled"; else rm -f "$work/wifi-sdk-enabled"; fi
initctl reload-configuration
initctl start resurectphone-usb-access
passwd -d developer >/dev/null
"$sshd" -t
test -s /var/run/sshd.pid
kill -HUP "$(cat /var/run/sshd.pid)"
initctl status resurectphone-usb-access | grep -q 'start/running'
printf '%s\n' "$backup" > "$work/latest-usb-backup"
finished=1
printf 'RESURECTPHONE_BACKUP=%s\n' "$backup"
printf 'RESURECTPHONE_SDK_SCOPE=%s\n' "$scope"
echo 'RESURECTPHONE_USB_READY'
