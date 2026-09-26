using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ResurectPhone.Core.Devices;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9MaintenanceTests
{
    [Fact]
    public void TlsReportDoesNotExposeSessionSecrets()
    {
        var report = N9SshConnectionService.TlsPublicSummary("OpenSSL 1.0.2u\n    Protocol  : TLSv1.2\n    Cipher    : AES\n    Master-Key: secret\n    Session-ID: session\n    Verify return code: 0 (ok)\n");
        Assert.Contains("TLSv1.2", report);
        Assert.Contains("Verify return code: 0", report);
        Assert.DoesNotContain("secret", report);
        Assert.DoesNotContain("session", report);
    }

    [Fact]
    public void EveryN9CardHasAMaintenanceRoute()
    {
        var cards = RecoveryCatalog.Features.Where(feature => feature.SupportedPlatforms.Contains(PhonePlatform.MeeGoHarmattan)).Select(feature => feature.Id);
        Assert.True(N9MaintenanceCatalog.FeatureIds.SetEquals(cards));
    }

    [Fact]
    public void InventoryKeepsOnlyInstalledApplicationsAndPreservesProtection()
    {
        var applications = N9SshConnectionService.ParseApplications(
            "PKG\tcalc\t1.2\tarmel\tno\toptional\tcom.nokia.maemo\tinstall ok installed\n" +
            "PKG\tcore\t1.0\tarmel\tyes\trequired\tcom.nokia.maemo\tinstall ok installed\n" +
            "PKG\tremoved\t1.0\tarmel\tno\toptional\t\tdeinstall ok config-files\n" +
            "APP\tcalc\tCalculator\t/usr/share/applications/calc.desktop\n" +
            "APP\tcalc\tDuplicate\t/usr/share/applications/calc2.desktop\n" +
            "APP\tcore\tCore\t/usr/share/applications/core.desktop\n" +
            "APP\tremoved\tGone\t/usr/share/applications/gone.desktop\n");
        Assert.Equal(2, applications.Count);
        Assert.Null(applications.Single(app => app.Metadata.Package == "calc").RemovalBlock);
        Assert.NotNull(applications.Single(app => app.Metadata.Package == "core").RemovalBlock);
    }

    [Theory]
    [InlineData(0, "Remv calc [1.0]\n", false)]
    [InlineData(0, "Remv calc [1.0]\nRemv harmattan [1.0]\n", true)]
    [InlineData(100, " meta: Depends: calc but it is not going to be installed\nE: Unmet dependencies. Try apt-get -f install", true)]
    [InlineData(100, "E: Could not get lock /var/lib/dpkg/lock\n", false)]
    [InlineData(100, " other: Depends: unrelated\nE: Broken packages\n", false)]
    [InlineData(100, " other: Depends: calc-extra\nE: Broken packages\n", false)]
    public void ExpertRemovalRequiresAConfirmedDependencyBlock(int status, string output, bool expected) =>
        Assert.Equal(expected, N9SshConnectionService.IsDependencyRemovalBlock(status, output, "calc"));

    [Theory]
    [InlineData("http://wunderwungiel.pl/MeeGo/n9mirror/001/Packages.gz", "https://wunderwungiel.pl/MeeGo/n9mirror/001/Packages.gz")]
    [InlineData("http://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/pool/a.deb", "https://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/pool/a.deb")]
    [InlineData("http://127.0.0.1/private", null)]
    [InlineData("http://wunderwungiel.pl/MeeGo/n9mirror/../../../private", null)]
    [InlineData("http://wunderwungiel.pl/MeeGo/n9mirror/%2fprivate", null)]
    [InlineData("http://user:password@wunderwungiel.pl/MeeGo/n9mirror/file", null)]
    [InlineData("http://wunderwungiel.pl:8080/MeeGo/n9mirror/file", null)]
    [InlineData("http://wunderwungiel.pl/MeeGo/n9mirror/file?target=private", null)]
    public void UsbRelayOnlyReachesThePublishedMirrors(string request, string? expected) =>
        Assert.Equal(expected, N9RepositoryRelay.ResolveUpstream(request)?.AbsoluteUri);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DebianReaderRecognizesBackupEvenAfterFileRename(bool backup)
    {
        var path = await MakePackageAsync("Package: calc\nVersion: 1.2\nArchitecture: armel\n" +
            (backup ? "X-ResurectPhone-Backup: DebianArchiveOnly\n" : ""));
        try
        {
            var package = await N9LocalPackageReader.ReadAsync(path, CancellationToken.None);
            Assert.Equal("calc", package.Metadata.Package);
            Assert.Equal(backup, package.IsRebuiltBackup);
            Assert.Equal(64, package.Sha256.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DuplicateControlFieldsCannotOverridePackageIdentity()
    {
        var path = await MakePackageAsync("Package: calc\nPackage: essential-system\nVersion: 1\nArchitecture: armel\n");
        try { await Assert.ThrowsAsync<InvalidDataException>(() => N9LocalPackageReader.ReadAsync(path, CancellationToken.None)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BackupMarkerAcceptsDebianFieldCasingAndWhitespace()
    {
        var path = await MakePackageAsync("Package: calc\nVersion: 1\nArchitecture: armel\nx-resurectphone-backup:\tDebianArchiveOnly\n");
        try { Assert.True((await N9LocalPackageReader.ReadAsync(path, CancellationToken.None)).IsRebuiltBackup); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WrongArchitectureIsRejectedBeforeSsh()
    {
        var path = await MakePackageAsync("Package: calc\nVersion: 1\nArchitecture: amd64\n");
        try { await Assert.ThrowsAsync<InvalidDataException>(() => N9LocalPackageReader.ReadAsync(path, CancellationToken.None)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TruncatedMemberIsRejected()
    {
        var path = await MakePackageAsync("Package: calc\nVersion: 1\nArchitecture: all\n");
        try
        {
            using (var file = File.OpenWrite(path)) file.SetLength(file.Length - 4);
            await Assert.ThrowsAsync<InvalidDataException>(() => N9LocalPackageReader.ReadAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TlsRollbackManifestCoversEveryReplacedSystemPackage()
    {
        Assert.Equal(16, N9TlsManifest.Packages.Length);
        Assert.Equal(13, N9TlsManifest.Packages.Count(package => package.OriginalSha256?.Length == 64));
        Assert.Equal(new[] { "libaccounts-glib-tools", "openssl-local", "wunderw-perl-opt" },
            N9TlsManifest.Packages.Where(package => package.OriginalSha256 is null).Select(package => package.Package).Order());
    }

    private static async Task<string> MakePackageAsync(string control)
    {
        var path = Path.Combine(Path.GetTempPath(), "n9-reader-" + Guid.NewGuid().ToString("N") + ".deb");
        await using var output = File.Create(path);
        await output.WriteAsync("!<arch>\n"u8.ToArray());
        foreach (var member in new[] { ("debian-binary", "2.0\n"u8.ToArray()), ("control.tar.gz", TarGzip("control", control)), ("data.tar.gz", TarGzip("sample", "test")) })
        {
            var header = member.Item1.PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "100644".PadRight(8) + member.Item2.Length.ToString().PadRight(10) + "`\n";
            await output.WriteAsync(Encoding.ASCII.GetBytes(header));
            await output.WriteAsync(member.Item2);
            if (member.Item2.Length % 2 != 0) await output.WriteAsync("\n"u8.ToArray());
        }
        return path;
    }

    private static byte[] TarGzip(string name, string contents)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar, true))
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(contents));
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
        }
        return bytes.ToArray();
    }
}
