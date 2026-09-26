using System.Security.Cryptography;
using System.Text;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed class N9OriginalPackageStore(string? root = null)
{
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ResurectPhone", "N9", "OriginalPackages");

    private string PackageDirectory(string package, string version)
    {
        if (!N9PackageMaintenancePolicy.IsSafePackageName(package)) throw new InvalidDataException("Identifiant de paquet invalide.");
        return Path.Combine(_root, package, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version))));
    }

    internal async Task<N9LocalPackage> SaveAsync(N9LocalPackage package, CancellationToken cancellationToken)
    {
        if (package.IsRebuiltBackup) throw new InvalidDataException("Une archive reconstruite ne peut pas être conservée comme paquet original.");
        var directory = PackageDirectory(package.Metadata.Package, package.Metadata.Version);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, package.Sha256 + ".deb");
        if (!Path.GetFullPath(package.Path).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var source = File.OpenRead(package.Path))
                await using (var target = File.Create(temporary)) await source.CopyToAsync(target, cancellationToken);
                var check = await N9LocalPackageReader.ReadAsync(temporary, cancellationToken);
                if (check.Sha256 != package.Sha256 || check.Metadata != package.Metadata || check.IsRebuiltBackup)
                    throw new InvalidDataException("Le paquet original a changé pendant sa conservation.");
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var saved = await N9LocalPackageReader.ReadAsync(destination, cancellationToken);
        if (saved.Sha256 != package.Sha256 || saved.IsRebuiltBackup) throw new InvalidDataException("Le paquet conservé est corrompu.");
        return saved;
    }

    internal async Task<N9LocalPackage?> FindAsync(string package, string version, string architecture, CancellationToken cancellationToken)
    {
        var directory = PackageDirectory(package, version);
        if (!Directory.Exists(directory)) return null;
        foreach (var path in Directory.EnumerateFiles(directory, "*.deb"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var candidate = await N9LocalPackageReader.ReadAsync(path, cancellationToken);
                if (!candidate.IsRebuiltBackup && candidate.Metadata.Package == package && candidate.Metadata.Version == version &&
                    candidate.Metadata.Architecture == architecture && Path.GetFileNameWithoutExtension(path).Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            catch (InvalidDataException) { }
        }
        return null;
    }
}
