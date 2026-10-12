# AutoFete agent instructions

## Git commits

Only create commits when the user explicitly requests a commit. Follow
Conventional Commits 1.0.0-beta.2. Use a lowercase type followed by a colon
and a short imperative subject, without a scope in parentheses. Group changes
into focused commits by behavior or concern, and keep unrelated changes in
separate commits. Use direct subjects without leading articles.

## Build the DLL

The project targets .NET 10 and Dalamud API 15. Build from the repository
root and current source files. `bin/` and `obj/` are generated output.
`AutoFete.csproj` specifies `Dalamud.NET.Sdk/15.0.0`; keep that version for
restore, compilation, and packaging.

On Windows with the .NET 10 SDK and Dalamud API 15 development files:

```powershell
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncher\addon\Hooks\dev"
dotnet restore AutoFete.csproj --locked-mode -p:EnableWindowsTargeting=true
dotnet build AutoFete.csproj -c Release --no-restore -p:EnableWindowsTargeting=true
```

In this WSL workspace, use `tools/build-wsl.sh`. It runs the Windows-hosted
.NET 10 SDK through the Linux .NET 10 runtime and uses the locally installed
Dalamud SDK 15 packages in `../.nuget-packages`. Override
`AUTOFETE_DOTNET_HOST`, `AUTOFETE_SDK_DIR`,
`AUTOFETE_NUGET_PACKAGES`, or `DALAMUD_HOME` if those paths move.

```bash
./tools/build-wsl.sh
```

The build must finish with zero errors. The development DLL is
`bin/Release/AutoFete.dll`; the distributable package is
`bin/Release/AutoFete/latest.zip`. The SDK 15 packager creates the
API 15 manifest in that ZIP. Verify that the ZIP's DLL matches the
development DLL and that the manifest says API 15. Test loading and
item actions in game before distributing it.
