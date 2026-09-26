using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9SettingsHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "n9-history-tests", Guid.NewGuid().ToString("N"));
    private const string Backup = "/var/lib/resurectphone/maintenance/0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task HistorySurvivesNewInstanceDeduplicatesAndIsScopedToPhone()
    {
        var history = new N9SettingsHistory("phone-a", _root);
        await history.SaveAsync("n9.gps", Backup, "GPS", default);
        await history.SaveAsync("n9.gps", Backup, "GPS again", default);
        var fresh = new N9SettingsHistory("phone-a", _root);
        var entry = Assert.Single(await fresh.ReadAsync(default));
        Assert.Equal("GPS", entry.Label);
        Assert.Empty(await new N9SettingsHistory("phone-b", _root).ReadAsync(default));
        await fresh.RemoveAsync(Backup, default);
        Assert.Empty(await history.ReadAsync(default));
    }

    [Fact]
    public async Task InvalidPathsCannotEnterRestoreHistory()
    {
        var history = new N9SettingsHistory("phone-a", _root);
        await Assert.ThrowsAsync<ArgumentException>(() => history.SaveAsync("n9.gps", "/tmp/other;reboot", "GPS", default));
        await history.SaveAsync("n9.gps", Backup, "GPS", default);
        var file = Assert.Single(Directory.EnumerateFiles(_root, "*.json"));
        await File.WriteAllTextAsync(file, "[null,{\"Path\":\"/tmp/other\"},{\"Path\":null}]");
        Assert.Empty(await history.ReadAsync(default));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
