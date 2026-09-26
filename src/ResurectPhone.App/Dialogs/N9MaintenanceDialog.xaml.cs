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
    private IReadOnlyList<N9Application> _applications = [];
    private bool _busy;
    private bool _canApply;
    private bool _canForce;
    private string? _backupPath;
    private string? _preparedDirectory;
    private string? _restorablePackage;
    private CancellationTokenSource? _preparationCancellation;
    private N9LocalPackage? _package;
    private char[]? _operationPassword;

    public N9MaintenanceDialog(RecoveryFeature feature, IN9MaintenanceService service, Func<char[]?> requestPassword)
    {
        _feature = feature;
        InitializeComponent();
        _service = service;
        _requestPassword = requestPassword;
        Title = feature.Title + " — Nokia N9";
        Heading.Text = feature.Title;
        DescriptionText.Text = feature.Description;
        Applications.Visibility = feature.Id is "n9.package-backup" or "n9.cleanup" ? Visibility.Visible : Visibility.Collapsed;
        ChoosePackageButton.Visibility = feature.Id == "n9.package-install" ? Visibility.Visible : Visibility.Collapsed;
        Stores.Visibility = feature.Id == "n9.alternative-stores" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationSearch.Visibility = SearchLabel.Visibility = Applications.Visibility;
        RestoreApplicationButton.Visibility = Applications.Visibility;
        LaunchButton.Visibility = feature.Id is "n9.alternative-stores" or "n9.nokia-store" or "n9.gps" or "n9.internet" or "n9.account" ? Visibility.Visible : Visibility.Collapsed;
        LaunchButton.Content = feature.Id is "n9.gps" or "n9.account" ? "Ouvrir Cartes" : feature.Id == "n9.internet" ? "Ouvrir le navigateur" : "Ouvrir la boutique";
        DriveButton.Visibility = feature.Id is "n9.gps" or "n9.account" ? Visibility.Visible : Visibility.Collapsed;
        var workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        Loaded += async (_, _) => await RunAsync(LoadAsync);
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    private void EnableAction(string label, string description)
    {
        _canApply = true;
        ApplyButton.Content = label;
        StatusText.Text = description;
        SetBusy();
    }

    private void UpdateStoreAction() => EnableAction(Stores.SelectedIndex == 1 ? "Installer Warehouse" : "Installer MeeShop",
        "Le PC télécharge les fichiers, prépare les dépôts et installe la boutique sur le N9 par USB.");

    private void Store_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_busy) UpdateStoreAction();
    }

    private void ApplicationSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _busy) return;
        var filter = ApplicationSearch.Text.Trim();
        Applications.ItemsSource = _applications.Where(app => app.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _canApply = _canForce = false;
        SetBusy();
    }

    private async Task LoadAsync()
    {
        _canApply = _canForce = false;
        _package = null;
        ForceButton.Visibility = Visibility.Collapsed;
        if (_feature.Id is "n9.package-backup" or "n9.cleanup")
        {
            _applications = await _service.ReadApplicationsAsync();
            Applications.ItemsSource = _applications.Where(app => app.DisplayName.Contains(ApplicationSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            SetReport(new("Choisissez l’application à " + (_feature.Id == "n9.cleanup" ? "supprimer" : "sauvegarder"),
                $"{_applications.Count} applications installées. Une sauvegarde précède chaque suppression."));
        }
        else
        {
            SetReport(await _service.DiagnoseAsync(_feature.Id));
            if (_feature.Id.StartsWith("n9.devtools.", StringComparison.Ordinal))
                EnableAction("Installer ces outils", "Les dépôts et les dépendances seront préparés automatiquement, puis les outils seront installés.");
            if (_feature.Id == "n9.alternative-stores") UpdateStoreAction();
            if (_feature.Id == "n9.firmware")
                EnableAction("Télécharger, sauvegarder et transférer", "Kernel-plus 2.6.32.61 : préparer les fichiers, sauvegarder le noyau actuel et copier le candidat dans le dossier dédié du N9. La ROM de récupération représente environ 1,2 Go.");
            if (_feature.Id == "n9.nokia-store")
                EnableAction("Installer MeeShop sur le N9", "La boutique Nokia d’origine a fermé. Installer MeeShop donne accès aux applications OpenRepos.");
        }
    }

    private void SetReport(N9MaintenanceReport report)
    {
        StatusText.Text = report.Summary;
        ReportText.Text = report.Detail;
        _canApply = report.CanApply;
        ApplyButton.Content = report.ActionLabel;
        if (report.BackupPath is not null) _backupPath = report.BackupPath;
        if (report.LocalFile is not null)
        {
            _preparedDirectory = Path.GetDirectoryName(report.LocalFile);
            FilesButton.Visibility = Visibility.Visible;
            if (report.CanRestoreApplication)
            {
                _restorablePackage = report.LocalFile;
                RestoreApplicationButton.Content = "Réinstaller cette sauvegarde";
                RestoreApplicationButton.Visibility = Visibility.Visible;
            }
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        SetBusy();
        StatusText.Text = "Opération sur le N9 en cours…";
        try { await action(); }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Préparation annulée";
            ReportText.Text = "Vous pouvez relancer la préparation. Les fichiers déjà téléchargés et vérifiés seront réutilisés.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Opération non terminée";
            Details.IsExpanded = true;
            ReportText.Text = exception.Message;
            if (exception is N9MaintenanceFailureException failure)
            {
                _backupPath = failure.BackupPath;
                SettingsBackupsPanel.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (_operationPassword is not null) Array.Clear(_operationPassword);
            _operationPassword = null;
            _busy = false;
            _preparationCancellation?.Dispose();
            _preparationCancellation = null;
            await ReloadBackupsAsync();
            SetBusy();
        }
    }

    private void SetBusy()
    {
        if (_busy) Progress.IsIndeterminate = true;
        Progress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        FilesButton.IsEnabled = RestoreApplicationButton.IsEnabled = LaunchButton.IsEnabled = DriveButton.IsEnabled = SettingsBackups.IsEnabled = !_busy;
        Stores.IsEnabled = ApplicationSearch.IsEnabled = !_busy;
        RefreshButton.IsEnabled = ExportReportButton.IsEnabled = ChoosePackageButton.IsEnabled =
            Applications.IsEnabled = RestoreButton.IsEnabled = CloseButton.IsEnabled = !_busy;
        ApplyButton.IsEnabled = !_busy && _canApply;
        ForceButton.IsEnabled = !_busy && _canForce;
        RestoreButton.IsEnabled = !_busy && _backupPath is not null;
        if (_busy && _preparationCancellation is not null) CloseButton.IsEnabled = true;
        CloseButton.Content = _busy && _preparationCancellation is not null ? "Annuler la préparation" : "Fermer";
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
        if (_busy) return;
        _package = null;
        if (Applications.SelectedItem is not N9Application app)
        {
            _canApply = _canForce = false;
            ForceButton.Visibility = Visibility.Collapsed;
            SetBusy();
            return;
        }
        await RunAsync(async () =>
        {
            _canForce = false;
            ForceButton.Visibility = Visibility.Collapsed;
            if (_feature.Id == "n9.package-backup")
            {
                SetReport(new("Application sélectionnée", app.DisplayName + "\nLa sauvegarde sera enregistrée dans Documents/ResurectPhone/N9/Backups.\n" +
                    "Le paquet original sera conservé s’il est disponible. Sinon, l’archive reconstruite sera identifiée comme non réinstallable automatiquement.", true, "Sauvegarder l’application"));
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
        if (_feature.Id == "n9.firmware" && _service is IN9KernelService kernel)
        {
            _preparationCancellation = new CancellationTokenSource();
            SetBusy();
            var progress = new Progress<N9OperationProgress>(step =>
            {
                StatusText.Text = step.Message;
                Progress.IsIndeterminate = step.Percent is null;
                Progress.Value = step.Percent ?? 0;
            });
            SetReport(await WithPasswordAsync(async password =>
            {
                var prepared = await kernel.PrepareKernelAsync(progress, password, _preparationCancellation.Token);
                _preparedDirectory = prepared.Directory;
                FilesButton.Visibility = Visibility.Visible;
                return new N9MaintenanceReport("Noyau préparé et sauvegarde vérifiée",
                    "Sur le PC : " + prepared.Directory + "\nSur le N9 : " + prepared.RemoteDirectory +
                    "\nSauvegarde du noyau : " + prepared.BackupImage + "\nSHA-256 : " + prepared.BackupSha256 +
                    "\nLe noyau actif reste " + prepared.OriginalKernel + ". Le flashage n’a pas été effectué.");
            }));
        }
        else if (_feature.Id == "n9.package-install" && _package is not null)
            SetReport(await WithPasswordAsync(password => _service.InstallLocalPackageAsync(_package, password)));
        else if (_feature.Id is "n9.package-backup" or "n9.cleanup" && Applications.SelectedItem is N9Application app)
        {
            var destination = BackupDestination(app);
            var backup = await WithPasswordAsync(password => _service.ExportApplicationAsync(app.Metadata.Package, destination, password));
            if (_feature.Id == "n9.package-backup") SetReport(backup);
            else
            {
                var removed = await WithPasswordAsync(password => _service.RemoveApplicationAsync(app.Metadata.Package, false, password));
                SetReport(removed with { Detail = removed.Detail + "\nSauvegarde : " + destination, LocalFile = backup.LocalFile, CanRestoreApplication = backup.CanRestoreApplication });
                _canForce = false;
            }
        }
        else
        {
            var progress = new Progress<N9OperationProgress>(step => StatusText.Text = step.Message);
            var actionId = _feature.Id == "n9.alternative-stores" && Stores.SelectedIndex == 1 ? "n9.store.warehouse" : _feature.Id;
            SetReport(await WithPasswordAsync(password => _service.ExecuteActionAsync(actionId, progress, password)));
        }
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
            var backup = await WithPasswordAsync(password => _service.ExportApplicationAsync(app.Metadata.Package, destination, password));
            var removed = await WithPasswordAsync(password => _service.RemoveApplicationAsync(app.Metadata.Package, true, password));
            SetReport(removed with { Detail = removed.Detail + "\nSauvegarde : " + destination, LocalFile = backup.LocalFile, CanRestoreApplication = backup.CanRestoreApplication });
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
    private async Task ReloadBackupsAsync()
    {
        try
        {
            var backups = (await _service.ReadSettingsBackupsAsync()).ToList();
            if (_backupPath is not null && !backups.Any(backup => backup.Path == _backupPath))
                backups.Insert(0, new(_feature.Id, _backupPath, "Sauvegarde de cette opération", DateTimeOffset.MinValue));
            var selectedPath = _backupPath;
            SettingsBackups.ItemsSource = backups;
            SettingsBackups.SelectedItem = backups.FirstOrDefault(backup => backup.Path == selectedPath) ?? backups.FirstOrDefault();
            SettingsBackupsPanel.Visibility = backups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { ReportText.Text += "\nHistorique local indisponible : " + exception.Message; }
    }

    private void SettingsBackup_Changed(object sender, SelectionChangedEventArgs e)
    {
        _backupPath = (SettingsBackups.SelectedItem as N9SettingsBackup)?.Path;
        if (IsLoaded) SetBusy();
    }

    private async void RestoreApplication_Click(object sender, RoutedEventArgs e)
    {
        var path = _restorablePackage;
        if (path is null || !File.Exists(path))
        {
            var picker = new OpenFileDialog { Filter = "Sauvegarde d’application (*.deb)|*.deb", CheckFileExists = true,
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ResurectPhone", "N9", "Backups") };
            if (picker.ShowDialog(this) != true) return;
            path = picker.FileName;
        }
        await RunAsync(async () =>
        {
            var package = await _service.InspectLocalPackageAsync(path);
            SetReport(await WithPasswordAsync(password => _service.InstallLocalPackageAsync(package, password)));
        });
    }

    private async void Launch_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var app = _feature.Id switch
        {
            "n9.gps" or "n9.account" => "maps",
            "n9.internet" => "browser",
            _ => Stores.SelectedIndex == 1 ? "warehouse" : "meeshop"
        };
        SetLaunchReport(await _service.LaunchApplicationAsync(app));
    });

    private async void Drive_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(async () => SetLaunchReport(await _service.LaunchApplicationAsync("drive")));

    private void SetLaunchReport(N9MaintenanceReport report) =>
        SetReport(report with { CanApply = _canApply, ActionLabel = ApplyButton.Content?.ToString() ?? "Appliquer" });

    private void Files_Click(object sender, RoutedEventArgs e)
    {
        if (_preparedDirectory is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_preparedDirectory) { UseShellExecute = true }); }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) _preparationCancellation?.Cancel();
        else Close();
    }
}
