using System.Windows;
using System.Windows.Threading;
using ResurectPhone.App.Presentation;

namespace ResurectPhone.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _n9DiscoveryTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        _n9DiscoveryTimer.Tick += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.AutoDiscoverN9Async();
        };
        Loaded += async (_, _) =>
        {
            _n9DiscoveryTimer.Start();
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.AutoDiscoverN9Async();
        };
        Closed += (_, _) =>
        {
            _n9DiscoveryTimer.Stop();
            (DataContext as MainWindowViewModel)?.Shutdown();
        };
    }
}
