using System.IO;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public enum N9UsbSetupStageStatus
{
    Unavailable,
    Ready,
    Conflict,
    Error
}

public sealed record N9UsbSetupStageResult(N9UsbSetupStageStatus Status, string? Path = null);

internal sealed record N9StorageVolume(string RootPath, string Label, DriveType DriveType);

public static class N9UsbSetupStager
{
    public const string DirectoryName = "ResurectPhone";
    public const string FileName = "resurectphone-usb-setup.sh";

    public static N9UsbSetupStageResult TryStage(byte[] script)
    {
        var volumes = new List<N9StorageVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                    volumes.Add(new N9StorageVolume(drive.RootDirectory.FullName, drive.VolumeLabel, drive.DriveType));
            }
            catch (IOException)
            {
                // Le téléphone peut changer de mode pendant l'énumération.
            }
            catch (UnauthorizedAccessException)
            {
                // Un autre lecteur n'est pas accessible par cette session.
            }
        }

        return TryStage(volumes, script);
    }

    internal static N9UsbSetupStageResult TryStage(IEnumerable<N9StorageVolume> volumes, byte[] script)
    {
        var matches = volumes.Where(volume =>
            volume.DriveType is DriveType.Removable or DriveType.Fixed &&
            string.Equals(volume.Label.Trim(), "Nokia N9", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            return new N9UsbSetupStageResult(N9UsbSetupStageStatus.Unavailable);

        var directory = System.IO.Path.Combine(matches[0].RootPath, DirectoryName);
        var path = System.IO.Path.Combine(directory, FileName);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                return new N9UsbSetupStageResult(
                    existing.AsSpan().SequenceEqual(script)
                        ? N9UsbSetupStageStatus.Ready
                        : N9UsbSetupStageStatus.Conflict,
                    path);
            }

            temporary = System.IO.Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, script);
            File.Move(temporary, path);
            return new N9UsbSetupStageResult(N9UsbSetupStageStatus.Ready, path);
        }
        catch (IOException)
        {
            return new N9UsbSetupStageResult(N9UsbSetupStageStatus.Error, path);
        }
        catch (UnauthorizedAccessException)
        {
            return new N9UsbSetupStageResult(N9UsbSetupStageStatus.Error, path);
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
