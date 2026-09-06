using System.Windows;
using ResurectPhone.App.Presentation;

namespace ResurectPhone.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        Closed += (_, _) => (DataContext as MainWindowViewModel)?.Shutdown();
    }
}
