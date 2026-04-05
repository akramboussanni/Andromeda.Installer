namespace Andromeda.Installer.ViewModels;

public class DetailsViewModel(GameModel game) : ViewModelBase
{
    private bool _installing;
    private bool _offline;
    private bool _linuxInstructions;
    private bool _macOSInstructions;
    private string _andromedaStatusText = "Checking Andromeda version...";
    private bool _andromedaEnabled = true;
    private bool _showConsoleWindow = false;
    private string _optionsStatusText = string.Empty;
    private bool _andromedaInstalled;
    private bool _andromedaUpdateAvailable;
    private bool _bleedingEdgeEnabled = AndromedaManager.BleedingEdgeEnabled;
    private AndromedaVersion? _selectedAndromedaVersion;
    private bool _andromedaVersionsLoaded;

    public GameModel Game => game;

    public bool Installing
    {
        get => _installing;
        set
        {
            _installing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnableSettings));
        }
    }

    public bool Offline
    {
        get => _offline;
        set
        {
            _offline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnableSettings));
        }
    }

    public bool LinuxInstructions
    {
        get => _linuxInstructions;
        set
        {
            _linuxInstructions = value;
            OnPropertyChanged();
        }
    }

    public bool MacOSInstructions
    {
        get => _macOSInstructions;
        set
        {
            _macOSInstructions = value;
            OnPropertyChanged();
        }
    }

    public bool SupportsAndromeda => AndromedaManager.ShouldInstall(game.Dir);

    public string AndromedaStatusText
    {
        get => _andromedaStatusText;
        set
        {
            _andromedaStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool AndromedaEnabled
    {
        get => _andromedaEnabled;
        set
        {
            _andromedaEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShowConsoleWindow
    {
        get => _showConsoleWindow;
        set
        {
            _showConsoleWindow = value;
            OnPropertyChanged();
        }
    }

    public string OptionsStatusText
    {
        get => _optionsStatusText;
        set
        {
            _optionsStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool AndromedaInstalled
    {
        get => _andromedaInstalled;
        set
        {
            _andromedaInstalled = value;
            OnPropertyChanged();
        }
    }

    public bool AndromedaUpdateAvailable
    {
        get => _andromedaUpdateAvailable;
        set
        {
            _andromedaUpdateAvailable = value;
            OnPropertyChanged();
        }
    }

    public bool BleedingEdgeEnabled
    {
        get => _bleedingEdgeEnabled;
        set
        {
            _bleedingEdgeEnabled = value;
            AndromedaManager.BleedingEdgeEnabled = value;
            OnPropertyChanged();
        }
    }

    public AndromedaVersion? SelectedAndromedaVersion
    {
        get => _selectedAndromedaVersion;
        set
        {
            _selectedAndromedaVersion = value;
            OnPropertyChanged();
        }
    }

    private Semver.SemVersion? _installedAndromedaVersion;
    public Semver.SemVersion? InstalledAndromedaVersion
    {
        get => _installedAndromedaVersion;
        set
        {
            _installedAndromedaVersion = value;
            OnPropertyChanged();
        }
    }

    public bool AndromedaVersionsLoaded
    {
        get => _andromedaVersionsLoaded;
        set
        {
            _andromedaVersionsLoaded = value;
            OnPropertyChanged();
        }
    }

    public bool EnableSettings => !Offline && !Installing;
}
