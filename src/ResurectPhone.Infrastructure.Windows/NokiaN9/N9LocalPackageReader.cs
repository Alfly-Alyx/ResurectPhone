using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal static class N9LocalPackageReader
{
    public static async Task<N9LocalPackage> ReadAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        await using var source = File.OpenRead(path);
        if (source.Length is < 68 or > 1024L * 1024 * 1024)
            throw new InvalidDataException("Le paquet .deb est vide ou trop volumineux (maximum 1 Go).");
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
        source.Position = 0;
        var magic = new byte[8];
        await source.ReadExactlyAsync(magic, cancellationToken);
        if (!magic.AsSpan().SequenceEqual("!<arch>\n"u8))
            throw new InvalidDataException("Ce fichier n’est pas une archive Debian.");
        string? control = null;
        var dataFound = false;
        var controlFound = false;
        var binaryFound = false;
        while (source.Position < source.Length)
        {
            var header = new byte[60];
            await source.ReadExactlyAsync(header, cancellationToken);
            if (header[58] != '`' || header[59] != '\n' ||
                !long.TryParse(Encoding.ASCII.GetString(header, 48, 10).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var size) || size < 0 || size > source.Length - source.Position)
                throw new InvalidDataException("L’en-tête Debian est invalide.");
            var name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            var next = source.Position + size + (size & 1);
            if (name == "debian-binary")
            {
                if (binaryFound || size != 4) throw new InvalidDataException("Version Debian invalide.");
                var version = new byte[4];
                await source.ReadExactlyAsync(version, cancellationToken);
                if (!version.AsSpan().SequenceEqual("2.0\n"u8)) throw new InvalidDataException("Debian 2.0 requis.");
                binaryFound = true;
            }
            else if (name == "control.tar.gz")
            {
                if (controlFound || size > 32 * 1024 * 1024)
                    throw new InvalidDataException("Archive de contrôle absente ou trop volumineuse.");
                controlFound = true;
                var bytes = new byte[(int)size];
                await source.ReadExactlyAsync(bytes, cancellationToken);
                using var compressed = new MemoryStream(bytes);
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                // Bound decompression before handing it to the tar parser.
                using var tarBytes = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await gzip.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (tarBytes.Length + count > 32 * 1024 * 1024)
                        throw new InvalidDataException("Archive de contrôle décompressée trop volumineuse.");
                    tarBytes.Write(buffer, 0, count);
                }
                tarBytes.Position = 0;
                using var tar = new TarReader(tarBytes);
                TarEntry? entry;
                while ((entry = tar.GetNextEntry()) is not null)
                {
                    if (entry.Name is not ("control" or "./control")) continue;
                    if (control is not null || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null || entry.Length > 1024 * 1024)
                        throw new InvalidDataException("Fichier control invalide.");
                    using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
                    control = await reader.ReadToEndAsync(cancellationToken);
                }
            }
            else if (name == "data.tar.gz")
            {
                if (dataFound) throw new InvalidDataException("Membre Debian data dupliqué.");
                dataFound = true;
            }
            else if (name.StartsWith("data.tar", StringComparison.Ordinal) || name.StartsWith("control.tar", StringComparison.Ordinal))
                throw new InvalidDataException("Harmattan exige un paquet utilisant control.tar.gz et data.tar.gz.");
            if (next > source.Length) throw new InvalidDataException("Archive Debian tronquée.");
            source.Position = next;
        }
        if (!binaryFound || !dataFound || control is null)
            throw new InvalidDataException("Le paquet ne contient pas tous les membres Debian requis.");
        return new(path, N9PackageMaintenancePolicy.ParseControl(control), sha256,
            Regex.IsMatch(control, @"^X-ResurectPhone-Backup:[ \t]*DebianArchiveOnly[ \t]*\r?$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
