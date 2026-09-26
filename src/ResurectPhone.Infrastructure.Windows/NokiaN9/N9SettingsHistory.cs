using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed class N9SettingsHistory(string hostFingerprint, string? root = null)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = Path.Combine(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ResurectPhone", "N9", "SettingsHistory"), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hostFingerprint))) + ".json");

    internal async Task<IReadOnlyList<N9SettingsBackup>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        var entries = JsonSerializer.Deserialize<N9SettingsBackup[]>(await File.ReadAllTextAsync(_path, cancellationToken)) ?? [];
        return entries.Where(entry => entry is not null && entry.Path is not null && Regex.IsMatch(entry.Path, @"^/var/lib/resurectphone/maintenance/[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            .DistinctBy(entry => entry.Path).OrderByDescending(entry => entry.CreatedAt).ToArray();
    }

    internal async Task SaveAsync(string feature, string path, string label, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(path, @"^/var/lib/resurectphone/maintenance/[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Chemin de sauvegarde invalide.", nameof(path));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            if (entries.Any(entry => entry.Path == path)) return;
            await WriteAsync(entries.Append(new(feature, path, label, DateTimeOffset.UtcNow)), cancellationToken);
        }
        finally { Gate.Release(); }
    }

    internal async Task RemoveAsync(string path, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try { await WriteAsync((await ReadAsync(cancellationToken)).Where(entry => entry.Path != path), cancellationToken); }
        finally { Gate.Release(); }
    }

    private async Task WriteAsync(IEnumerable<N9SettingsBackup> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".new";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries), cancellationToken);
        File.Move(temporary, _path, true);
    }
}
