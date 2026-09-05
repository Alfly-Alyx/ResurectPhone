using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal static partial class N9PackageCommands
{
    public static IReadOnlyList<string> VerifiedMirrorSourceLines { get; } =
    [
        "deb http://mirror.thecust.net/harmattan-dev.nokia.com/ ./",
        "deb http://coderus.openrepos.net/n9mirro/ ./"
    ];

    public static string ExportScript => NormalizeLf(ExportScriptSource);

    public static string BuildInspectControlCommand(string remotePackagePath) =>
        $"busybox ar -p '{ValidateRemotePath(remotePackagePath)}' control.tar.gz | " +
        "busybox tar -xzOf - ./control";

    public static string BuildInstallCommand(
        string remotePackagePath,
        N9DebPackageMetadata metadata,
        N9DebBackupAssessment? rebuiltBackup = null)
    {
        if (!N9PackageMaintenancePolicy.IsSafePackageName(metadata.Package) ||
            metadata.Architecture is not ("armel" or "all") ||
            string.IsNullOrWhiteSpace(metadata.Version))
            throw new InvalidDataException("Le paquet n’a pas passé la validation Harmattan obligatoire.");
        if (rebuiltBackup is not null && !rebuiltBackup.CanOfferAutomaticRestore)
            throw new InvalidOperationException(rebuiltBackup.Detail);
        return $"dpkg -i '{ValidateRemotePath(remotePackagePath)}'";
    }

    public static string BuildStandardRemovalCommand(N9DebPackageMetadata metadata)
    {
        ThrowIfProtected(metadata);
        return $"apt-get -y remove {metadata.Package}";
    }

    public static string BuildExpertRemovalCommand(
        N9DebPackageMetadata metadata,
        bool expertForceConfirmed)
    {
        ThrowIfProtected(metadata);
        if (!expertForceConfirmed)
            throw new InvalidOperationException("La suppression forcée nécessite une confirmation explicite du mode expert.");
        return $"dpkg --force-depends --remove {metadata.Package}";
    }

    public static string WrapWithDevelSu(string adminCommand)
    {
        if (string.IsNullOrWhiteSpace(adminCommand) || adminCommand.Contains('\''))
            throw new ArgumentException("La commande administrateur N9 est invalide.", nameof(adminCommand));
        return $"devel-su -c '{adminCommand}'";
    }

    public static byte[] EncodeAdminPassword(char[] passwordCharacters)
    {
        ArgumentNullException.ThrowIfNull(passwordCharacters);
        if (passwordCharacters.Length == 0)
            throw new ArgumentException("Le mot de passe administrateur est vide.", nameof(passwordCharacters));
        try
        {
            return Encoding.UTF8.GetBytes(passwordCharacters);
        }
        finally
        {
            Array.Clear(passwordCharacters);
        }
    }

    public static async Task SendPasswordToStandardInputAsync(
        Stream standardInput,
        byte[] passwordBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        ArgumentNullException.ThrowIfNull(passwordBytes);
        if (passwordBytes.Length == 0)
            throw new ArgumentException("Le mot de passe administrateur est vide.", nameof(passwordBytes));

        try
        {
            await standardInput.WriteAsync(passwordBytes, cancellationToken);
            await standardInput.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await standardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static string NormalizeLf(string script) =>
        script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string ValidateRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains("..", StringComparison.Ordinal) ||
            !SafeRemotePathRegex().IsMatch(path))
            throw new ArgumentException("Le chemin temporaire du paquet N9 est invalide.", nameof(path));
        return path;
    }

    private static void ThrowIfProtected(N9DebPackageMetadata metadata)
    {
        var reason = N9PackageMaintenancePolicy.GetRemovalBlockReason(metadata);
        if (reason is not null)
            throw new InvalidOperationException(reason);
    }

    private const string ExportScriptSource = """
        #!/bin/sh
        set -eu
        package="$1"
        work="$2"
        control_archive="$3"
        data_archive="$4"
        case "$work" in /var/tmp/resurectphone-*) ;; *) exit 64 ;; esac
        list="/var/lib/dpkg/info/$package.list"
        test -r "$list"
        rm -rf "$work"
        mkdir -p "$work/control" "$work/data"
        while IFS= read -r path; do
            case "$path" in
                /|.|'') continue ;;
                /*)
                    destination="$work/data$path"
                    if test -d "$path" && ! test -L "$path"; then
                        mkdir -p "$destination"
                    elif test -f "$path" || test -L "$path"; then
                        mkdir -p "$(dirname "$destination")"
                        cp -a "$path" "$destination"
                    fi
                    ;;
            esac
        done < "$list"
        for suffix in conffiles preinst postinst prerm postrm config templates triggers md5sums; do
            source="/var/lib/dpkg/info/$package.$suffix"
            if test -e "$source"; then cp -a "$source" "$work/control/$suffix"; fi
        done
        dpkg-query -W -f='Package: ${Package}\nVersion: ${Version}\nArchitecture: ${Architecture}\nMaintainer: ${Maintainer}\nSection: ${Section}\nPriority: ${Priority}\nEssential: ${Essential}\nPre-Depends: ${Pre-Depends}\nDepends: ${Depends}\nRecommends: ${Recommends}\nSuggests: ${Suggests}\nConflicts: ${Conflicts}\nReplaces: ${Replaces}\nProvides: ${Provides}\nDescription: ${Description}\n' "$package" > "$work/control/control"
        (cd "$work/control" && busybox tar -czf "$control_archive" .)
        (cd "$work/data" && busybox tar -czf "$data_archive" .)
        chmod 0644 "$control_archive" "$data_archive"
        """;

    [GeneratedRegex("^/[A-Za-z0-9_./-]+$")]
    private static partial Regex SafeRemotePathRegex();
}
