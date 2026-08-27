namespace BreathedgeCoopLauncher.Models;

public sealed class LauncherSettings
{
    public string GamePath { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public string Mode { get; set; } = "Host";
    public string InstalledModVersion { get; set; } = "Not installed";
}
