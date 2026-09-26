namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    // Older installers sometimes left this file owned by nobody. Harmattan root
    // lacks DAC override, so preserve ownership and write as its existing owner.
    private const string LegacyRepositoryWriter = """
        write_legacy_source() {
            target=/etc/apt/sources.list.d/n9repomirror.list
            test -f "$target" && test ! -L "$target" || return 1
            owner=$(python -c 'import os; print(os.stat("/etc/apt/sources.list.d/n9repomirror.list").st_uid)')
            test "$owner" = "$1" || return 1
            case "$owner" in
                0) cat > "$target" ;;
                29999) devel-su user -c "cat > $target" ;;
                65534) devel-su nobody -c "cat > $target" ;;
                *) echo 'Propriétaire du dépôt non pris en charge.' >&2; return 1 ;;
            esac
        }
        """;

    internal const string PrepareLegacyRepositoriesPython = """
        import os, sys, stat
        backup = sys.argv[1]
        path = '/etc/apt/sources.list.d/n9repomirror.list'
        if os.path.lexists(path):
            info = os.lstat(path)
            if not stat.S_ISREG(info.st_mode) or info.st_size > 65536:
                raise IOError('Unexpected legacy repository file')
            original = open(path, 'rb').read()
            output = []
            changed = False
            for line in original.splitlines(True):
                fields = line.split()
                url = fields[1] if len(fields) >= 2 else ''
                old = url.rstrip('/') in (
                    'http://mirror.thecust.net/harmattan-dev.nokia.com',
                    'http://coderus.openrepos.net/n9mirro',
                    'http://coderus.openrepos.net/n9mirror',
                    'https://coderus.openrepos.net/n9mirror')
                if fields and fields[0] in ('deb', 'deb-src') and old:
                    output.append('# ResurectPhone: utilise maintenant resurectphone.list\n# ' + line)
                    changed = True
                else:
                    output.append(line)
            if changed:
                if info.st_uid not in (0, 29999, 65534): raise IOError('Unsupported repository owner')
                for name, data in [('legacy.saved', original), ('legacy.new', ''.join(output)), ('legacy.owner', str(info.st_uid))]:
                    handle = open(backup + '/' + name, 'wb')
                    try: handle.write(data)
                    finally: handle.close()
        """;

    internal static string BuildRepositoryScript(string backup, IEnumerable<string> sources, string aptOptions = "")
    {
        const string target = "/etc/apt/sources.list.d/resurectphone.list";
        const string legacy = "/etc/apt/sources.list.d/n9repomirror.list";
        var restore = LegacyRepositoryWriter + "\nif [ -f ./sources.saved ]; then cat ./sources.saved > " + target +
            "; elif [ -f ./sources.absent ]; then rm -f " + target + "; fi\n" +
            "if test -f ./legacy.saved; then cat ./legacy.saved | write_legacy_source \"$(cat ./legacy.owner)\"; cmp -s ./legacy.saved " + legacy + "; fi";
        return BackupPreamble(backup, restore) +
            "test ! -L " + target + "\n" + LegacyRepositoryWriter + "\n" +
            "if test -f " + target + "; then cat " + target + " > \"$backup/sources.saved\"; else touch \"$backup/sources.absent\"; fi\n" +
            "python -c " + Quote(PrepareLegacyRepositoriesPython) + " \"$backup\"\n" +
            SettingsRollback + "\nchanged=1\ncat > " + target + " <<'RESURECTPHONE_SOURCES'\n" + string.Join('\n', sources) +
            "\nRESURECTPHONE_SOURCES\nchmod 644 " + target + "\n" +
            "if test -f \"$backup/legacy.new\"; then cat \"$backup/legacy.new\" | write_legacy_source \"$(cat \"$backup/legacy.owner\")\"; cmp -s \"$backup/legacy.new\" " + legacy + "; fi\n" +
            "apt-get -o Dir::Etc::sourcelist=" + target + " -o Dir::Etc::sourceparts=- -o APT::Get::List-Cleanup=false" + aptOptions + " update > \"$backup/update.log\" 2>&1\ncat \"$backup/update.log\"\n" +
            "if grep -qE '^(Err |W: Failed to fetch|W: GPG error|E:)' \"$backup/update.log\"; then echo 'Index non entièrement validés.' >&2; exit 1; fi" +
            "\nfinished=1\necho 'Sources ResurectPhone enregistrées et index actualisés. Les anciennes adresses connues de N9 RepoMirror ont été désactivées avec sauvegarde. Les autres dépôts restent inchangés. Aucun paquet mis à jour automatiquement.'\n";
    }
}
