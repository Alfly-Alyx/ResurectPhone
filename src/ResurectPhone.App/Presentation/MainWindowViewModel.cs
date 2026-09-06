using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ResurectPhone.Core.Android;
using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Discovery;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlySet<PhonePlatform> N9Platforms =
        new HashSet<PhonePlatform> { PhonePlatform.MeeGoHarmattan };

    private static readonly IReadOnlySet<PhonePlatform> WindowsPhonePlatforms =
        new HashSet<PhonePlatform> { PhonePlatform.WindowsPhone, PhonePlatform.Windows10Mobile };

    private readonly IPhoneDiscoveryService _discovery;
    private readonly IN9ConnectionService _n9Connection;
    private readonly IN9PairingInteraction _n9PairingInteraction;
    private readonly IAndroidTaskManagerService _androidTaskManager;
    private readonly IAndroidTaskManagerInteraction _androidInteraction;
    private readonly NavigationSectionViewModel _homeSection;
    private readonly IReadOnlyList<NavigationSectionViewModel> _n9Sections;
    private readonly IReadOnlyList<NavigationSectionViewModel> _windowsPhoneSections;
    private readonly IReadOnlyList<NavigationSectionViewModel> _androidSections;
    private NavigationSectionViewModel _selectedSection;
    private string _activeFamilyTitle = string.Empty;
    private DetectedPhone? _connectedPhone;
    private string _connectionTitle = "Aucun téléphone détecté";
    private string _connectionDetail = "Branchez un Nokia N9 ou un Windows Phone en USB.";
    private bool _isScanning;
    private bool _isConnectingN9;
    private string? _androidSerial;
    private CancellationTokenSource? _androidMonitorCancellation;
    private IReadOnlyList<AndroidProcessInfo> _latestAndroidProcesses = [];
    private bool _isAndroidMonitoring;
    private bool _isReleasingMemory;
    private string _androidFilter = string.Empty;
    private string _androidCpuText = "—";
    private string _androidMemoryText = "—";
    private string _androidAvailableMemoryText = "—";
    private string _androidProcessCountText = "0";
    private string _androidTaskManagerDetail = "Connectez un téléphone Android pour lire ses processus.";
    private string _androidLastRefreshText = "Aucune mesure";

    public MainWindowViewModel(
        IPhoneDiscoveryService discovery,
        IN9ConnectionService n9Connection,
        IN9PairingInteraction n9PairingInteraction,
        IAndroidTaskManagerService androidTaskManager,
        IAndroidTaskManagerInteraction androidInteraction)
    {
        _discovery = discovery;
        _n9Connection = n9Connection;
        _n9PairingInteraction = n9PairingInteraction;
        _androidTaskManager = androidTaskManager;
        _androidInteraction = androidInteraction;
        _homeSection = new(
            "home", string.Empty, "\uE80F", "Accueil",
            "Choisissez le téléphone à remettre en service.", null,
            new HashSet<PhonePlatform>(), true);
        _n9Sections =
        [
            new("n9.home", "Nokia N9", "\uE80F", "Nokia N9", "Toutes les solutions prévues pour remettre un Nokia N9 en service.", null, N9Platforms),
            new("n9.device", "Nokia N9", "\uE946", "Appareil", "Identité, version d’Harmattan et compatibilité.", RecoveryArea.Device, N9Platforms),
            new("n9.developer-tools", "Nokia N9", "\uE943", "Outils développeur", "Les neuf ensembles d’outils installables à la demande.", RecoveryArea.DeveloperTools, N9Platforms),
            new("n9.firmware", "Nokia N9", "\uE950", "ROM et système", "ROM Harmattan PR1.3 et opérations système.", RecoveryArea.Firmware, N9Platforms),
            new("n9.stores", "Nokia N9", "\uE719", "Boutiques et applications", "Nokia Store, boutiques alternatives et mises à jour.", RecoveryArea.StoresAndApplications, N9Platforms),
            new("n9.internet", "Nokia N9", "\uE774", "Internet", "Certificats, chiffrement et navigation Web.", RecoveryArea.Internet, N9Platforms),
            new("n9.navigation", "Nokia N9", "\uE707", "GPS et cartes", "Localisation, Cartes et Drive.", RecoveryArea.Navigation, N9Platforms),
            new("n9.account", "Nokia N9", "\uE77B", "Compte Nokia", "Services de compte devenus indisponibles.", RecoveryArea.NokiaAccount, N9Platforms),
            new("n9.cleanup", "Nokia N9", "\uE74D", "Nettoyage", "Applications dépendant de services disparus.", RecoveryArea.Cleanup, N9Platforms)
        ];
        _windowsPhoneSections =
        [
            new("windows-phone.home", "Windows Phone", "\uE80F", "Windows Phone", "Solutions avancées prévues pour les appareils Windows Phone compatibles.", null, WindowsPhonePlatforms),
            new("windows-phone.device", "Windows Phone", "\uE946", "Appareil", "Modèle, version de Windows Phone et compatibilité.", RecoveryArea.Device, WindowsPhonePlatforms),
            new("windows-phone.stores", "Windows Phone", "\uE719", "Boutiques alternatives", "Paquets de boutiques intégrés au logiciel et installation hors connexion selon la compatibilité.", RecoveryArea.StoresAndApplications, WindowsPhonePlatforms),
            new("windows-phone.internals", "Windows Phone", "\uE8A7", "Windows Internals", "Déverrouillage et opérations avancées sur les modèles compatibles.", RecoveryArea.WindowsInternals, WindowsPhonePlatforms),
            new("windows-phone.android", "Windows Phone", "\uE8D7", "Android", "Projet Android et possibilité d’installation selon l’appareil.", RecoveryArea.AlternativeSystem, WindowsPhonePlatforms)
        ];
        _androidSections =
        [
            new("android.home", "Android", "\uE80F", "Android", "Outils directs pour diagnostiquer et entretenir un téléphone Android.", null, new HashSet<PhonePlatform> { PhonePlatform.Android }),
            new("android.device", "Android", "\uE946", "Appareil", "Modèle, version d’Android et état de la liaison ADB.", RecoveryArea.Device, new HashSet<PhonePlatform> { PhonePlatform.Android }),
            new("android.task-manager", "Android", "\uE9D9", "Gestionnaire des tâches", "Processus et ressources consommées en temps réel, avec optimisation prudente de la mémoire vive.", RecoveryArea.TaskManager, new HashSet<PhonePlatform> { PhonePlatform.Android })
        ];
        Sections = [_homeSection];
        _selectedSection = _homeSection;
        OpenN9Command = new RelayCommand(() => OpenFamily(_n9Sections));
        OpenWindowsPhoneCommand = new RelayCommand(() => OpenFamily(_windowsPhoneSections));
        OpenAndroidCommand = new RelayCommand(() => OpenFamily(_androidSections));
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        ConnectN9Command = new AsyncRelayCommand(ConnectN9Async, () => !IsConnectingN9);
        ForgetN9PairingCommand = new RelayCommand(ForgetN9Pairing, () => CanForgetN9Pairing);
        ToggleAndroidMonitoringCommand = new RelayCommand(
            ToggleAndroidMonitoring,
            () => !IsScanning && !IsReleasingMemory);
        ReleaseAndroidMemoryCommand = new AsyncRelayCommand(
            ReleaseAndroidMemoryAsync,
            () => CanReleaseAndroidMemory);
        RefreshFeatures();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NavigationSectionViewModel> Sections { get; }
    public ObservableCollection<RecoveryFeatureViewModel> SelectedFeatures { get; } = [];
    public ObservableCollection<AndroidProcessViewModel> AndroidProcesses { get; } = [];
    public RelayCommand OpenN9Command { get; }
    public RelayCommand OpenWindowsPhoneCommand { get; }
    public RelayCommand OpenAndroidCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ConnectN9Command { get; }
    public RelayCommand ForgetN9PairingCommand { get; }
    public RelayCommand ToggleAndroidMonitoringCommand { get; }
    public AsyncRelayCommand ReleaseAndroidMemoryCommand { get; }

    public NavigationSectionViewModel SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is null || ReferenceEquals(_selectedSection, value))
                return;

            if (value.IsHome)
                ShowHome();
            else
                SelectSection(value);
        }
    }

    public bool IsHome => SelectedSection.IsHome;
    public bool IsFamilyView => !IsHome;
    public bool IsTaskManagerView => SelectedSection.Area == RecoveryArea.TaskManager;
    public bool IsStandardFamilyView => IsFamilyView && !IsTaskManagerView;
    public bool IsN9Family => ActiveFamilyTitle == "Nokia N9";
    public bool IsAndroidFamily => ActiveFamilyTitle == "Android";
    public bool CanForgetN9Pairing => IsN9Family && _n9Connection.HasPairing;
    public string ActiveFamilyTitle => _activeFamilyTitle;
    public string PageTitle => SelectedSection.Title;
    public string PageSubtitle => SelectedSection.Subtitle;
    public string SelectedFamilyTitle => SelectedSection.FamilyTitle;
    public string FeaturesTitle => $"Fonctions pour {SelectedSection.FamilyTitle}";
    public string ConnectionTitle
    {
        get => _connectionTitle;
        private set => SetField(ref _connectionTitle, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetField(ref _connectionDetail, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetField(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                OnPropertyChanged(nameof(CanReleaseAndroidMemory));
                ScanCommand.RaiseCanExecuteChanged();
                ToggleAndroidMonitoringCommand.RaiseCanExecuteChanged();
                ReleaseAndroidMemoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ScanButtonText => IsScanning ? "Recherche…" : "Rechercher";

    public bool IsConnectingN9
    {
        get => _isConnectingN9;
        private set
        {
            if (SetField(ref _isConnectingN9, value))
            {
                OnPropertyChanged(nameof(N9ConnectionButtonText));
                ConnectN9Command.RaiseCanExecuteChanged();
            }
        }
    }

    public string N9ConnectionButtonText => IsConnectingN9
        ? "Connexion…"
        : _n9Connection.HasPairing ? "Lire le N9" : "Appairer le N9";

    public bool IsAndroidMonitoring
    {
        get => _isAndroidMonitoring;
        private set
        {
            if (SetField(ref _isAndroidMonitoring, value))
            {
                OnPropertyChanged(nameof(AndroidMonitoringButtonText));
                OnPropertyChanged(nameof(CanReleaseAndroidMemory));
                ToggleAndroidMonitoringCommand.RaiseCanExecuteChanged();
                ReleaseAndroidMemoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsReleasingMemory
    {
        get => _isReleasingMemory;
        private set
        {
            if (SetField(ref _isReleasingMemory, value))
            {
                OnPropertyChanged(nameof(AndroidMemoryButtonText));
                OnPropertyChanged(nameof(CanReleaseAndroidMemory));
                ReleaseAndroidMemoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanReleaseAndroidMemory =>
        IsTaskManagerView && _androidSerial is not null && !IsScanning && !IsReleasingMemory;

    public string AndroidMonitoringButtonText => IsAndroidMonitoring ? "Arrêter" : "Démarrer";
    public string AndroidMemoryButtonText => IsReleasingMemory ? "Libération…" : "Libérer la mémoire";

    public string AndroidFilter
    {
        get => _androidFilter;
        set
        {
            if (SetField(ref _androidFilter, value))
                ApplyAndroidFilter();
        }
    }

    public string AndroidCpuText
    {
        get => _androidCpuText;
        private set => SetField(ref _androidCpuText, value);
    }

    public string AndroidMemoryText
    {
        get => _androidMemoryText;
        private set => SetField(ref _androidMemoryText, value);
    }

    public string AndroidAvailableMemoryText
    {
        get => _androidAvailableMemoryText;
        private set => SetField(ref _androidAvailableMemoryText, value);
    }

    public string AndroidProcessCountText
    {
        get => _androidProcessCountText;
        private set => SetField(ref _androidProcessCountText, value);
    }

    public string AndroidTaskManagerDetail
    {
        get => _androidTaskManagerDetail;
        private set => SetField(ref _androidTaskManagerDetail, value);
    }

    public string AndroidLastRefreshText
    {
        get => _androidLastRefreshText;
        private set => SetField(ref _androidLastRefreshText, value);
    }

    private void OpenFamily(IReadOnlyList<NavigationSectionViewModel> familySections)
    {
        StopAndroidMonitoring();
        _activeFamilyTitle = familySections[0].FamilyTitle;
        if (_activeFamilyTitle != "Android")
            _androidSerial = null;
        if (_connectedPhone is not null && !familySections[0].Platforms.Contains(_connectedPhone.Platform))
            _connectedPhone = null;
        ResetConnectionTextForFamily(_activeFamilyTitle);
        Sections.Clear();
        Sections.Add(_homeSection);
        foreach (var section in familySections)
            Sections.Add(section);

        OnPropertyChanged(nameof(ActiveFamilyTitle));
        OnPropertyChanged(nameof(IsN9Family));
        OnPropertyChanged(nameof(IsAndroidFamily));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        OnPropertyChanged(nameof(N9ConnectionButtonText));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
        SelectSection(familySections[0]);
    }

    private void ShowHome()
    {
        StopAndroidMonitoring();
        _activeFamilyTitle = string.Empty;
        Sections.Clear();
        Sections.Add(_homeSection);
        OnPropertyChanged(nameof(ActiveFamilyTitle));
        OnPropertyChanged(nameof(IsN9Family));
        OnPropertyChanged(nameof(IsAndroidFamily));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
        SelectSection(_homeSection);
    }

    private void SelectSection(NavigationSectionViewModel section)
    {
        if (section.Area != RecoveryArea.TaskManager)
            StopAndroidMonitoring();
        _selectedSection = section;
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsFamilyView));
        OnPropertyChanged(nameof(IsTaskManagerView));
        OnPropertyChanged(nameof(IsStandardFamilyView));
        OnPropertyChanged(nameof(CanReleaseAndroidMemory));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(SelectedFamilyTitle));
        OnPropertyChanged(nameof(FeaturesTitle));
        ReleaseAndroidMemoryCommand.RaiseCanExecuteChanged();
        RefreshFeatures();
    }

    private async Task ScanAsync()
    {
        if (IsAndroidFamily)
        {
            await ScanAndroidAsync();
            return;
        }

        IsScanning = true;
        ConnectionTitle = "Recherche du téléphone…";
        ConnectionDetail = "ResurectPhone consulte les appareils reconnus par Windows.";
        try
        {
            var phones = await _discovery.DiscoverAsync();
            _connectedPhone = phones.FirstOrDefault(phone =>
                SelectedSection.Platforms.Contains(phone.Platform));
            if (_connectedPhone is null)
            {
                ConnectionTitle = "Aucun téléphone détecté";
                ConnectionDetail = "Branchez un Nokia N9 ou un Windows Phone en USB, puis réessayez.";
            }
            else
            {
                ConnectionTitle = _connectedPhone.DisplayName;
                ConnectionDetail = Describe(_connectedPhone);
            }
        }
        catch (Exception)
        {
            _connectedPhone = null;
            ConnectionTitle = "Recherche impossible";
            ConnectionDetail = "Windows n’a pas permis de consulter les appareils. Réessayez après avoir reconnecté le téléphone.";
        }
        finally
        {
            IsScanning = false;
            RefreshFeatures();
        }
    }

    private async Task ScanAndroidAsync()
    {
        StopAndroidMonitoring();
        IsScanning = true;
        ConnectionTitle = "Recherche du téléphone Android…";
        ConnectionDetail = "ResurectPhone consulte la liaison ADB en lecture seule.";
        try
        {
            if (!_androidTaskManager.IsAdbAvailable)
            {
                _androidSerial = null;
                _connectedPhone = null;
                ConnectionTitle = "ADB est introuvable";
                ConnectionDetail = "Android Platform Tools doit être installé ou intégré à ResurectPhone.";
                AndroidTaskManagerDetail = ConnectionDetail;
                return;
            }

            var devices = await _androidTaskManager.DiscoverDevicesAsync();
            var ready = devices.FirstOrDefault(device => device.IsReady);
            if (ready is null)
            {
                _androidSerial = null;
                _connectedPhone = null;
                var unauthorized = devices.FirstOrDefault(device =>
                    device.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase));
                ConnectionTitle = unauthorized is null
                    ? "Aucun téléphone Android détecté"
                    : "Autorisation Android nécessaire";
                ConnectionDetail = unauthorized is null
                    ? "Activez le débogage USB, branchez le téléphone puis relancez la recherche."
                    : "Déverrouillez le téléphone et acceptez la demande d’autorisation de débogage USB.";
                AndroidTaskManagerDetail = ConnectionDetail;
                return;
            }

            _androidSerial = ready.Serial;
            _connectedPhone = new DetectedPhone(
                "android-adb",
                ready.DisplayName,
                PhonePlatform.Android,
                "Android",
                ready.AndroidVersion,
                string.IsNullOrWhiteSpace(ready.ApiLevel) ? null : $"API {ready.ApiLevel}",
                null,
                PhoneCapability.ReadIdentity | PhoneCapability.ReadProcesses | PhoneCapability.ManageProcesses);
            ConnectionTitle = _connectedPhone.DisplayName;
            ConnectionDetail = Describe(_connectedPhone);
            AndroidTaskManagerDetail = "Téléphone prêt. Démarrez la surveillance pour afficher les processus.";
        }
        catch (AndroidAdbException exception)
        {
            _androidSerial = null;
            _connectedPhone = null;
            ConnectionTitle = "Connexion Android impossible";
            ConnectionDetail = exception.Message;
            AndroidTaskManagerDetail = exception.Message;
        }
        catch (Exception)
        {
            _androidSerial = null;
            _connectedPhone = null;
            ConnectionTitle = "Recherche Android interrompue";
            ConnectionDetail = "ResurectPhone n’a pas pu consulter ADB. Reconnectez le câble USB puis réessayez.";
            AndroidTaskManagerDetail = ConnectionDetail;
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanReleaseAndroidMemory));
            ReleaseAndroidMemoryCommand.RaiseCanExecuteChanged();
            RefreshFeatures();
        }
    }

    private async Task ConnectN9Async()
    {
        var previousTitle = ConnectionTitle;
        var previousDetail = ConnectionDetail;
        IsConnectingN9 = true;
        try
        {
            N9ConnectionStatus status;
            if (_n9Connection.HasPairing)
            {
                ConnectionTitle = "Connexion au Nokia N9…";
                ConnectionDetail = "Vérification de la liaison développeur et de l’empreinte enregistrée.";
                status = await _n9Connection.GetStatusAsync();
            }
            else
            {
                var password = _n9PairingInteraction.RequestTemporaryPassword();
                if (password is null)
                {
                    ConnectionTitle = previousTitle;
                    ConnectionDetail = previousDetail;
                    return;
                }

                ConnectionTitle = "Appairage du Nokia N9…";
                ConnectionDetail = "Connexion au compte developer et vérification de l’identité du téléphone.";
                status = await _n9Connection.PairAsync(
                    password,
                    _n9PairingInteraction.ConfirmHostKey);
            }

            if (!status.IsReachable || !status.IsHarmattan)
            {
                ConnectionTitle = status.IsPaired ? "Nokia N9 appairé" : "Connexion impossible";
                ConnectionDetail = status.Detail;
                return;
            }

            var details = await _n9Connection.ReadDeviceDetailsAsync();
            var existingId = _connectedPhone?.Platform == PhonePlatform.MeeGoHarmattan
                ? _connectedPhone.DeviceId
                : "n9-sdk-usb";
            _connectedPhone = new DetectedPhone(
                existingId,
                string.IsNullOrWhiteSpace(details.ProductName) ? "Nokia N9" : details.ProductName,
                PhonePlatform.MeeGoHarmattan,
                details.SystemName,
                details.SystemVersion,
                details.SystemBuild,
                details.ProductCode,
                PhoneCapability.ReadIdentity | PhoneCapability.ReadFirmware);
            ConnectionTitle = _connectedPhone.DisplayName;
            ConnectionDetail = DescribeN9(_connectedPhone, details);
        }
        catch (N9ConnectionException exception)
        {
            ConnectionTitle = "Connexion au N9 impossible";
            ConnectionDetail = exception.Message;
        }
        catch (Exception)
        {
            ConnectionTitle = "Connexion au N9 interrompue";
            ConnectionDetail = "ResurectPhone n’a pas pu lire le téléphone. Vérifiez SDK Connectivity et reconnectez le câble USB.";
        }
        finally
        {
            IsConnectingN9 = false;
            OnPropertyChanged(nameof(N9ConnectionButtonText));
            OnPropertyChanged(nameof(CanForgetN9Pairing));
            ForgetN9PairingCommand.RaiseCanExecuteChanged();
            RefreshFeatures();
        }
    }

    private void ForgetN9Pairing()
    {
        if (!_n9PairingInteraction.ConfirmForgetPairing())
            return;

        _n9Connection.ForgetPairing();
        ConnectionTitle = "Liaison N9 oubliée";
        ConnectionDetail = "La clé privée et l’empreinte du téléphone ont été supprimées de ce PC.";
        OnPropertyChanged(nameof(N9ConnectionButtonText));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
    }

    private void ToggleAndroidMonitoring()
    {
        if (IsAndroidMonitoring)
        {
            StopAndroidMonitoring();
            AndroidTaskManagerDetail = "Surveillance arrêtée. La dernière mesure reste affichée.";
            return;
        }

        StartAndroidMonitoring();
    }

    private void StartAndroidMonitoring()
    {
        if (!IsTaskManagerView)
            return;
        if (_androidSerial is null)
        {
            AndroidTaskManagerDetail = "Recherchez d’abord un téléphone Android autorisé pour le débogage USB.";
            return;
        }

        StopAndroidMonitoring();
        var cancellation = new CancellationTokenSource();
        _androidMonitorCancellation = cancellation;
        IsAndroidMonitoring = true;
        AndroidTaskManagerDetail = "Première mesure en cours… Le pourcentage CPU apparaîtra au second relevé.";
        _ = MonitorAndroidAsync(_androidSerial, cancellation);
    }

    private async Task MonitorAndroidAsync(string serial, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var snapshot = await _androidTaskManager.CaptureAsync(serial, cancellation.Token);
                if (_androidSerial != serial || !IsTaskManagerView)
                    return;
                ApplyAndroidSnapshot(snapshot);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (AndroidAdbException exception)
        {
            AndroidTaskManagerDetail = exception.Message;
        }
        catch (Exception)
        {
            AndroidTaskManagerDetail = "La lecture en temps réel a été interrompue. Reconnectez le téléphone puis réessayez.";
        }
        finally
        {
            if (ReferenceEquals(_androidMonitorCancellation, cancellation))
            {
                _androidMonitorCancellation = null;
                IsAndroidMonitoring = false;
            }
            cancellation.Dispose();
        }
    }

    private void StopAndroidMonitoring()
    {
        var cancellation = _androidMonitorCancellation;
        _androidMonitorCancellation = null;
        if (cancellation is not null)
            cancellation.Cancel();
        IsAndroidMonitoring = false;
    }

    private async Task ReleaseAndroidMemoryAsync()
    {
        var serial = _androidSerial;
        if (serial is null || !_androidInteraction.ConfirmMemoryRelease())
            return;

        var resumeMonitoring = IsAndroidMonitoring;
        StopAndroidMonitoring();
        IsReleasingMemory = true;
        AndroidTaskManagerDetail = "Android ferme les applications autorisées en arrière-plan, puis ResurectPhone mesure le résultat…";
        try
        {
            var result = await _androidTaskManager.ReleaseMemoryAsync(serial);
            if (_androidSerial != serial)
                return;
            ApplyAndroidSnapshot(result.Snapshot);
            AndroidTaskManagerDetail = result.ReleasedMemoryBytes > 0
                ? $"{FormatBytes(result.ReleasedMemoryBytes)} de mémoire vive ont été récupérés."
                : "Android n’a pas libéré de mémoire supplémentaire ; le système était déjà optimisé.";
        }
        catch (AndroidAdbException exception)
        {
            AndroidTaskManagerDetail = exception.Message;
        }
        catch (Exception)
        {
            AndroidTaskManagerDetail = "L’optimisation de la mémoire a été interrompue sans modifier les données du téléphone.";
        }
        finally
        {
            IsReleasingMemory = false;
            if (resumeMonitoring && _androidSerial == serial && IsTaskManagerView)
                StartAndroidMonitoring();
        }
    }

    private void ApplyAndroidSnapshot(AndroidProcessSnapshot snapshot)
    {
        _latestAndroidProcesses = snapshot.Processes;
        AndroidCpuText = snapshot.CpuUsagePercent is { } cpu
            ? cpu.ToString("0.0' %'", CultureInfo.CurrentCulture)
            : "Mesure…";
        AndroidMemoryText = snapshot.TotalMemoryBytes > 0
            ? $"{FormatBytes(snapshot.UsedMemoryBytes)} / {FormatBytes(snapshot.TotalMemoryBytes)}"
            : "Indisponible";
        AndroidAvailableMemoryText = snapshot.TotalMemoryBytes > 0
            ? FormatBytes(snapshot.AvailableMemoryBytes)
            : "Indisponible";
        AndroidProcessCountText = snapshot.Processes.Count.ToString(CultureInfo.CurrentCulture);
        AndroidLastRefreshText = $"Actualisé à {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
        AndroidTaskManagerDetail = snapshot.Detail;
        ApplyAndroidFilter();
    }

    private void ApplyAndroidFilter()
    {
        var filter = AndroidFilter.Trim();
        var processes = string.IsNullOrWhiteSpace(filter)
            ? _latestAndroidProcesses
            : _latestAndroidProcesses.Where(process =>
                process.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                process.User.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                process.CommandLine.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                process.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal));

        AndroidProcesses.Clear();
        foreach (var process in processes)
            AndroidProcesses.Add(new(process));
    }

    public void Shutdown() => StopAndroidMonitoring();

    private void ResetConnectionTextForFamily(string familyTitle)
    {
        if (_connectedPhone is not null)
        {
            ConnectionTitle = _connectedPhone.DisplayName;
            ConnectionDetail = Describe(_connectedPhone);
            return;
        }

        ConnectionTitle = "Aucun téléphone détecté";
        ConnectionDetail = familyTitle switch
        {
            "Android" => "Activez le débogage USB, branchez un téléphone Android puis lancez la recherche.",
            "Nokia N9" => "Branchez un Nokia N9 en USB ou utilisez l’appairage développeur.",
            _ => "Branchez un Windows Phone en USB, puis lancez la recherche."
        };
    }

    private void RefreshFeatures()
    {
        SelectedFeatures.Clear();
        if (IsHome)
            return;

        var features = SelectedSection.Area is { } area
            ? RecoveryCatalog.ForAreaAndPlatforms(area, SelectedSection.Platforms)
            : RecoveryCatalog.ForPlatforms(SelectedSection.Platforms);
        foreach (var feature in features)
            SelectedFeatures.Add(new RecoveryFeatureViewModel(feature, _connectedPhone));
    }

    private static string Describe(DetectedPhone phone)
    {
        var details = new[]
        {
            phone.SystemName,
            phone.SystemVersion,
            string.IsNullOrWhiteSpace(phone.BuildNumber) ? null : $"build {phone.BuildNumber}"
        };
        var description = string.Join(" · ", details.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(description)
            ? "Téléphone reconnu. L’identification détaillée reste à effectuer."
            : description;
    }

    private static string DescribeN9(DetectedPhone phone, N9DeviceDetails details)
    {
        var values = new[]
        {
            phone.SystemName,
            phone.SystemVersion,
            string.IsNullOrWhiteSpace(phone.BuildNumber) ? null : phone.BuildNumber,
            string.IsNullOrWhiteSpace(phone.ProductCode) ? null : phone.ProductCode,
            string.IsNullOrWhiteSpace(details.KernelVersion) ? null : $"noyau {details.KernelVersion}"
        };
        return string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
