using System.Windows;
using ResurectPhone.App.Dialogs;
using ResurectPhone.App.Presentation;
using ResurectPhone.Infrastructure.Windows.Discovery;
using ResurectPhone.Infrastructure.Windows.Android;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.ApplyTo(Resources);
        base.OnStartup(e);

        var discovery = new WindowsPhoneDiscoveryService();
        var n9Connection = new N9SshConnectionService();
        var pairingInteraction = new N9PairingInteraction(() => Current.MainWindow);
        var androidTaskManager = new AndroidAdbTaskManagerService();
        var androidInteraction = new AndroidTaskManagerInteraction(() => Current.MainWindow);
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                discovery,
                n9Connection,
                pairingInteraction,
                androidTaskManager,
                androidInteraction)
        };
        MainWindow = window;
        window.Show();
    }
}
