using System.Diagnostics;

namespace BreathedgeCoopLauncher.Services;

public static class GameLauncher
{
    public static void Launch(string gamePath)
    {
        string executable = GameLocator.FindExecutable(gamePath)
            ?? throw new FileNotFoundException("Breathedge executable was not found.");

        var startInfo = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start Breathedge.");
    }
}
