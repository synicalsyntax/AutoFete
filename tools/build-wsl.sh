#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
workspace_root=$(dirname "$repo_root")
cd "$repo_root"

dotnet_host=${AUTOFETE_DOTNET_HOST:-$(find "$HOME/.vscode-server/data/User/globalStorage/ms-dotnettools.vscode-dotnet-runtime/.dotnet" -maxdepth 2 -path '*/10.*~x64~aspnetcore/dotnet' -type f 2>/dev/null | sort -V | tail -n 1)}
sdk_dir=${AUTOFETE_SDK_DIR:-$(find '/mnt/c/Program Files/dotnet/sdk' -mindepth 1 -maxdepth 1 -type d -name '10.*' 2>/dev/null | sort -V | tail -n 1)}
export NUGET_PACKAGES=${AUTOFETE_NUGET_PACKAGES:-"$workspace_root/.nuget-packages"}
export DALAMUD_HOME=${DALAMUD_HOME:-"/mnt/c/Users/$USER/AppData/Roaming/XIVLauncher/addon/Hooks/dev"}
export DOTNET_CLI_HOME="$workspace_root/.dotnet-cli-home"
export MSBuildSDKsPath="$sdk_dir/Sdks"
export COMPlus_ReadyToRun=0
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

for required in "$dotnet_host" "$sdk_dir/dotnet.dll" "$DALAMUD_HOME/Dalamud.dll" "$NUGET_PACKAGES/dalamud.net.sdk/15.0.0/Sdk/Sdk.props"; do
    if [[ ! -f "$required" ]]; then
        printf 'Missing build prerequisite: %s\n' "$required" >&2
        exit 1
    fi
done

"$dotnet_host" "$sdk_dir/dotnet.dll" restore AutoFete.csproj \
    --locked-mode -p:EnableWindowsTargeting=true -p:NuGetAudit=false \
    --ignore-failed-sources --nologo
"$dotnet_host" "$sdk_dir/dotnet.dll" build AutoFete.csproj \
    -c Release --no-restore -p:EnableWindowsTargeting=true \
    --nologo --disable-build-servers

python3 - <<'PY'
from pathlib import Path
import json
import zipfile

root = Path('bin/Release')
dll = (root / 'AutoFete.dll').read_bytes()
with zipfile.ZipFile(root / 'AutoFete/latest.zip') as archive:
    assert archive.read('AutoFete.dll') == dll
    assert json.loads(archive.read('AutoFete.json'))['DalamudApiLevel'] == 15
print('Verified SDK 15 ZIP, DLL, and manifest.')
PY
