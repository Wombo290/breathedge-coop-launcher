using System.Text.Json;
using BreathedgeCoopLauncher.Models;

namespace BreathedgeCoopLauncher.Services;

public sealed class SettingsService
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreathedgeCoopLauncher");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public LauncherSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
