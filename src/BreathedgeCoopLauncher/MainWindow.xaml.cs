using System.Diagnostics;
using System.Windows;
using BreathedgeCoopLauncher.Models;
using BreathedgeCoopLauncher.Services;
using Microsoft.Win32;

namespace BreathedgeCoopLauncher;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GameLocator _locator = new();
    private readonly UpdateService _updater = new();
    private readonly CoopRuntimeService _runtime = new();
    private readonly LauncherSettings _settings;
    private Process? _relayProcess;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        GamePathBox.Text = _settings.GamePath;
        ServerBox.Text = _settings.ServerAddress;
        JoinModeButton.IsChecked = string.Equals(_settings.Mode, "Join", StringComparison.OrdinalIgnoreCase);
        HostModeButton.IsChecked = JoinModeButton.IsChecked != true;
        VersionText.Text = _settings.InstalledModVersion;
        Loaded += (_, _) =>
        {
            UpdateModeUi();
            if (!GameLocator.IsGameFolder(GamePathBox.Text)) DetectGame();
            else RefreshRuntimeStatus();
        };
        Closed += (_, _) => StopRelay();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Detect_Click(object sender, RoutedEventArgs e) => DetectGame();

    private void DetectGame()
    {
        StatusText.Text = "Scanning Steam and Epic Games installations…";
        string? path = _locator.FindGame();
        if (path is null) { StatusText.Text = "Breathedge was not detected. Select its folder manually."; return; }
        GamePathBox.Text = path;
        StatusText.Text = "Breathedge installation detected.";
        SaveSettings();
        RefreshRuntimeStatus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the Breathedge installation folder", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (!GameLocator.IsGameFolder(dialog.FolderName))
        {
            MessageBox.Show(this, "That folder does not contain Breathedge.exe or its Win64 shipping executable.",
                "Invalid game folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        GamePathBox.Text = dialog.FolderName;
        StatusText.Text = "Game folder selected.";
        SaveSettings();
        RefreshRuntimeStatus();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateModeUi();
        SaveSettings();
    }

    private void UpdateModeUi()
    {
        bool joining = JoinModeButton.IsChecked == true;
        AddressLabel.Text = joining ? "HOST'S RADMIN VPN IP" : "YOUR RADMIN VPN IP (SHARE THIS WITH PLAYER 2)";
        ServerBox.IsReadOnly = !joining;
        DetectRadminButton.Visibility = joining ? Visibility.Collapsed : Visibility.Visible;
        if (!joining) DetectRadminAddress();
        StatusText.Text = joining
            ? "Enter the host PC's 26.x.x.x Radmin VPN address."
            : "Start Host Game first, then give this Radmin address to Player 2.";
    }

    private void DetectRadmin_Click(object sender, RoutedEventArgs e) => DetectRadminAddress();

    private void DetectRadminAddress()
    {
        string? address = CoopRuntimeService.FindRadminAddress();
        if (address is null)
        {
            ServerBox.Text = "";
            StatusText.Text = "Radmin VPN is not connected or its IPv4 adapter was not found.";
            return;
        }
        ServerBox.Text = address;
        StatusText.Text = $"Radmin VPN detected: {address}";
    }

    private void RefreshRuntimeStatus()
    {
        if (!GameLocator.IsGameFolder(GamePathBox.Text)) return;
        IReadOnlyList<string> missing = _runtime.CheckPrerequisites(GamePathBox.Text);
        if (missing.Count == 0)
        {
            VersionText.Text = _settings.InstalledModVersion == "Not installed"
                ? $"{CoopRuntimeService.TargetRuntimeVersion} · local test runtime"
                : _settings.InstalledModVersion;
            StatusText.Text = $"UE4SS co-op probe, native plugin, and TCP relay are ready for the {CoopRuntimeService.TargetRuntimeVersion} test.";
        }
        else
        {
            VersionText.Text = "Runtime incomplete";
            StatusText.Text = "Missing: " + string.Join(", ", missing);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!GameLocator.IsGameFolder(GamePathBox.Text))
        {
            MessageBox.Show(this, "Choose a valid Breathedge installation folder first.");
            return;
        }
        SetBusy(true, "Checking for the latest co-op build…");
        try
        {
            UpdateManifest manifest = await _updater.GetManifestAsync();
            if (!string.Equals(manifest.Version, CoopRuntimeService.TargetRuntimeVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"This launcher targets {CoopRuntimeService.TargetRuntimeVersion}, but the update feed returned {manifest.Version}.");
            StatusText.Text = $"Downloading co-op mod {manifest.Version}…";
            var progress = new Progress<double>(value => UpdateProgress.Value = value * 100);
            await _updater.InstallAsync(manifest, GamePathBox.Text, progress);
            _settings.InstalledModVersion = manifest.Version;
            VersionText.Text = manifest.Version;
            SaveSettings();
            StatusText.Text = string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                ? $"Co-op mod {manifest.Version} is installed."
                : $"Installed {manifest.Version}: {manifest.ReleaseNotes}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update failed.";
            MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out string server)) return;
        try
        {
            SaveSettings();
            StopRelay();
            LaunchMode mode = JoinModeButton.IsChecked == true ? LaunchMode.Join : LaunchMode.Host;
            _relayProcess = _runtime.StartRelay(GamePathBox.Text, mode, server);
            GameLauncher.Launch(GamePathBox.Text);
            StatusText.Text = mode == LaunchMode.Host
                ? $"Host relay started. Share {server} with Player 2. In game, press F9 then F4."
                : $"Connecting relay to {server}. In game, press F9 then F4.";
        }
        catch (Exception ex)
        {
            StopRelay();
            MessageBox.Show(this, ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ValidateInputs(out string server)
    {
        server = ServerBox.Text.Trim();
        if (!GameLocator.IsGameFolder(GamePathBox.Text))
        {
            MessageBox.Show(this, "Choose a valid Breathedge installation folder first."); return false;
        }
        if (JoinModeButton.IsChecked == true && !CoopRuntimeService.IsValidHost(server))
        {
            MessageBox.Show(this, "Enter the host PC's valid Radmin VPN IPv4 address."); return false;
        }
        if (HostModeButton.IsChecked == true && !CoopRuntimeService.IsValidHost(server))
        {
            MessageBox.Show(this, "Connect Radmin VPN, then detect this PC's Radmin IPv4 address."); return false;
        }
        IReadOnlyList<string> missing = _runtime.CheckPrerequisites(GamePathBox.Text);
        if (missing.Count != 0)
        {
            MessageBox.Show(this, "Install the complete co-op runtime first. Missing: " + string.Join(", ", missing));
            return false;
        }
        return true;
    }

    private void SaveSettings()
    {
        _settings.GamePath = GamePathBox.Text;
        _settings.ServerAddress = ServerBox.Text.Trim();
        _settings.Mode = JoinModeButton.IsChecked == true ? "Join" : "Host";
        _settingsService.Save(_settings);
    }

    private void StopRelay()
    {
        try
        {
            if (_relayProcess is { HasExited: false }) _relayProcess.Kill(true);
        }
        catch { /* Relay may already have exited with the game. */ }
        finally { _relayProcess?.Dispose(); _relayProcess = null; }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) UpdateProgress.Value = 0;
        if (message is not null) StatusText.Text = message;
        IsEnabled = !busy;
    }
}
