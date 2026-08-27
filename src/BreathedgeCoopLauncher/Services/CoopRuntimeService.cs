using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BreathedgeCoopLauncher.Models;

namespace BreathedgeCoopLauncher.Services;

public sealed class CoopRuntimeService
{
    public const string UpdateChannelName = "latest-test";
    public const string ProbeModName = "BreathedgeCoopProbe";
    public const string NativeModName = "BreathedgeCoopNative";

    public static string GetModsPath(string gamePath) => Path.Combine(gamePath,
        "Breathedge", "Binaries", "Win64", "Mods");

    public static string GetProbePath(string gamePath) => Path.Combine(GetModsPath(gamePath), ProbeModName);

    public IReadOnlyList<string> CheckPrerequisites(string gamePath)
    {
        var missing = new List<string>();
        string binaries = Path.Combine(gamePath, "Breathedge", "Binaries", "Win64");
        string probe = GetProbePath(gamePath);
        if (!File.Exists(Path.Combine(binaries, "UE4SS.dll"))) missing.Add("UE4SS.dll");
        if (!File.Exists(Path.Combine(probe, "Scripts", "main.lua"))) missing.Add("BreathedgeCoopProbe/Scripts/main.lua");
        if (!File.Exists(Path.Combine(probe, "BreathedgeCoopRelayTCP.exe"))) missing.Add("BreathedgeCoopRelayTCP.exe");
        if (!File.Exists(Path.Combine(GetModsPath(gamePath), NativeModName, "dlls", "main.dll")))
            missing.Add("BreathedgeCoopNative/dlls/main.dll");
        if (!File.Exists(Path.Combine(gamePath, "Breathedge", "Content", "Paks", "~mods", "BreathedgeCoopProxy_P.pak")))
            missing.Add("BreathedgeCoopProxy_P.pak");
        return missing;
    }

    public Process StartRelay(string gamePath, LaunchMode mode, string hostAddress)
    {
        IReadOnlyList<string> missing = CheckPrerequisites(gamePath);
        if (missing.Count != 0)
            throw new InvalidOperationException("The co-op runtime is incomplete. Missing: " + string.Join(", ", missing));

        string probe = GetProbePath(gamePath);
        string role = mode == LaunchMode.Host ? "host" : "client";
        File.WriteAllText(Path.Combine(probe, "role.txt"), role + Environment.NewLine);
        EnableRequiredMods(GetModsPath(gamePath));

        var info = new ProcessStartInfo(Path.Combine(probe, "BreathedgeCoopRelayTCP.exe"))
        {
            WorkingDirectory = probe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("--role"); info.ArgumentList.Add(role);
        info.ArgumentList.Add("--dir"); info.ArgumentList.Add(probe);
        if (mode == LaunchMode.Join)
        {
            info.ArgumentList.Add("--peer"); info.ArgumentList.Add(hostAddress);
        }
        return Process.Start(info) ?? throw new InvalidOperationException("The co-op relay could not be started.");
    }

    public static string? FindRadminAddress()
    {
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            bool likelyRadmin = adapter.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)
                || adapter.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase);
            foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte first = address.Address.GetAddressBytes()[0];
                if (likelyRadmin || first == 26) return address.Address.ToString();
            }
        }
        return null;
    }

    public static bool IsValidHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value.Any(char.IsWhiteSpace)) return false;
        if (IPAddress.TryParse(value, out IPAddress? ip))
            return ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip);
        return Uri.CheckHostName(value) == UriHostNameType.Dns;
    }

    private static void EnableRequiredMods(string modsPath)
    {
        string file = Path.Combine(modsPath, "mods.txt");
        if (!File.Exists(file)) throw new FileNotFoundException("UE4SS mods.txt was not found.", file);
        var lines = File.ReadAllLines(file).ToList();
        SetEnabled(lines, ProbeModName);
        SetEnabled(lines, NativeModName);
        File.WriteAllLines(file, lines);
    }

    private static void SetEnabled(List<string> lines, string modName)
    {
        int index = lines.FindIndex(line => line.TrimStart().StartsWith(modName + " ", StringComparison.OrdinalIgnoreCase));
        if (index < 0) lines.Add($"{modName} : 1");
        else lines[index] = $"{modName} : 1";
    }
}
