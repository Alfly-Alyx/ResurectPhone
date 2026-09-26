using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Dialogs;

public partial class N9MaintenanceDialog : Window
{
    private readonly RecoveryFeature _feature;
    private readonly IN9MaintenanceService _service;
    private readonly Func<char[]?> _requestPassword;
    private bool _busy;
    private bool _canApply;
    private bool _canForce;
    private string? _backupPath;
    private N9LocalPackage? _package;
    private char[]? _operationPassword;

    public N9MaintenanceDialog(RecoveryFeature feature, IN9MaintenanceService service, Func<char[]?> requestPassword)
    {
        InitializeComponent();
        _feature = feature;
        _service = service;
        _requestPassword = requestPassword;
        Title = feature.Title + " — Nokia N9";
        Heading.Text = feature.Title;
        DescriptionText.Text = feature.Description;
        Applications.Visibility = feature.Id is "n9.package-backup" or "n9.cleanup" ? Visibility.Visible : Visibility.Collapsed;
        ChoosePackageButton.Visibility = feature.Id == "n9.package-install" ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await RunAsync(LoadAsync);
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    private async Task LoadAsync()
    {
        _canForce = false;
        _package = null;
        ForceButton.Visibility = Visibility.Collapsed;
        SetReport(await _service.DiagnoseAsync(_feature.Id));
        if (_feature.Id is "n9.package-backup" or "n9.cleanup")
            Applications.ItemsSource = await _service.ReadApplicationsAsync();
    }

    private void SetReport(N9MaintenanceReport report)
    {
        StatusText.Text = report.Summary;
        ReportText.Text = report.Detail;
        _canApply = report.CanApply;
        ApplyButton.Content = report.ActionLabel;
        if (report.BackupPath is not null) _backupPath = report.BackupPath;
        RestoreButton.Visibility = _backupPath is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        SetBusy();
        StatusText.Text = "Opération sur le N9 en cours…";
        try { await action(); }
        catch (Exception exception)
        {
            StatusText.Text = "Opération non terminée";
            ReportText.Text = exception.Message;
            if (exception is N9MaintenanceFailureException failure)
            {
                _backupPath = failure.BackupPath;
                RestoreButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (_operationPassword is not null) Array.Clear(_operationPassword);
            _operationPassword = null;
            _busy = false;
            SetBusy();
        }
    }

    private void SetBusy()
    {
        Progress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = ExportReportButton.IsEnabled = ChoosePackageButton.IsEnabled =
            Applications.IsEnabled = RestoreButton.IsEnabled = CloseButton.IsEnabled = !_busy;
        ApplyButton.IsEnabled = !_busy && _canApply;
        ForceButton.IsEnabled = !_busy && _canForce;
    }

    private async Task<N9MaintenanceReport> WithPasswordAsync(Func<char[]?, Task<N9MaintenanceReport>> action)
    {
        try { return await action(_operationPassword?.ToArray()); }
        catch (N9AdministratorRequiredException) when (_operationPassword is null)
        {
            _operationPassword = _requestPassword();
            if (_operationPassword is null) throw new N9ConnectionException("Préparation annulée : mot de passe administrateur personnalisé non fourni.");
            return await action(_operationPassword.ToArray());
        }
    }

    private async void Application_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || Applications.SelectedItem is not N9Application app) return;
        await RunAsync(async () =>
        {
            _canForce = false;
            ForceButton.Visibility = Visibility.Collapsed;
            if (_feature.Id == "n9.package-backup")
            {
                SetReport(new("Application sélectionnée", app.DisplayName + "\nLa sauvegarde sera enregistrée dans Documents/ResurectPhone/N9/Backups.\n" +
                    "L’archive sera vérifiée ; sa restauration Aegis ne sera pas garantie.", true, "Sauvegarder l’application"));
            }
            else
            {
                var preview = await _service.PreviewRemovalAsync(app.Metadata.Package);
                SetReport(preview with { Detail = app.DisplayName + "\n\n" + preview.Detail +
                    "\nUne sauvegarde sera créée avant la suppression. Sa restauration Aegis peut être impossible.",
                    ActionLabel = "Sauvegarder puis supprimer" });
                _canForce = preview.CanForceRemoval && app.RemovalBlock is null;
                ForceButton.Visibility = _canForce ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private async void ChoosePackage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Paquet Debian (*.deb)|*.deb", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await RunAsync(async () =>
        {
            _package = null;
            _canApply = false;
            var package = await _service.InspectLocalPackageAsync(picker.FileName);
            _package = package;
            SetReport(new("Paquet vérifié sur le PC",
                $"{package.Metadata.Package} {package.Metadata.Version}\nArchitecture : {package.Metadata.Architecture}\nSHA-256 : {package.Sha256}\n\n" +
                (package.IsRebuiltBackup ? "Sauvegarde reconstruite : restauration Aegis bloquée." : "Harmattan vérifiera aussi les dépendances et la provenance lors de l’installation."),
                !package.IsRebuiltBackup && !N9PackageMaintenancePolicy.IsProtectedPackage(package.Metadata), "Installer ce paquet"));
        });
    }

    private string BackupDestination(N9Application application)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ResurectPhone", "N9", "Backups");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, application.Metadata.Package + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".deb");
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_feature.Id == "n9.package-install" && _package is not null)
            SetReport(await WithPasswordAsync(password => _service.InstallLocalPackageAsync(_package, password)));
        else if (_feature.Id is "n9.package-backup" or "n9.cleanup" && Applications.SelectedItem is N9Application app)
        {
            var destination = BackupDestination(app);
            var backup = await WithPasswordAsync(password => _service.ExportApplicationAsync(app.Metadata.Package, destination, password));
            if (_feature.Id == "n9.package-backup") SetReport(backup);
            else
            {
                var removed = await WithPasswordAsync(password => _service.RemoveApplicationAsync(app.Metadata.Package, false, password));
                SetReport(removed with { Detail = removed.Detail + "\nSauvegarde : " + destination });
                _canForce = false;
            }
        }
        else SetReport(await WithPasswordAsync(password => _service.ApplyAsync(_feature.Id, password)));
    });

    private async void Force_Click(object sender, RoutedEventArgs e)
    {
        if (!_canForce || Applications.SelectedItem is not N9Application app) return;
        if (MessageBox.Show(this,
            "Supprimer uniquement « " + app.Name + " » en ignorant les dépendances ?\n\nLes paquets qui en dépendent peuvent ne plus fonctionner. Une sauvegarde sera créée, mais sa restauration Aegis n’est pas garantie.",
            "Suppression en mode expert", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            var destination = BackupDestination(app);
            await WithPasswordAsync(password => _service.ExportApplicationAsync(app.Metadata.Package, destination, password));
            var removed = await WithPasswordAsync(password => _service.RemoveApplicationAsync(app.Metadata.Package, true, password));
            SetReport(removed with { Detail = removed.Detail + "\nSauvegarde : " + destination });
            _canForce = false;
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadAsync);
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_backupPath is not null)
            await RunAsync(async () =>
            {
                var result = await WithPasswordAsync(password => _service.RestoreSettingsAsync(_backupPath, password));
                _backupPath = null;
                SetReport(result);
            });
    }
    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = _feature.Id + "-rapport.txt", Filter = "Rapport texte (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) == true)
        {
            try { File.WriteAllText(dialog.FileName, DateTimeOffset.Now + "\n" + Heading.Text + "\n" + StatusText.Text + "\n\n" + ReportText.Text); }
            catch (IOException exception) { StatusText.Text = exception.Message; }
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
