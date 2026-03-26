namespace Andromeda.Installer.ViewModels;

public class DetailsViewModel(GameModel game) : ViewModelBase
{
    private bool _installing;
    private bool _offline;
    private bool _linuxInstructions;
    private bool _macOSInstructions;
    private string _andromedaStatusText = "Checking Andromeda version...";
    private bool _melonConsoleEnabled;
    private bool _hideConsoleWindow = true;
    private string _optionsStatusText = string.Empty;
    private bool _andromedaInstalled;
    private bool _andromedaUpdateAvailable;

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

    public bool MelonConsoleEnabled
    {
        get => _melonConsoleEnabled;
        set
        {
            _melonConsoleEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool HideConsoleWindow
    {
        get => _hideConsoleWindow;
        set
        {
            _hideConsoleWindow = value;
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

    public bool EnableSettings => !Offline && !Installing;
}
