using System.Windows.Input;
using Still.App.Services;
using Still.Core.Runtime;

namespace Still.App.ViewModels;

/// <summary>
/// Compares the installed version with the newest GitHub release. Automatic
/// checks run at most once per day; a newer release shows a dismissible banner,
/// and the installer can be downloaded and started right from the app.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    private readonly StillRuntime _runtime;

    private bool _isChecking;
    private bool _isUpdateAvailable;
    private bool _isBannerVisible;
    private bool _dismissed;
    private bool _isInstalling;
    private double _installProgressPercent;
    private string _statusText;
    private string _detailText = string.Empty;
    private string _bannerText = string.Empty;
    private string _latestVersion = string.Empty;
    private string _releaseUrl = UpdateService.ReleasesPage;
    private string _lastCheckedText = "Noch nicht geprüft";
    private UpdateInfo? _pending;

    public UpdateViewModel(StillRuntime runtime, string currentVersion, Action? shutdown = null)
    {
        _runtime = runtime;
        CurrentVersion = currentVersion;
        ShutdownRequested = shutdown;
        _statusText = $"Installiert: {currentVersion}";
        ShowLastChecked();

        CheckCommand = new AsyncRelayCommand(CheckAsync, _ =>
            StatusText = "Prüfung gerade nicht möglich");
        OpenReleaseCommand = new RelayCommand(() => _runtime.OpenExternal(ReleaseUrl));
        DismissCommand = new RelayCommand(() =>
        {
            IsBannerVisible = false;
            _dismissed = true;
        });
        InstallCommand = new AsyncRelayCommand(InstallAsync);
    }

    public string CurrentVersion { get; }
    public string LatestVersion => _latestVersion;
    public string ReleaseUrl => _releaseUrl;

    /// <summary>Set by the main view model; asks before replacing the app.</summary>
    public Func<Task<bool>>? ConfirmInstall { get; set; }

    /// <summary>Quits Still so the installer can replace its files.</summary>
    public Action? ShutdownRequested { get; }

    public ICommand CheckCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand InstallCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (Set(ref _isChecking, value))
            {
                Raise(nameof(CanCheck));
            }
        }
    }

    public bool CanCheck => !IsChecking && !IsInstalling;

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => Set(ref _isUpdateAvailable, value);
    }

    public bool IsBannerVisible
    {
        get => _isBannerVisible;
        private set => Set(ref _isBannerVisible, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (Set(ref _isInstalling, value))
            {
                Raise(nameof(CanCheck));
            }
        }
    }

    public double InstallProgressPercent
    {
        get => _installProgressPercent;
        private set => Set(ref _installProgressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set
        {
            if (Set(ref _detailText, value))
            {
                Raise(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);

    public string BannerText
    {
        get => _bannerText;
        private set => Set(ref _bannerText, value);
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        private set => Set(ref _lastCheckedText, value);
    }

    public async Task CheckOnStartupAsync()
    {
        var last = _runtime.State.Settings.LastUpdateCheckUtc;

        if (!_runtime.State.Settings.CheckForUpdates)
        {
            StatusText = "Automatische Prüfung ist aus";
            DetailText = string.Empty;
            return;
        }

        if (last is { } previous && DateTimeOffset.UtcNow - previous < AutomaticCheckInterval)
        {
            StatusText = $"Installiert: {CurrentVersion}";
            DetailText = "Automatische Prüfung höchstens einmal pro Tag.";
            return;
        }

        await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (IsChecking || IsInstalling)
        {
            return;
        }

        IsChecking = true;
        StatusText = "Prüfe auf Updates …";
        RecordCheck();
        try
        {
            var update = await UpdateService.CheckAsync(CurrentVersion);

            if (update is null)
            {
                _pending = null;
                IsUpdateAvailable = false;
                IsBannerVisible = false;
                StatusText = $"Aktuell ({CurrentVersion})";
                DetailText = "Du nutzt die neueste Version.";
                return;
            }

            _pending = update;
            _latestVersion = update.Version;
            _releaseUrl = update.PageUrl;
            IsUpdateAvailable = true;
            IsBannerVisible = !_dismissed;
            BannerText = $"Still {update.Version} ist verfügbar";
            StatusText = $"Version {update.Version} ist verfügbar";
            DetailText = update.InstallerUrl is not null
                ? $"Du nutzt {CurrentVersion}. Das Update lässt sich direkt hier installieren; deine Daten bleiben erhalten."
                : $"Du nutzt {CurrentVersion}. Dieses Release enthält keinen Installer, öffne die Release-Seite.";
        }
        catch (Exception)
        {
            StatusText = "Prüfung gerade nicht möglich";
            DetailText = "Keine Verbindung zu GitHub. Deine Version läuft normal weiter.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    public async Task InstallAsync()
    {
        if (!IsUpdateAvailable || _pending is null || IsInstalling)
        {
            return;
        }

        if (ConfirmInstall is not null && !await ConfirmInstall())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_pending.InstallerUrl))
        {
            StatusText = "Kein Installer im Release";
            DetailText = "Öffne die Release-Seite und lade den Installer manuell.";
            return;
        }

        IsInstalling = true;
        InstallProgressPercent = 0;
        StatusText = "Lade Installer …";
        DetailText = $"Still {_pending.Version} wird heruntergeladen. Danach beendet sich die App und der Installer übernimmt.";

        try
        {
            var directory = Path.Combine(_runtime.DataDirectory, "updates");
            var path = await UpdateService.DownloadInstallerAsync(
                _pending,
                directory,
                new Progress<double>(value =>
                {
                    InstallProgressPercent = value * 100;
                    StatusText = $"Lade Installer … {value:P0}";
                }));

            StatusText = "Installer gestartet";
            DetailText = "Still beendet sich jetzt. Der Installer ersetzt die App, deine Daten bleiben erhalten.";
            AppInstaller.StartAfterExit(path);

            await Task.Delay(1200);
            ShutdownRequested?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = "Update konnte nicht geladen werden";
            DetailText = ex.Message + " Du kannst den Installer über die Release-Seite laden.";
        }
        finally
        {
            IsInstalling = false;
            InstallProgressPercent = 0;
        }
    }

    private void RecordCheck()
    {
        var now = DateTimeOffset.UtcNow;
        _runtime.RecordUpdateCheck(now);
        LastCheckedText = $"Zuletzt geprüft: {now.ToLocalTime():dd.MM., HH:mm}";
    }

    private void ShowLastChecked()
    {
        var last = _runtime.State.Settings.LastUpdateCheckUtc;
        LastCheckedText = last is { } value
            ? $"Zuletzt geprüft: {value.ToLocalTime():dd.MM., HH:mm}"
            : "Noch nicht geprüft";
    }
}
