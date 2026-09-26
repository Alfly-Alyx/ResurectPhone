using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed record N9TlsPrepared(string Directory, IReadOnlyDictionary<string, string> Hashes);

internal static class N9TlsDownloads
{
    public static async Task<N9TlsPrepared> PrepareAsync(CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResurectPhone", "N9", "TLS-1.2-0.0.2");
        Directory.CreateDirectory(Path.Combine(root, "patched"));
        Directory.CreateDirectory(Path.Combine(root, "original"));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3), MaxResponseContentBufferSize = 40 * 1024 * 1024 };
        var bundlePath = Path.Combine(root, "bundle.tar.gz");
        await DownloadVerifiedAsync(http, N9TlsManifest.BundleUrl, bundlePath, N9TlsManifest.BundleSha256, cancellationToken);
        await using (var compressed = File.OpenRead(bundlePath))
        await using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
        await using (var tar = new TarReader(gzip))
        {
            var found = new HashSet<string>();
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(cancellationToken: cancellationToken)) is not null)
            {
                var filename = entry.Name.Split('/')[^1];
                var expected = N9TlsManifest.Packages.SingleOrDefault(package => package.Filename == filename);
                if (expected is null) continue;
                if (!found.Add(filename) || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                    entry.DataStream is null || entry.Length > 32 * 1024 * 1024)
                    throw new InvalidDataException("Contenu du correctif TLS inattendu.");
                var path = Path.Combine(root, "patched", filename);
                await using (var file = File.Create(path)) await entry.DataStream.CopyToAsync(file, cancellationToken);
                var actual = await N9LocalPackageReader.ReadAsync(path, cancellationToken);
                if (actual.Metadata.Package != expected.Package || actual.Metadata.Version != expected.Version || actual.IsRebuiltBackup)
                    throw new InvalidDataException("Identité d’un paquet TLS inattendue.");
                hashes.Add("patched/" + filename, actual.Sha256);
            }
            if (found.Count != N9TlsManifest.Packages.Length) throw new InvalidDataException("Correctif TLS incomplet.");
        }
        foreach (var package in N9TlsManifest.Packages.Where(package => package.OriginalSha256 is not null))
        {
            var path = Path.Combine(root, "original", package.Filename);
            await DownloadVerifiedAsync(http, "https://wunderwungiel.pl/MeeGo/n9mirror/001/" + Uri.EscapeDataString(package.Filename),
                path, package.OriginalSha256!, cancellationToken);
            var metadata = (await N9LocalPackageReader.ReadAsync(path, cancellationToken)).Metadata;
            if (metadata.Package != package.Package || metadata.Version != package.Version)
                throw new InvalidDataException("Le paquet Nokia de restauration n’a pas l’identité attendue.");
            hashes.Add("original/" + package.Filename, package.OriginalSha256!);
        }
        return new N9TlsPrepared(root, hashes);
    }

    private static async Task DownloadVerifiedAsync(HttpClient client, string url, string path, string hash, CancellationToken token)
    {
        if (File.Exists(path))
        {
            await using var existing = File.OpenRead(path);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing, token)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return;
        }
        var bytes = await client.GetByteArrayAsync(url, token);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’empreinte du téléchargement TLS ou du paquet de restauration a changé.");
        await File.WriteAllBytesAsync(path, bytes, token);
    }
}
