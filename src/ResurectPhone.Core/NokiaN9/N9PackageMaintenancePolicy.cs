using System.Text.RegularExpressions;

namespace ResurectPhone.Core.NokiaN9;

public sealed record N9DebPackageMetadata(
    string Package,
    string Version,
    string Architecture,
    string Essential = "",
    string Priority = "",
    string AegisOrigin = "",
    bool IsUserVisible = false);

public enum N9DebRestoreConfidence
{
    OriginalPackage,
    AegisOriginPreserved,
    DebianArchiveOnly
}

public sealed record N9DebBackupAssessment(
    N9DebPackageMetadata Metadata,
    N9DebRestoreConfidence RestoreConfidence,
    bool CanOfferAutomaticRestore,
    string Detail);

public static partial class N9PackageMaintenancePolicy
{
    public const string CompanionPackage = "resurectphone-n9";

    private static readonly IReadOnlySet<string> SupportedArchitectures =
        new HashSet<string>(StringComparer.Ordinal) { "armel", "all" };

    public static bool IsSafePackageName(string packageName) =>
        !string.IsNullOrWhiteSpace(packageName) &&
        packageName.Length <= 128 &&
        DebianPackageNameRegex().IsMatch(packageName);

    public static bool IsProtectedPackage(N9DebPackageMetadata metadata) =>
        metadata.Package.Equals(CompanionPackage, StringComparison.Ordinal) ||
        metadata.Essential.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        metadata.Priority.Trim().Equals("required", StringComparison.OrdinalIgnoreCase) ||
        metadata.Priority.Trim().Equals("important", StringComparison.OrdinalIgnoreCase);

    public static string? GetRemovalBlockReason(N9DebPackageMetadata metadata)
    {
        if (!IsSafePackageName(metadata.Package))
            return "L’identifiant du paquet est invalide.";
        if (metadata.Package.Equals(CompanionPackage, StringComparison.Ordinal))
            return "Le compagnon ResurectPhone est nécessaire pour gérer le Nokia N9.";
        if (!metadata.IsUserVisible)
            return "Ce paquet n’a pas été identifié comme une application visible et ne peut pas être supprimé depuis ResurectPhone.";
        if (metadata.Essential.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase))
            return "Ce paquet est déclaré essentiel par Harmattan.";
        if (metadata.Priority.Trim().Equals("required", StringComparison.OrdinalIgnoreCase) ||
            metadata.Priority.Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
            return "Ce paquet fait partie des composants requis ou importants d’Harmattan.";
        return null;
    }

    public static N9DebPackageMetadata ParseControl(string control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        var fields = ParseDebianFields(control);
        var package = Required(fields, "Package");
        var version = Required(fields, "Version");
        var architecture = Required(fields, "Architecture").ToLowerInvariant();

        if (!IsSafePackageName(package) || version.Length > 256 || version.Any(char.IsControl))
            throw new InvalidDataException("Les métadonnées du paquet Debian sont invalides.");
        if (!SupportedArchitectures.Contains(architecture))
            throw new InvalidDataException("Ce paquet n’est pas prévu pour l’architecture ARMEL du Nokia N9.");

        return new(
            package,
            version,
            architecture,
            fields.GetValueOrDefault("Essential", string.Empty),
            fields.GetValueOrDefault("Priority", string.Empty),
            fields.GetValueOrDefault("Aegis-Origin", string.Empty));
    }

    public static N9DebBackupAssessment AssessRebuiltBackup(
        N9DebPackageMetadata metadata,
        bool aegisOriginPreservationVerified = false,
        bool reinstallationValidatedOnN9 = false)
    {
        if (!aegisOriginPreservationVerified || string.IsNullOrWhiteSpace(metadata.AegisOrigin))
        {
            return new(
                metadata,
                N9DebRestoreConfidence.DebianArchiveOnly,
                false,
                "La sauvegarde est lisible comme paquet Debian, mais sa provenance Aegis n’est pas préservée par une méthode vérifiée. Sa réinstallation automatique reste bloquée.");
        }

        return new(
            metadata,
            N9DebRestoreConfidence.AegisOriginPreserved,
            reinstallationValidatedOnN9,
            reinstallationValidatedOnN9
                ? "La provenance Aegis et le cycle de réinstallation ont été validés sur Nokia N9."
                : "La provenance Aegis paraît préservée, mais la réinstallation automatique reste bloquée jusqu’à un essai matériel concluant.");
    }

    private static Dictionary<string, string> ParseDebianFields(string control)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;
        foreach (var line in control.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
            {
                if (fields.Count > 0)
                    break;
                continue;
            }

            if (char.IsWhiteSpace(line[0]) && currentName is not null)
            {
                fields[currentName] += "\n" + line.TrimStart();
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new InvalidDataException("Le fichier control du paquet Debian est mal formé.");
            currentName = line[..separator].Trim();
            fields[currentName] = line[(separator + 1)..].Trim();
        }
        return fields;
    }

    private static string Required(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Le paquet Debian ne contient pas le champ {name}.");

    [GeneratedRegex("^[a-z0-9][a-z0-9+.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DebianPackageNameRegex();
}
