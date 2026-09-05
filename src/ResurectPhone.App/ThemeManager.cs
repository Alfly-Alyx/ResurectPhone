using System.Windows;

namespace ResurectPhone.App;

internal static class ThemeManager
{
    internal static void ApplyTo(ResourceDictionary resources)
    {
        var theme = Environment.OSVersion.Version.Build >= 22000
            ? "Themes/Windows11.xaml"
            : "Themes/Windows10.xaml";

        resources.MergedDictionaries.Clear();
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(theme, UriKind.Relative)
        });
    }
}
