using System.Windows;
using ResurectPhone.App.Presentation;
using ResurectPhone.Infrastructure.Windows.Discovery;

namespace ResurectPhone.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.ApplyTo(Resources);
        base.OnStartup(e);

        var discovery = new WindowsPhoneDiscoveryService();
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(discovery)
        };
        MainWindow = window;
        window.Show();
    }
}
