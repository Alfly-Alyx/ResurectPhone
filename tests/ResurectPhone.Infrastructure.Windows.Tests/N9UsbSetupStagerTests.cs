using System.IO;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9UsbSetupStagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ResurectPhone-N9-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StagesOnlyOnTheN9VolumeAndCanBeRepeated()
    {
        Directory.CreateDirectory(_directory);
        var script = "#!/bin/sh\necho ready\n"u8.ToArray();
        var other = Path.Combine(_directory, "other");
        var n9 = Path.Combine(_directory, "n9");
        Directory.CreateDirectory(other);
        Directory.CreateDirectory(n9);
        var volumes = new[]
        {
            new N9StorageVolume(other, "USB DATA", DriveType.Removable),
            new N9StorageVolume(n9, "Nokia N9", DriveType.Fixed)
        };

        var first = N9UsbSetupStager.TryStage(volumes, script);
        var second = N9UsbSetupStager.TryStage(volumes, script);

        Assert.Equal(N9UsbSetupStageStatus.Ready, first.Status);
        Assert.Equal(N9UsbSetupStageStatus.Ready, second.Status);
        var setupDirectory = Path.Combine(n9, N9UsbSetupStager.DirectoryName);
        Assert.Equal(script, File.ReadAllBytes(Path.Combine(setupDirectory, N9UsbSetupStager.FileName)));
        Assert.Empty(Directory.EnumerateFiles(other));
        Assert.Single(Directory.EnumerateFiles(setupDirectory));
    }

    [Fact]
    public void ExistingDifferentFileIsPreserved()
    {
        Directory.CreateDirectory(_directory);
        var setupDirectory = Path.Combine(_directory, N9UsbSetupStager.DirectoryName);
        Directory.CreateDirectory(setupDirectory);
        var path = Path.Combine(setupDirectory, N9UsbSetupStager.FileName);
        File.WriteAllText(path, "user data");

        var result = N9UsbSetupStager.TryStage(
            [new N9StorageVolume(_directory, "Nokia N9", DriveType.Removable)],
            "replacement"u8.ToArray());

        Assert.Equal(N9UsbSetupStageStatus.Conflict, result.Status);
        Assert.Equal("user data", File.ReadAllText(path));
    }

    [Fact]
    public void AmbiguousVolumesAreNotModified()
    {
        Directory.CreateDirectory(_directory);
        var one = Path.Combine(_directory, "one");
        var two = Path.Combine(_directory, "two");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);

        var result = N9UsbSetupStager.TryStage(
            [new N9StorageVolume(one, "Nokia N9", DriveType.Removable),
             new N9StorageVolume(two, "Nokia N9", DriveType.Removable)],
            "script"u8.ToArray());

        Assert.Equal(N9UsbSetupStageStatus.Unavailable, result.Status);
        Assert.Empty(Directory.EnumerateFiles(one));
        Assert.Empty(Directory.EnumerateFiles(two));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
