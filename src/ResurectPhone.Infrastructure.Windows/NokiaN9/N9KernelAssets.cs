using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed record N9FirmwareAsset(string Name, long Size, string Sha1);
internal sealed record N9KernelFiles(string Directory, string Archive, string Image, string Firmware, string Flasher, N9FirmwareAsset Stock);

internal static class N9KernelAssets
{
    internal const string Version = "2.6.32.61-plus";
    internal const string ArchiveName = "linux_2.6.32.61-plus-20131128.tar.gz";
    internal const string ImageName = "zImage_2.6.32.61-plus_20131128";
    internal const string ArchiveHash = "6B069F05C42F0375C546201D6835B8A35399B9644A5F764E90569032967104BA";
    internal const string ImageHash = "F7ECAeca6F5D85F06613A7371F4DB967B7D5D580F97D46095915A64AE4E53282";
    internal const string FlasherHash = "CD591783C59D59E53334E1F58556FF8854D31A74C98C0A98AD1366C9445FBD26";
    private const string TableHash = "E429E53F970BF04614BEB49432DFA4C0E873B074E1F5C0A3BA4536C5E30B8464";
    private const string BaseUrl = "https://archive.org/download/n9-drivers-fw/";
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    internal static readonly N9FirmwareAsset Stock005 = new(
        "DFL61_HARMATTAN_40.2012.21-3_PR_LEGACY_005-OEM1-958_ARM.bin", 1248012278,
        "31395779C12B40E0C8C2BDC66C3C1EDDE91D61DF");

    internal static N9FirmwareAsset ResolveFirmware(N9DeviceDetails details, string table)
    {
        if (details.ProductCode != "RM-696" || details.SystemBuild != "DFL61_HARMATTAN_40.2012.21-3_PR_005" ||
            !Regex.IsMatch(details.SalesCode, "^[A-Z0-9]{7}$", RegexOptions.CultureInvariant))
            throw new N9ConnectionException("La préparation automatique de kernel-plus exige actuellement un N9 RM-696 sous PR1.3 variante 005. La variante de ce téléphone n’a pas encore de ROM de récupération validée dans le catalogue.");
        var rows = table.Split('\n').Select(line => line.Trim().Split(',')).Where(row => row.Length == 6 && row[0] == details.SalesCode).ToArray();
        if (rows.Length != 1 || rows[0][1] != "005" || rows[0][4] != Stock005.Name)
            throw new N9ConnectionException("La table Nokia ne confirme pas la ROM de récupération pour ce code produit.");
        return Stock005;
    }

    internal static async Task<N9KernelFiles> PrepareAsync(N9DeviceDetails details, IProgress<N9OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ResurectPhone", "N9", "Kernels", "kernel-plus-20131128");
        Directory.CreateDirectory(directory);
        var table = await DownloadAsync(directory, "N9-variant-table-PR13.txt", BaseUrl + "N9-variant-table-PR13.txt", 48387, TableHash, HashAlgorithmName.SHA256, progress, cancellationToken);
        var stock = ResolveFirmware(details, await File.ReadAllTextAsync(table, cancellationToken));
        var archive = await DownloadAsync(directory, ArchiveName, BaseUrl + ArchiveName, 4090372, ArchiveHash, HashAlgorithmName.SHA256, progress, cancellationToken);
        var kernel = ReadKernelArchive(archive);
        var image = Path.Combine(directory, ImageName);
        await File.WriteAllBytesAsync(image, kernel, cancellationToken);
        var flasher = await DownloadAsync(directory, "flasher.exe", "https://coderus.openrepos.net/flasher/WinFlasher_3.12.1.exe", 342096,
            FlasherHash, HashAlgorithmName.SHA256, progress, cancellationToken);
        var firmware = await DownloadAsync(directory, stock.Name, BaseUrl + stock.Name, stock.Size, stock.Sha1, HashAlgorithmName.SHA1, progress, cancellationToken);
        return new(directory, archive, image, firmware, flasher, stock);
    }

    internal static byte[] ReadKernelArchive(string path)
    {
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(ArchiveHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’archive kernel-plus ne correspond pas à la publication vérifiée.");
        file.Position = 0;
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        byte[]? image = null;
        var modules = 0;
        long total = 0;
        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (!seen.Add(name) || entry.Length > 4 * 1024 * 1024 || (total += entry.Length) > 32 * 1024 * 1024)
                throw new InvalidDataException("Structure de l’archive noyau inattendue.");
            if (entry.EntryType == TarEntryType.Directory && new[] { "boot", "lib", "lib/modules", "lib/modules/" + Version }.Contains(name.TrimEnd('/'))) continue;
            if (name == "lib/modules/current" && entry.EntryType == TarEntryType.SymbolicLink && entry.LinkName == Version) continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                throw new InvalidDataException("Type d’entrée non autorisé dans l’archive noyau.");
            using var bytes = new MemoryStream();
            entry.DataStream.CopyTo(bytes);
            var data = bytes.ToArray();
            if (name == "boot/" + ImageName)
            {
                ValidateZImage(data);
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(ImageHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("L’image kernel-plus a changé.");
                image = data;
            }
            else
            {
                if (!Regex.IsMatch(name, @"^lib/modules/2\.6\.32\.61-plus/[A-Za-z0-9_.+-]+\.ko$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Chemin inattendu dans les modules noyau.");
                if (data.Length < 52 || !data.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }) ||
                    BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18, 2)) != 40 ||
                    !Encoding.Latin1.GetString(data).Contains("vermagic=" + Version + " ", StringComparison.Ordinal))
                    throw new InvalidDataException("Un module ne correspond pas au noyau ARM prévu.");
                modules++;
            }
        }
        if (image is null || modules != 99) throw new InvalidDataException("L’archive doit contenir l’image et ses 99 modules.");
        return image;
    }

    internal static void ValidateZImage(byte[] data)
    {
        if (data.Length < 48 || data.Length > 16 * 1024 * 1024 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(36, 4)) != 0x016f2818 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(44, 4)) - BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(40, 4)) != data.Length)
            throw new InvalidDataException("Image de noyau ARM tronquée ou invalide.");
    }

    internal static async Task VerifyFileAsync(string path, string expected, HashAlgorithmName algorithm, CancellationToken cancellationToken)
    {
        using var file = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[1024 * 1024];
        int count;
        while ((count = await file.ReadAsync(buffer, cancellationToken)) > 0) hash.AppendData(buffer, 0, count);
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Empreinte incorrecte : " + Path.GetFileName(path));
    }

    private static async Task<string> DownloadAsync(string directory, string name, string url, long size, string hash,
        HashAlgorithmName algorithm, IProgress<N9OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, name);
        if (File.Exists(path) && new FileInfo(path).Length == size)
        {
            try { await VerifyFileAsync(path, hash, algorithm, cancellationToken); return path; }
            catch (InvalidDataException) { }
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(60));
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Téléchargement redirigé hors HTTPS.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[1024 * 1024];
                long total = 0;
                int count;
                var lastProgress = DateTime.MinValue;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > size) throw new InvalidDataException("Taille inattendue : " + name);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if ((DateTime.UtcNow - lastProgress).TotalMilliseconds > 300)
                    {
                        progress?.Report(new($"Téléchargement de {name} — {total / 1048576} / {size / 1048576} Mo", total * 100d / size));
                        lastProgress = DateTime.UtcNow;
                    }
                }
                if (total != size) throw new InvalidDataException("Téléchargement incomplet : " + name);
            }
            progress?.Report(new("Vérification de " + name + "…"));
            await VerifyFileAsync(temporary, hash, algorithm, timeout.Token);
            File.Move(temporary, path, true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
