# AutoFete

Dalamud plugin for the automating completion of [fêtes in the Firmament](https://ffxiv.consolegameswiki.com/wiki/F%C3%AAtes).

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
