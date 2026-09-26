namespace ResurectPhone.Core.NokiaN9;

public static class N9MaintenanceCatalog
{
    // Verified against /usr/lib/developer-mode/developer-mode.pkglist on PR1.3.
    // Pin latrace to the signed Nokia tools mirror; the newer SDK index is unsigned.
    public static IReadOnlyDictionary<string, string[]> DeveloperPackages { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["n9.devtools.debugging"] = ["maemo-debug-scripts", "gdb", "gdbserver"],
            ["n9.devtools.networking"] = ["tcpdump"],
            ["n9.devtools.resources"] = ["valgrind", "sp-memusage", "sp-endurance", "sp-endurance-postproc", "sp-smaps-measure", "sp-smaps-visualize", "xrestop"],
            ["n9.devtools.power"] = ["energy-profiler", "energy-profiler-server", "energy-profiler-plugin-ram", "energy-profiler-plugin-power", "energy-profiler-plugin-net", "energy-profiler-plugin-cpu"],
            ["n9.devtools.performance"] = ["oprofile", "swaplogger", "xresponse", "htop"],
            ["n9.devtools.tracing"] = ["strace", "latrace=0.5.11-1~dc115d0+0m6", "functracer", "sp-rtrace", "sp-rtrace-visualize", "xtrace"],
            ["n9.devtools.test-automation"] = ["xnee", "sp-stress"],
            ["n9.devtools.utilities"] = ["wget", "nano", "x11-utils"],
            ["n9.devtools.logging"] = ["klogd", "sp-timestamp"]
        };

    public static IReadOnlySet<string> FeatureIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "device.identity", "n9.firmware", "n9.repositories", "n9.dependencies", "n9.nokia-store",
        "n9.alternative-stores", "n9.package-backup", "n9.package-install",
        "n9.internet", "n9.gps", "n9.account", "n9.cleanup",
        "n9.devtools.debugging", "n9.devtools.networking", "n9.devtools.resources",
        "n9.devtools.power", "n9.devtools.performance", "n9.devtools.tracing",
        "n9.devtools.test-automation", "n9.devtools.utilities", "n9.devtools.logging"
    };
}
