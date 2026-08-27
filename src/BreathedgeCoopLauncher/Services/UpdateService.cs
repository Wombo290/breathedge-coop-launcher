using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using BreathedgeCoopLauncher.Models;

namespace BreathedgeCoopLauncher.Services;

public sealed class UpdateService
{
    // Replace this with your HTTPS-hosted manifest URL before distributing the launcher.
    public const string ManifestUrl = "https://raw.githubusercontent.com/Wombo290/breathedge-coop-launcher/main/examples/latest.json";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<UpdateManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        EnsureHttps(ManifestUrl);
        await using Stream stream = await _http.GetStreamAsync(ManifestUrl, cancellationToken);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The update manifest is empty or invalid.");
    }

    public async Task InstallAsync(UpdateManifest manifest, string gamePath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureHttps(manifest.DownloadUrl);
        ValidateManifest(manifest);
        string tempRoot = Path.Combine(Path.GetTempPath(), "BreathedgeCoopLauncher", Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(tempRoot, "mod.zip");
        string stagingPath = Path.Combine(tempRoot, "staging");
        Directory.CreateDirectory(stagingPath);
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(manifest.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream output = File.Create(archivePath))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    readTotal += read;
                    if (total > 0) progress?.Report(readTotal * 0.70 / total.Value);
                }
            }

            string actualHash;
            await using (FileStream hashStream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash), Convert.FromHexString(manifest.Sha256)))
                throw new CryptographicException("Downloaded mod hash does not match the signed release manifest.");

            ExtractSafely(archivePath, stagingPath);
            progress?.Report(0.80);
            InstallStagedFiles(stagingPath, gamePath);
            progress?.Report(1.0);
        }
        finally
        {
            // Cleanup must never turn an otherwise successful installation into an
            // "update failed" result. Windows scanners can also hold a newly
            // downloaded archive briefly after all launcher streams are closed.
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ExtractSafely(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in update archive: {entry.FullName}");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    // The ZIP mirrors paths relative to the game root, e.g. Breathedge/Content/Paks/MyMod.pak.
    private static void InstallStagedFiles(string stagingPath, string gamePath)
    {
        string backupRoot = Path.Combine(gamePath, ".coop-launcher-backup", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        foreach (string source in Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(stagingPath, source);
            // Prototype archives place Mods relative to the Win64 executable,
            // while their Breathedge/Content paths are relative to the game root.
            if (relative.StartsWith("Mods" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                relative = Path.Combine("Breathedge", "Binaries", "Win64", relative);
            else if (relative.Equals("INSTALL.txt", StringComparison.OrdinalIgnoreCase)
                     || relative.Equals("ADD-TO-MODS-TXT.txt", StringComparison.OrdinalIgnoreCase))
                continue;
            string destination = Path.GetFullPath(Path.Combine(gamePath, relative));
            string gameRoot = Path.GetFullPath(gamePath) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An update attempted to write outside the game folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                string backup = Path.Combine(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, true);
            }
            File.Copy(source, destination, true);
        }
    }

    private static void EnsureHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Update URLs must use HTTPS.");
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Sha256.Length != 64)
            throw new InvalidDataException("The update manifest is missing a version or valid SHA-256 hash.");
        _ = Convert.FromHexString(manifest.Sha256);
    }
}
