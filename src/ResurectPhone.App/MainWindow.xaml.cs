using System.Windows;

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
    }
}
