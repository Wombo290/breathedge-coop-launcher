using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace BreathedgeCoopLauncher.Services;

public sealed partial class GameLocator
{
    private const string SteamAppId = "738520";

    public string? FindGame()
    {
        foreach (string candidate in FindSteamCandidates().Concat(FindEpicCandidates()).Distinct())
            if (IsGameFolder(candidate)) return candidate;
        return null;
    }

    public static bool IsGameFolder(string path) => FindExecutable(path) is not null;

    public static string? FindExecutable(string gamePath)
    {
        if (!Directory.Exists(gamePath)) return null;
        string[] expected =
        {
            Path.Combine(gamePath, "Breathedge.exe"),
            Path.Combine(gamePath, "Breathedge", "Binaries", "Win64", "Breathedge-Win64-Shipping.exe")
        };
        return expected.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindSteamCandidates()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string path) roots.Add(path);
        }
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            if (key?.GetValue("SteamPath") is string path) roots.Add(path.Replace('/', '\\'));

        foreach (string steamRoot in roots.ToArray())
        {
            string librariesFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(librariesFile))
                foreach (Match match in VdfPathRegex().Matches(File.ReadAllText(librariesFile)))
                    roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }

        foreach (string root in roots)
        {
            string manifest = Path.Combine(root, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;
            Match directory = InstallDirectoryRegex().Match(File.ReadAllText(manifest));
            if (directory.Success) yield return Path.Combine(root, "steamapps", "common", directory.Groups[1].Value);
        }
    }

    private static IEnumerable<string> FindEpicCandidates()
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (string file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string json;
            try { json = File.ReadAllText(file); } catch { continue; }
            if (!json.Contains("Breathedge", StringComparison.OrdinalIgnoreCase)) continue;
            Match location = EpicLocationRegex().Match(json);
            if (location.Success) yield return location.Groups[1].Value.Replace("\\\\", "\\");
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirectoryRegex();
    [GeneratedRegex("\\\"InstallLocation\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex EpicLocationRegex();
}
