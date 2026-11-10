#!/usr/bin/env bash
# Build the release files into dist/: TinyHand-{arm64,x64}.exe for the installer
# and TinyHand-portable-{arm64,x64}.zip.
# Needs the .NET 10 SDK; works on Windows, macOS or Linux.
set -euo pipefail
cd "$(dirname "$0")"
dotnet=${DOTNET:-dotnet}
command -v "$dotnet" >/dev/null || dotnet="$HOME/.dotnet/dotnet"
rm -rf dist
for arch in arm64 x64; do
    "$dotnet" publish src/TinyHand -c Release -r "win-$arch" -o "dist/win-$arch" --nologo -v quiet
    mv "dist/win-$arch/TinyHand.exe" "dist/TinyHand-$arch.exe"
    rm -rf "dist/win-$arch"

    # The portable zip: the exe plus the marker file that keeps settings next to it.
    portable="dist/TinyHand-portable-$arch"
    mkdir -p "$portable"
    cp "dist/TinyHand-$arch.exe" "$portable/TinyHand.exe"
    cp portable/TinyHand.portable "$portable/"
    (cd "$portable" && zip -q -X "../TinyHand-portable-$arch.zip" TinyHand.exe TinyHand.portable)
    rm -rf "$portable"
done
ls -lh dist
