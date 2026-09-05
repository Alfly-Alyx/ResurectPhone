using System.Text;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9DebianArchiveAssemblerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ResurectPhone-deb-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Assemble_CreatesAValidDebianAr20Container()
    {
        Directory.CreateDirectory(_directory);
        var control = Path.Combine(_directory, "control.tar.gz");
        var data = Path.Combine(_directory, "data.tar.gz");
        var destination = Path.Combine(_directory, "calc.deb");
        await File.WriteAllBytesAsync(control, [0x1F, 0x8B, 1, 2, 3]);
        await File.WriteAllBytesAsync(data, [0x1F, 0x8B, 4, 5, 6, 7]);

        await N9DebianArchiveAssembler.AssembleAsync(control, data, destination);

        var archive = await File.ReadAllBytesAsync(destination);
        Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(archive, 0, 8));
        var members = ReadMembers(archive);
        Assert.Equal(["debian-binary/", "control.tar.gz/", "data.tar.gz/"],
            members.Select(member => member.Name));
        Assert.Equal("2.0\n", Encoding.ASCII.GetString(members[0].Content));
        Assert.Equal(await File.ReadAllBytesAsync(control), members[1].Content);
        Assert.Equal(await File.ReadAllBytesAsync(data), members[2].Content);
    }

    private static IReadOnlyList<ArMember> ReadMembers(byte[] archive)
    {
        var members = new List<ArMember>();
        var offset = 8;
        while (offset < archive.Length)
        {
            var header = Encoding.ASCII.GetString(archive, offset, 60);
            Assert.Equal("`\n", header[58..60]);
            var name = header[..16].TrimEnd();
            var length = int.Parse(header.Substring(48, 10).Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
            offset += 60;
            var content = archive.AsSpan(offset, length).ToArray();
            members.Add(new(name, content));
            offset += length + (length & 1);
        }
        return members;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed record ArMember(string Name, byte[] Content);
}
