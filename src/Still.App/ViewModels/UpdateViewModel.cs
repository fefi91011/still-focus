using System.Windows.Input;
using Still.App.Services;
using Still.Core.Runtime;

namespace Still.App.ViewModels;

/// <summary>
/// Compares the installed version with the newest GitHub release. A newer
/// release shows a dismissible banner and a Settings card; nothing is
/// downloaded automatically.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private readonly StillRuntime _runtime;

    private bool _isChecking;
    private bool _isUpdateAvailable;
    private bool _isBannerVisible;
    private bool _dismissed;
    private string _statusText;
    private string _detailText = string.Empty;
    private string _bannerText = string.Empty;
    private string _latestVersion = string.Empty;
    private string _releaseUrl = UpdateService.ReleasesPage;
    private string _lastCheckedText = "Noch nicht geprüft";

    public UpdateViewModel(StillRuntime runtime, string currentVersion)
    {
        _runtime = runtime;
        CurrentVersion = currentVersion;
        _statusText = $"Installiert: {currentVersion}";

        CheckCommand = new AsyncRelayCommand(CheckAsync, _ =>
            StatusText = "Prüfung gerade nicht möglich");
        OpenReleaseCommand = new RelayCommand(() => _runtime.OpenExternal(ReleaseUrl));
        DismissCommand = new RelayCommand(() =>
        {
            IsBannerVisible = false;
            _dismissed = true;
        });
    }

    public string CurrentVersion { get; }
    public string LatestVersion => _latestVersion;
    public string ReleaseUrl => _releaseUrl;

    public ICommand CheckCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public ICommand DismissCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set => Set(ref _isChecking, value);
    }

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
        if (!_runtime.State.Settings.CheckForUpdates)
        {
            StatusText = "Automatische Prüfung ist aus";
            DetailText = string.Empty;
            return;
        }

        await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        StatusText = "Prüfe auf Updates …";
        try
        {
            var update = await UpdateService.CheckAsync(CurrentVersion);
            LastCheckedText = $"Zuletzt geprüft: {DateTimeOffset.Now:dd.MM., HH:mm}";

            if (update is null)
            {
                IsUpdateAvailable = false;
                IsBannerVisible = false;
                StatusText = $"Aktuell ({CurrentVersion})";
                DetailText = "Du nutzt die neueste Version.";
                return;
            }

            _latestVersion = update.Version;
            _releaseUrl = update.PageUrl;
            IsUpdateAvailable = true;
            IsBannerVisible = !_dismissed;
            BannerText = $"Still {update.Version} ist verfügbar";
            StatusText = $"Version {update.Version} ist verfügbar";
            DetailText = $"Du nutzt {CurrentVersion}. Der Installer ersetzt die App, deine Daten bleiben erhalten.";
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
}
