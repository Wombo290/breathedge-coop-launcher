# Breathedge Co-op Launcher

A native Windows 10/11 WPF launcher for locating Breathedge, installing verified
co-op mod packages, starting its UE4SS TCP relay, and launching Host or Join sessions.

## Before you build

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. In `UpdateService.cs`, replace `ManifestUrl` with the HTTPS URL of your release
   manifest. A template is provided at `examples/latest.json`.
3. Package the existing `BreathedgeCoopProbe`, `BreathedgeCoopNative`, UE4SS loader,
   and TCP relay in the release ZIP. The launcher deliberately does not perform
   remote DLL injection; it starts the installed UE4SS runtime and relay.

The release ZIP must mirror the game directory. For example:

```text
Breathedge/Binaries/Win64/UE4SS.dll
Breathedge/Binaries/Win64/Mods/BreathedgeCoopProbe/Scripts/main.lua
Breathedge/Binaries/Win64/Mods/BreathedgeCoopProbe/BreathedgeCoopRelayTCP.exe
Breathedge/Binaries/Win64/Mods/BreathedgeCoopNative/dlls/main.dll
```

## Build and run locally

Open PowerShell in the repository root:

```powershell
dotnet restore .\BreathedgeCoopLauncher.slnx
dotnet build .\BreathedgeCoopLauncher.slnx -c Release
dotnet run --project .\src\BreathedgeCoopLauncher\BreathedgeCoopLauncher.csproj
```

Visual Studio 2022 (17.8 or newer) is also supported. Install the **.NET desktop
development** workload, open the solution, choose `Release | x64`, and build.

## Publish a standalone EXE

This creates a Windows x64 build that does not require players to install .NET:

```powershell
dotnet publish .\src\BreathedgeCoopLauncher\BreathedgeCoopLauncher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

Distribute `publish\win-x64\BreathedgeCoopLauncher.exe`. Code-sign it with an
Authenticode certificate before public distribution to reduce SmartScreen warnings.

## Publishing a mod update

1. Build a ZIP whose paths are relative to the Breathedge installation root.
2. Generate its digest:

   ```powershell
   (Get-FileHash .\breathedge-coop-v28-test.zip -Algorithm SHA256).Hash
   ```

3. Upload the immutable ZIP to a GitHub Release or HTTPS object store.
4. Copy `examples/latest.json`, set its version, exact download URL, SHA-256, and
   concise release notes, then publish it at the configured manifest URL.
5. Test the update with a clean game installation and a non-administrator account.

## Update security recommendations

- Keep HTTPS and SHA-256 checks mandatory. Never publish mutable assets under the
  same version or filename.
- For production, sign the canonical manifest with Ed25519 and embed only the public
  key in the launcher. Verify the signature before trusting its URL or hash. SHA-256
  alone detects corruption, but a signature also protects against a compromised host.
- Protect release publishing with MFA, least-privilege tokens, protected tags, and
  required CI approvals. Generate hashes in CI from the exact uploaded artifact.
- Sign both the launcher and native mod DLLs with Authenticode. Do not disable TLS
  validation or antivirus checks.
- Run updates before starting the game. The included installer stages and validates
  the full archive, rejects ZIP traversal, and backs up overwritten files under
  `.coop-launcher-backup`; add an explicit signed file inventory for removal/rollback
  when the mod's package layout stabilizes.
- Publish supported game-build and minimum-launcher versions in the manifest before
  distributing broadly, so incompatible UE4 binaries fail closed.

## Notes

- Steam detection uses Breathedge's Steam app ID (`738520`) and all registered Steam
  libraries. Epic detection reads the launcher's local `.item` manifests.
- Settings are stored in `%LOCALAPPDATA%\BreathedgeCoopLauncher\settings.json`.
- Installing into `Program Files` may require elevation depending on folder ACLs.
- Host mode detects and displays this PC's Radmin VPN IPv4 address. Join mode expects
  the host's Radmin address. The launcher writes `role.txt`, enables both required
  UE4SS mods, starts `BreathedgeCoopRelayTCP.exe`, and then starts Breathedge.
- With the current prototype payload, each player must press **F9** after loading into
  a save to spawn the remote proxy, then **F4** to enable network mode.
- The launcher currently targets the upcoming **v28 test build**. The probe remains experimental; its bundled README explicitly says it
  lacks authentication, encryption, discovery, NAT traversal, and reliable gameplay
  events. Do not describe or distribute it as production-complete co-op until those
  runtime features and full gameplay replication have been implemented and tested.
