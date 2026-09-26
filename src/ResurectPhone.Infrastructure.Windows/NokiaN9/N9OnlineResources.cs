using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed record N9RepositoryProbe(string SourceLine, string Detail, bool Available);

internal static class N9OnlineResources
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 32 * 1024 * 1024 };

    public static async Task<IReadOnlyList<N9RepositoryProbe>> ProbeRepositoriesAsync(CancellationToken cancellationToken)
    {
        var roots = RepositorySources;

        return await Task.WhenAll(roots.Select(async item =>
        {
            try
            {
                using var response = await Client.GetAsync(item.IndexUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                using var stream = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var buffer = new char[256 * 1024];
                var count = await reader.ReadBlockAsync(buffer, cancellationToken);
                var text = new string(buffer, 0, count);
                var valid = text.Contains("Package: ", StringComparison.Ordinal) &&
                    (text.Contains("Architecture: armel", StringComparison.Ordinal) || text.Contains("Architecture: all", StringComparison.Ordinal));
                return new N9RepositoryProbe(item.SourceLine, item.IndexUrl + (valid ? " : index ARMEL accessible" : " : index incompatible"), valid);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new N9RepositoryProbe(item.SourceLine, item.IndexUrl + " : indisponible (réseau, certificat ou index)", false);
            }
        }));
    }

    public static readonly (string SourceLine, string IndexUrl)[] RepositorySources =
    [
        ("deb http://wunderwungiel.pl/MeeGo/n9mirror/001 ./", "https://wunderwungiel.pl/MeeGo/n9mirror/001/Packages.gz"),
        ("deb http://wunderwungiel.pl/MeeGo/n9mirror/apps ./", "https://wunderwungiel.pl/MeeGo/n9mirror/apps/Packages.gz"),
        ("deb http://wunderwungiel.pl/MeeGo/n9mirror/tools ./", "https://wunderwungiel.pl/MeeGo/n9mirror/tools/Packages.gz"),
        ("deb http://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/ harmattan/sdk free non-free", "https://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/dists/harmattan/sdk/free/binary-armel/Packages.gz"),
        ("deb http://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/ harmattan/sdk free non-free", "https://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/dists/harmattan/sdk/non-free/binary-armel/Packages.gz")
    ];

    public static async Task<string> DownloadWarehouseAsync(CancellationToken cancellationToken)
    {
        const string url = "https://openrepos.net/sites/default/files/packages/1/warehouse_0.1.9_armel.deb";
        const string hash = "46B35D0322FB4829E8DFE386C67E4FC11DD2302A3349020C6AE5DB8BDB8F84CA";
        var bytes = await Client.GetByteArrayAsync(url, cancellationToken);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Le paquet Warehouse a changé depuis sa vérification.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResurectPhone", "N9", "Downloads");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "warehouse-0.1.9-armel.deb");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        var package = await N9LocalPackageReader.ReadAsync(path, cancellationToken);
        if (package.Metadata.Package != "warehouse" || package.Metadata.Version != "0.1.9")
            throw new InvalidDataException("Identité du paquet Warehouse inattendue.");
        return path;
    }

    public static async Task<string> DownloadMeeShopAsync(CancellationToken cancellationToken)
    {
        const string url = "https://openrepos.net/sites/default/files/packages/19569/meeshop_0.8_armel.deb";
        const string expectedHash = "0D44B74CDEE588FA73DDAAAFB2D922D167D8C21947F6630DFCB0E3806D34EA35";
        var bytes = await Client.GetByteArrayAsync(url, cancellationToken);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expectedHash)
            throw new InvalidDataException("Le paquet MeeShop GUI a changé depuis sa vérification. Installation interrompue.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResurectPhone", "N9", "Downloads");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "meeshop-gui-0.8-armel.deb");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        var package = await N9LocalPackageReader.ReadAsync(path, cancellationToken);
        if (package.Metadata.Package != "meeshop" || package.Metadata.Version != "0.8")
            throw new InvalidDataException("Identité du paquet MeeShop GUI inattendue.");
        return path;
    }
}
