using System.Text;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal static class N9DebianArchiveAssembler
{
    private static readonly byte[] ArchiveMagic = "!<arch>\n"u8.ToArray();
    private static readonly byte[] DebianBinary = "2.0\n"u8.ToArray();
    private const long MaximumControlArchiveBytes = 32L * 1024 * 1024;
    private const long MaximumDataArchiveBytes = 1024L * 1024 * 1024;

    public static async Task AssembleAsync(
        string controlArchivePath,
        string dataArchivePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var control = ValidateInput(controlArchivePath, MaximumControlArchiveBytes);
        var data = ValidateInput(dataArchivePath, MaximumDataArchiveBytes);
        var destination = Path.GetFullPath(destinationPath);
        if (!Path.GetExtension(destination).Equals(".deb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La sauvegarde assemblée doit utiliser l’extension .deb.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await output.WriteAsync(ArchiveMagic, cancellationToken);
            await WriteMemberAsync(output, "debian-binary/", DebianBinary, cancellationToken);
            await WriteMemberAsync(output, "control.tar.gz/", control, cancellationToken);
            await WriteMemberAsync(output, "data.tar.gz/", data, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Close();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static FileInfo ValidateInput(string path, long maximumBytes)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Length is < 3 || file.Length > maximumBytes)
            throw new InvalidDataException("Une archive intermédiaire de la sauvegarde N9 est absente ou trop volumineuse.");

        using var stream = file.OpenRead();
        if (stream.ReadByte() != 0x1F || stream.ReadByte() != 0x8B)
            throw new InvalidDataException("Une archive intermédiaire du N9 n’est pas au format gzip attendu.");
        return file;
    }

    private static async Task WriteMemberAsync(
        Stream destination,
        string name,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await WriteHeaderAsync(destination, name, content.Length, cancellationToken);
        await destination.WriteAsync(content, cancellationToken);
        if ((content.Length & 1) != 0)
            await destination.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private static async Task WriteMemberAsync(
        Stream destination,
        string name,
        FileInfo source,
        CancellationToken cancellationToken)
    {
        await WriteHeaderAsync(destination, name, source.Length, cancellationToken);
        await using var input = new FileStream(
            source.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(destination, cancellationToken);
        if ((source.Length & 1) != 0)
            await destination.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private static async Task WriteHeaderAsync(
        Stream destination,
        string name,
        long length,
        CancellationToken cancellationToken)
    {
        if (name.Length > 16 || length < 0)
            throw new InvalidDataException("Un membre de l’archive Debian est invalide.");

        var header =
            name.PadRight(16) +
            "0".PadRight(12) +
            "0".PadRight(6) +
            "0".PadRight(6) +
            "100644".PadRight(8) +
            length.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(10) +
            "`\n";
        var bytes = Encoding.ASCII.GetBytes(header);
        if (bytes.Length != 60)
            throw new InvalidDataException("L’en-tête de l’archive Debian est invalide.");
        await destination.WriteAsync(bytes, cancellationToken);
    }
}
