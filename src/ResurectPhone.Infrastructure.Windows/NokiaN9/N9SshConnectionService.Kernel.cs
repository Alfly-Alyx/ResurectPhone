using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private readonly SemaphoreSlim _kernelLock = new(1, 1);

    // Preparation only: this method reads the stock partition, copies files and
    // downloads recovery assets. It never flashes, extracts modules into /lib,
    // changes the running kernel or launches the downloaded Nokia executable.
    public async Task<N9KernelPreparation> PrepareKernelAsync(IProgress<N9OperationProgress>? progress = null,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default)
    {
        var acquired = false;
        try
        {
            await _kernelLock.WaitAsync(cancellationToken);
            acquired = true;
            progress?.Report(new("Identification du N9 et vérification de l’accès administrateur…"));
            var details = await ReadDeviceDetailsAsync(cancellationToken);
            await WithMaintenanceClientAsync((client, _) => WithAdministratorAsync(client, administratorPassword?.ToArray(),
                _ => Task.FromResult(true), cancellationToken), cancellationToken);
            var assets = await N9KernelAssets.PrepareAsync(details, progress, cancellationToken);
            return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword, async password =>
            {
                var preflight = await RunMaintenanceScriptAsync(client,
                    "set -e\ntest \"$(uname -r)\" = '2.6.32.54-dfl61-20121301'\n" +
                    "test \"$(readlink /lib/modules/current)\" = '2.6.32.54-dfl61-20121301'\n" +
                    "grep -q '^mtd2: 01000000 .*\"kernel\"$' /proc/mtd\n" +
                    "sed -n 's/.*g_nokia.iSerialNumber=\\([0-9]*\\).*/\\1/p' /proc/cmdline", null, cancellationToken);
                RequireSuccess(preflight, "Le noyau ou le partitionnement actuel ne correspond pas au N9 prévu pour ce parcours.");
                var serial = preflight.Output.Trim();
                if (!Regex.IsMatch(serial, "^[0-9]{15}$", RegexOptions.CultureInvariant))
                    throw new N9ConnectionException("L’identité USB du N9 n’a pas été confirmée.");
                var id = Guid.NewGuid().ToString("N");
                var remoteBackup = "/var/lib/resurectphone/kernel-backups/" + id;
                var localBackup = Path.Combine(assets.Directory, "Sauvegardes", id);
                Directory.CreateDirectory(localBackup);
                progress?.Report(new("Sauvegarde de la partition noyau et des modules d’origine…"));
                var backup = await RunMaintenanceScriptAsync(client,
                    "set -e\numask 022\nmkdir -p " + remoteBackup + "\n" +
                    "dd if=/dev/mtd2 of=" + remoteBackup + "/kernel-partition.bin bs=65536 count=256\n" +
                    "test \"$(wc -c < " + remoteBackup + "/kernel-partition.bin)\" -eq 16777216\n" +
                    "tar czf " + remoteBackup + "/modules-stock.tar.gz -C /lib/modules 2.6.32.54-dfl61-20121301\n" +
                    "chmod 755 " + remoteBackup + "\nchmod 644 " + remoteBackup + "/*\nmd5sum " + remoteBackup + "/*", password, cancellationToken);
                RequireSuccess(backup, "La sauvegarde du noyau d’origine a échoué.");
                using var transfer = new SftpClient(CreateKeyConnectionInfo(pairing));
                PinHostKey(transfer, pairing);
                await ConnectAsync(transfer, cancellationToken);
                foreach (var name in new[] { "kernel-partition.bin", "modules-stock.tar.gz" })
                {
                    var path = Path.Combine(localBackup, name);
                    using (var output = File.Create(path)) await Task.Run(() => transfer.DownloadFile(remoteBackup + "/" + name, output), cancellationToken);
                    using var input = File.OpenRead(path);
                    var md5 = Convert.ToHexString(await MD5.HashDataAsync(input, cancellationToken));
                    if (!backup.Output.Contains(md5.ToLowerInvariant() + "  " + remoteBackup + "/" + name, StringComparison.Ordinal))
                        throw new InvalidDataException("La sauvegarde transférée diffère de celle du N9.");
                }
                var backupImage = Path.Combine(localBackup, "kernel-partition.bin");
                using var original = File.OpenRead(backupImage);
                var backupHash = Convert.ToHexString(await SHA256.HashDataAsync(original, cancellationToken));
                var remote = "/home/user/MyDocs/ResurectPhone/Kernels/kernel-plus-20131128";
                progress?.Report(new("Copie de kernel-plus dans le dossier dédié du N9…"));
                var directory = await RunMaintenanceScriptAsync(client, "mkdir -p " + remote, null, cancellationToken);
                RequireSuccess(directory, "Le dossier noyau du N9 n’est pas accessible.");
                foreach (var path in new[] { assets.Archive, assets.Image })
                {
                    using var input = File.OpenRead(path);
                    await Task.Run(() => transfer.UploadFile(input, remote + "/" + Path.GetFileName(path)), cancellationToken);
                }
                var hashes = await RunMaintenanceScriptAsync(client, "md5sum " + remote + "/" + N9KernelAssets.ArchiveName + " " + remote + "/" + N9KernelAssets.ImageName, null, cancellationToken);
                RequireSuccess(hashes, "Le contrôle du transfert noyau a échoué.");
                foreach (var path in new[] { assets.Archive, assets.Image })
                {
                    using var input = File.OpenRead(path);
                    var hash = Convert.ToHexString(await MD5.HashDataAsync(input, cancellationToken));
                    if (!hashes.Output.Contains(hash.ToLowerInvariant() + "  " + remote + "/" + Path.GetFileName(path), StringComparison.Ordinal))
                        throw new InvalidDataException("Le noyau transféré diffère du fichier vérifié sur le PC.");
                }
                var prepared = new N9KernelPreparation(assets.Directory, remote, details.SalesCode, details.KernelVersion,
                    assets.Image, assets.Firmware, assets.Flasher, backupImage, backupHash, pairing.HostKeySha256, serial);
                await File.WriteAllTextAsync(Path.Combine(localBackup, "preparation.json"), JsonSerializer.Serialize(prepared, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                progress?.Report(new("Noyau téléchargé, sauvegarde et transferts vérifiés.", 100));
                return prepared;
            }, cancellationToken), cancellationToken);
        }
        finally
        {
            if (administratorPassword is not null) Array.Clear(administratorPassword);
            if (acquired) _kernelLock.Release();
        }
    }
}
