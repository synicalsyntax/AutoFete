# AutoFete

Dalamud plugin for automating [Fêtes in the Firmament](https://ffxiv.consolegameswiki.com/wiki/F%C3%AAtes).

## Requirements

Install and enable **vnavmesh** and **TextAdvance** before using AutoFete.
AutoFete uses vnavmesh for movement and TextAdvance for Fête hand-in dialogs.
Without TextAdvance, stages that require hand-ins cannot complete automatically.

## Build a release

Build on a machine with .NET 10 and a Dalamud API 15 development installation:

```sh
dotnet build AutoFete.csproj -c Release
```

DalamudPackager writes `bin/Release/AutoFete/latest.zip`. Distribute that ZIP,
which includes `AutoFete.dll` and its generated `AutoFete.json` manifest. The
source `AutoFete.json` is the manifest template; the packager supplies the
internal name, assembly version, and Dalamud API level in the ZIP.

## License

Copyright (c) 2026 synicalsyntax.

AutoFete is licensed under the [MIT License](LICENSE).
