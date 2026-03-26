using Andromeda.Installer.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;
using System.Diagnostics;

namespace Andromeda.Installer.Views;

public partial class DetailsView : UserControl
{
    public DetailsViewModel? Model => (DetailsViewModel?)DataContext;

    public DetailsView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (Model == null)
            return;

        Model.Game.PropertyChanged -= PropertyChangedHandler;
    }

    private void PropertyChangedHandler(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == "MLVersion")
        {
            UpdateVersionInfo();
        }
    }

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (Model == null)
            return;

#if LINUX
        if (Model.Game.Arch == Architecture.LinuxX64)
        {
            LdLibPathVar.Text = $"LD_LIBRARY_PATH=\"{Model.Game.Dir}:$LD_LIBRARY_PATH\"";
            LdSteamLaunchOptions.Text = $"{LdLibPathVar.Text} {LdPreloadVar.Text} %command%";
        }
        
        ShowLinuxInstructions.IsVisible = Model.Game.MLInstalled;
#elif OSX
        if (Model.Game.Arch == Architecture.MacOSX64)
        {
            DylibPathVar.Text = $"DYLD_LIBRARY_PATH=\"{Model.Game.Dir}:$DYLD_LIBRARY_PATH\"";
            DylibSteamLaunchOptions.Text = $"{DylibPathVar.Text} {DylibPreloadVar.Text} %command%";
        }

        ShowMacOSInstructions.IsVisible = Model.Game.MLInstalled;
#endif

        Model.Game.PropertyChanged += PropertyChangedHandler;

        UpdateVersionList();

        if (!await MLManager.Init())
        {
            Model.Offline = true;
            DialogBox.ShowError("Failed to fetch MelonLoader releases. Ensure you're online.");
        }

        await RefreshAndromedaStatusAsync();
        LoadMelonLoaderOptions();
    }

    private async Task RefreshAndromedaStatusAsync()
    {
        if (Model == null || !Model.SupportsAndromeda)
            return;

        try
        {
            var andromedaVersionInfo = await AndromedaManager.GetVersionInfoAsync(Model.Game.Dir);
            Model.AndromedaStatusText = NormalizeAndromedaStatusText(andromedaVersionInfo.StatusText);
            Model.AndromedaInstalled = andromedaVersionInfo.InstalledVersion != null;
            Model.AndromedaUpdateAvailable = andromedaVersionInfo.InstalledVersion != null
                && andromedaVersionInfo.LatestVersion != null
                && andromedaVersionInfo.InstalledVersion.ComparePrecedenceTo(andromedaVersionInfo.LatestVersion) < 0;
            UpdateVersionInfo();
        }
        catch
        {
            Model.AndromedaStatusText = "Failed to check version";
            Model.AndromedaInstalled = false;
            Model.AndromedaUpdateAvailable = false;
        }
    }

    private static string NormalizeAndromedaStatusText(string statusText)
    {
        const string prefix = "Andromeda: ";
        if (statusText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return statusText[prefix.Length..];

        return statusText;
    }

    private void LoadMelonLoaderOptions()
    {
        if (Model == null)
            return;

        var options = AndromedaManager.GetMelonLoaderOptions(Model.Game.Dir);
        Model.MelonConsoleEnabled = options.ConsoleEnabled;
        Model.HideConsoleWindow = options.HideConsole;
        Model.OptionsStatusText = "Loaded current MelonLoader settings.";
    }

    private void SaveOptionsHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;

        var error = AndromedaManager.SaveMelonLoaderOptions(Model.Game.Dir, new MelonLoaderOptions
        {
            ConsoleEnabled = Model.MelonConsoleEnabled,
            HideConsole = Model.HideConsoleWindow
        });

        if (error != null)
        {
            Model.OptionsStatusText = error;
            DialogBox.ShowError(error);
            return;
        }

        Model.OptionsStatusText = "Saved MelonLoader settings.";
    }

    public void UpdateVersionList()
    {
        if (Model == null)
            return;

        var en = MLManager.Versions.Where(x =>
        Model.Game.Arch switch
        {
            Architecture.MacOSX64 => x.DownloadUrlMacOS,
            Architecture.LinuxX64 => x.DownloadUrlLinux,
            Architecture.WindowsX64 => x.DownloadUrlWin,
            Architecture.WindowsX86 => x.DownloadUrlWinX86,

#if WINDOWS
            _ => x.DownloadUrlWin,
#elif LINUX
            _ => x.DownloadUrlLinux,
#elif OSX
            _ => x.DownloadUrlMacOS,
#endif

        } != null);
        en = en.Where(x => !x.Version.IsPrerelease || x.IsLocalPath);

        VersionCombobox.ItemsSource = en;
        VersionCombobox.SelectedIndex = 0;
    }

    private void BackClickHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;

        if (Model.LinuxInstructions)
        {
            Model.LinuxInstructions = false;
            return;
        }

        if (Model.MacOSInstructions)
        {
            Model.MacOSInstructions = false;
            return;
        }

        if (Model.Installing)
            return;

        MainWindow.Instance.ShowMainView();
    }

    private void VersionSelectHandler(object? sender, SelectionChangedEventArgs args)
    {
        UpdateVersionInfo();
    }

    public void UpdateVersionInfo()
    {
        if (Model == null || VersionCombobox.SelectedItem == null)
            return;

        MelonIcon.Opacity = Model.AndromedaInstalled ? 1 : 0.3;
        UpdateAndromedaButtonLabel();
        UpdateModloaderButtonLabel();
    }

    private void UpdateAndromedaButtonLabel()
    {
        if (Model == null)
            return;

        if (!Model.AndromedaInstalled)
            AndromedaInstallButton.Content = "Install Andromeda";
        else if (Model.AndromedaUpdateAvailable)
            AndromedaInstallButton.Content = "Update Andromeda";
        else
            AndromedaInstallButton.Content = "Reinstall Andromeda";
    }

    private void UpdateModloaderButtonLabel()
    {
        if (Model == null || VersionCombobox.SelectedItem == null)
            return;

        if (Model.Game.MLVersion == null)
        {
            InstallButton.Content = "Install Modloader";
            return;
        }

        var comp = ((MLVersion)VersionCombobox.SelectedItem).Version.CompareSortOrderTo(Model.Game.MLVersion);
        InstallButton.Content = comp switch
        {
            < 0 => "Downgrade Modloader",
            0 => "Reinstall Modloader",
            > 0 => "Upgrade Modloader"
        };
    }

    private void InstallAndromedaHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.Validate(out _))
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        if (AskForElevation())
            return;

        Model.Installing = true;
        ShowLinuxInstructions.IsVisible = false;
        ShowMacOSInstructions.IsVisible = false;

        if (VersionCombobox.SelectedItem is not MLVersion selectedVersion)
        {
            OnOperationFinished("No Modloader version is selected in Options.");
            return;
        }

        // Andromeda depends on MelonLoader, so this path always refreshes MelonLoader first.
        _ = MLManager.InstallAsync(Model.Game.Dir, Model.Game.MLInstalled && !KeepFilesCheck.IsChecked!.Value,
            selectedVersion, Model.Game.Arch,
            (progress, newStatus) => Dispatcher.UIThread.Post(() => OnInstallProgress(progress, newStatus)),
            (errorMessage) => Dispatcher.UIThread.Post(() => OnOperationFinished(errorMessage)));
    }

    private void InstallModloaderHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.Validate(out _))
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        if (AskForElevation())
            return;

        if (VersionCombobox.SelectedItem is not MLVersion selectedVersion)
        {
            DialogBox.ShowError("No Modloader version selected.");
            return;
        }

        Model.Installing = true;
        ShowLinuxInstructions.IsVisible = false;
        ShowMacOSInstructions.IsVisible = false;

        _ = MLManager.InstallAsync(Model.Game.Dir, Model.Game.MLInstalled && !KeepFilesCheck.IsChecked!.Value,
            selectedVersion, Model.Game.Arch,
            (progress, newStatus) => Dispatcher.UIThread.Post(() => OnInstallProgress(progress, newStatus)),
            (errorMessage) => Dispatcher.UIThread.Post(() => OnOperationFinished(errorMessage)));
    }

    public bool AskForElevation()
    {
        var tempPath = Path.Combine(Model!.Game.Dir, "ml.tmp");
        try
        {
            using var file = File.Create(tempPath);
        }
        catch (UnauthorizedAccessException)
        {
            DialogBox.ShowConfirmation(
                "The installation of MelonLoader on this game may require elevated privileges.\nWould you like to restart with elevated privileges?",
                Program.RestartWithElevatedPrivileges);

            return true;
        }
        catch
        {
            return false;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch { }

        return false;
    }

    private void OnInstallProgress(double progress, string? newStatus)
    {
        if (newStatus != null)
        {
            InstallStatus.Text = newStatus;
            if (Model != null)
            {
                Model.OptionsStatusText = newStatus;
            }
        }

        Progress.Value = progress * 100;
        MelonIcon.Opacity = progress * 0.7 + 0.3;
    }

    private void OnOperationFinished(string? errorMessage, bool addedLocalBuild = false, bool andromedaOnly = false)
    {
        if (Model == null)
            return;

        Model.Installing = false;

#if LINUX
        ShowLinuxInstructions.IsVisible = Model.Game.MLInstalled;
#elif OSX
        ShowMacOSInstructions.IsVisible = Model.Game.MLInstalled;
#endif

        if (errorMessage != null)
        {
            DialogBox.ShowError(errorMessage);
            return;
        }

        if (andromedaOnly)
        {
            DialogBox.ShowNotice("SUCCESS!", "Andromeda is now up to date.");
            _ = RefreshAndromedaStatusAsync();
            LoadMelonLoaderOptions();
            return;
        }

        var currentMLVersion = Model.Game.MLVersion;
        Model.Game.Validate(out errorMessage);
        if (errorMessage != null)
        {
            DialogBox.ShowError(errorMessage);
            return;
        }

        if (addedLocalBuild)
        {
            _ = RefreshAndromedaStatusAsync();
            return;
        }

        var isInstall = true;
        var operationType = Model.Game.MLInstalled ? "Installed" : "Uninstalled";
        if (Model.Game.MLInstalled
            && Model.Game.MLVersion != null
            && currentMLVersion != null)
        {
            var comp = Model.Game.MLVersion.CompareSortOrderTo(currentMLVersion);
            isInstall = comp == 0;
            operationType = comp switch
            {
                > 0 => "Upgraded",
                0 => "Reinstalled",
                < 0 => "Downgraded"
            };
        }

        DialogBox.ShowNotice("SUCCESS!", $"Successfully {operationType}{((!Model.Game.MLInstalled || isInstall) ? string.Empty : " to")}\nAndromeda v{(Model.Game.MLInstalled ? Model.Game.MLVersion : currentMLVersion)}");
        _ = RefreshAndromedaStatusAsync();
        LoadMelonLoaderOptions();
    }

    private void OpenDirHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;

        InstallerUtils.OpenFolderInExplorer(Model.Game.Dir);
    }

    private void UninstallAndromedaHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.Validate(out _))
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        if (AskForElevation())
            return;

        var error = AndromedaManager.Uninstall(Model.Game.Dir);
        if (error != null)
        {
            DialogBox.ShowError(error);
            return;
        }

        DialogBox.ShowNotice("SUCCESS!", "Andromeda has been removed.");
        _ = RefreshAndromedaStatusAsync();
    }

    private void UninstallModloaderHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.Validate(out _))
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        if (!Model.Game.MLInstalled || AskForElevation())
            return;

        var error = MLManager.Uninstall(Model.Game.Dir, !KeepFilesCheck.IsChecked!.Value);

        OnOperationFinished(error);
    }

    private async void SelectZipHandler(object sender, TappedEventArgs args)
    {
        if (Model == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Select a zipped MelonLoader version...",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new("ZIP Archive")
                {
                    Patterns = [ "*.zip" ]
                }
            ]
        });

        if (files.Count is 0 or > 1)
            return;

        var path = files[0].Path.LocalPath;

        Model.Installing = true;
        ShowLinuxInstructions.IsVisible = false;

        _ = Task.Run(() => MLManager.SetLocalZip(path,
            (progress, newStatus) => Dispatcher.UIThread.Post(() => OnInstallProgress(progress, newStatus)),
            (errorMessage) => Dispatcher.UIThread.Post(() =>
            {
                if (errorMessage == null)
                {
                    var ver = MLManager.Versions[0];
                    var downloadUrl = Model.Game.Arch switch
                    {
                        Architecture.MacOSX64 => ver.DownloadUrlMacOS,
                        Architecture.LinuxX64 => ver.DownloadUrlLinux,
                        Architecture.WindowsX64 => ver.DownloadUrlWin,
                        Architecture.WindowsX86 => ver.DownloadUrlWinX86,
                        _ => null
                    };
                    if (downloadUrl == null)
                    {
                        DialogBox.ShowError($"The selected version does not support the architecture of the current game: {Model.Game.Arch}");
                    }
                }

                OnOperationFinished(errorMessage, true);
                UpdateVersionList();
            })));
    }

    private void ShowLinuxInstructionsHandler(object sender, TappedEventArgs args)
    {
        if (Model == null)
            return;

        Model.LinuxInstructions = true;
    }

    private void ShowMacOSInstructionsHandler(object sender, TappedEventArgs args)
    {
        if (Model == null)
            return;

        Model.MacOSInstructions = true;
    }
}