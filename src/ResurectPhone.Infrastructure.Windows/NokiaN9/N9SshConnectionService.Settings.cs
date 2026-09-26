namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private static string BackupPreamble(string backup, string restore) =>
        "set -eu\numask 077\nbackup=" + Quote(backup) + "\nmkdir -p \"$backup\"\n" +
        "cat > \"$backup/restore.sh\" <<'RESURECTPHONE_RESTORE'\n#!/bin/sh\nset -eu\nPATH=/bin:/sbin:/usr/bin:/usr/sbin\nexport PATH\ncd \"$(dirname \"$0\")\"\n" +
        restore + "\necho 'Réglages précédents restaurés.'\nRESURECTPHONE_RESTORE\nchmod 700 \"$backup/restore.sh\"\n";

    private const string SettingsRollback = """
        changed=0
        finished=0
        finish() {
            result=$?
            if [ "$changed" = 1 ] && [ "$finished" = 0 ]; then
                sh "$backup/restore.sh" >&2 || echo 'Restauration automatique incomplète.' >&2
            fi
            exit "$result"
        }
        trap finish 0
        trap 'exit 1' HUP INT TERM
        """;

    internal static string BuildSettingsScript(string backup, bool includeGps)
    {
        var restore = """
            for name in Maps.conf Drive.ini; do
                target="/home/user/.config/Nokia/$name"
                if [ -f "./$name.saved" ]; then
                    cat "./$name.saved" | devel-su user -c "cat > $target"
                elif [ -f "./$name.absent" ]; then
                    devel-su user -c "rm -f $target"
                fi
            done
            """;
        if (includeGps) restore += "\nif [ -f ./location-settings.conf ]; then cat ./location-settings.conf > /etc/xdg/nokia/location-settings.conf; fi\n";
        var prepare = """
            command -v python >/dev/null
            command -v devel-su >/dev/null
            for name in Maps.conf Drive.ini; do
                target="/home/user/.config/Nokia/$name"
                test ! -L "$target"
                if [ -f "$target" ]; then
                    devel-su user -c "cat $target" > "$backup/$name.saved"
                else
                    touch "$backup/$name.absent"
                fi
            done
            """;
        if (includeGps) prepare += "\ntest -f /etc/xdg/nokia/location-settings.conf\ncp -p /etc/xdg/nokia/location-settings.conf \"$backup/location-settings.conf\"\n";
        var mutate = "\nchanged=1\ndevel-su user -c " + Quote("/usr/bin/python -c " + Quote(EditNokiaSettingsPython)) + "\n";
        if (includeGps) mutate += """
            sed 's/^PrimarySuplServer=.*/PrimarySuplServer=supl.google.com/;s/^SecondarySuplServer=.*/SecondarySuplServer=supl.google.com/' "$backup/location-settings.conf" > "$backup/location-settings.new"
            grep -q '^PrimarySuplServer=supl.google.com$' "$backup/location-settings.new"
            grep -q '^SecondarySuplServer=supl.google.com$' "$backup/location-settings.new"
            cat "$backup/location-settings.new" > /etc/xdg/nokia/location-settings.conf
            grep -q '^PrimarySuplServer=supl.google.com$' /etc/xdg/nokia/location-settings.conf
            echo 'Assistance GPS configurée sur supl.google.com. Réception satellite à tester en extérieur.'
            """;
        return BackupPreamble(backup, restore) + prepare + "\n" + SettingsRollback + mutate +
            "\nfinished=1\nprintf 'RESURECTPHONE_BACKUP=%s\\n' \"$backup\"\n";
    }

    private const string EditNokiaSettingsPython = """
        import os, stat
        directory = '/home/user/.config/Nokia'
        if not os.path.isdir(directory): os.makedirs(directory)
        def change(name, section, key, value):
            path = directory + '/' + name
            if os.path.islink(path): raise IOError('Symbolic link refused')
            mode = stat.S_IMODE(os.stat(path).st_mode) if os.path.exists(path) else 0600
            lines = open(path).read().splitlines(True) if os.path.exists(path) else []
            output = []
            active = False
            found_section = False
            found_key = False
            for line in lines:
                stripped = line.strip()
                if stripped.startswith('[') and stripped.endswith(']'):
                    if active and not found_key:
                        output.append(key + '=' + value + '\n')
                        found_key = True
                    active = stripped == '[' + section + ']'
                    found_section = found_section or active
                if active and '=' in line and line.split('=',1)[0].strip() == key:
                    output.append(key + '=' + value + '\n')
                    found_key = True
                else:
                    output.append(line if line.endswith('\n') else line + '\n')
            if not found_section: output.append('\n[' + section + ']\n')
            if not found_key: output.append(key + '=' + value + '\n')
            temporary = path + '.resurectphone-new'
            if os.path.lexists(temporary): raise IOError('Temporary file already exists')
            handle = open(temporary, 'w')
            try:
                handle.write(''.join(output))
            finally:
                handle.close()
            os.chmod(temporary, mode)
            os.rename(temporary, path)
            if key + '=' + value not in open(path).read(): raise IOError('Verification failed')
        change('Maps.conf', 'General', 'isSsoEnabled', 'False')
        change('Drive.ini', 'Generalsettings', 'ssoDone', 'True')
        print('Demandes de compte desactivees dans Cartes et Drive.')
        """;

    internal static string BuildRepositoryScript(string backup, IEnumerable<string> sources, string aptOptions = "")
    {
        const string target = "/etc/apt/sources.list.d/resurectphone.list";
        var restore = "if [ -f ./sources.saved ]; then cat ./sources.saved > " + target +
            "; elif [ -f ./sources.absent ]; then rm -f " + target + "; fi";
        return BackupPreamble(backup, restore) +
            "if test -f " + target + "; then cat " + target + " > \"$backup/sources.saved\"; else touch \"$backup/sources.absent\"; fi\n" +
            SettingsRollback + "\nchanged=1\ncat > " + target + " <<'RESURECTPHONE_SOURCES'\n" + string.Join('\n', sources) +
            "\nRESURECTPHONE_SOURCES\nchmod 644 " + target +
            "\napt-get -o Dir::Etc::sourcelist=" + target + " -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=false" + aptOptions + " update > \"$backup/update.log\" 2>&1\ncat \"$backup/update.log\"\n" +
            "if grep -qE '^(Err |W: Failed to fetch|W: GPG error|E:)' \"$backup/update.log\"; then echo 'Index non entièrement validés.' >&2; exit 1; fi" +
            "\nfinished=1\necho 'Sources ResurectPhone enregistrées et index actualisés. Les anciennes sources sont conservées, mais exclues de cette actualisation. Aucun paquet mis à jour automatiquement.'\n";
    }

}
