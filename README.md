# TinyHand for Windows

A hand on a stick for pointing at your screen during demos, screen shares and recordings. While it is on, your mouse pointer becomes a big cartoon hand. Clicks become taps, and dragging draws shapes that fade away, so you can point things out without clicking anything by accident.

This is a Windows port of [TinyHand for macOS](https://github.com/nilbuild/tinyhand) by Kamran Ahmed. There is also a [GNOME version for Linux](https://github.com/CredibleThought/tinyhand-linux).

## Install

Open PowerShell (not as administrator) and run:

```powershell
irm https://raw.githubusercontent.com/CredibleThought/tinyhand-windows/main/install.ps1 | iex
```

To read the script before running it, open [install.ps1](install.ps1).

The script:

- downloads the right build for your PC (x64 or ARM64) from the latest release
- installs it in `%LOCALAPPDATA%\Programs\TinyHand` (no administrator rights needed)
- adds TinyHand and TinyHand Settings to the Start menu
- starts TinyHand now and whenever you sign in
- adds TinyHand to Settings › Apps, so you can uninstall it there

Run the same command again to update.

### Install by hand

1. Download `TinyHand-x64.exe` (most PCs) or `TinyHand-arm64.exe` (Snapdragon and other ARM PCs) from the [latest release](https://github.com/CredibleThought/tinyhand-windows/releases/latest).
2. Put it wherever you like and run it. Windows SmartScreen may warn you because the app is not signed; choose **More info › Run anyway**.
3. To start it when you sign in, right-click the hand in the notification area and tick **Start with Windows**.

TinyHand is one self-contained exe. It needs no .NET install and Windows 10 or 11 (64-bit).

### Portable version

To run TinyHand without installing it, for example from a USB stick:

1. Download `TinyHand-portable-x64.zip` (most PCs) or `TinyHand-portable-arm64.zip` (ARM PCs) from the [latest release](https://github.com/CredibleThought/tinyhand-windows/releases/latest).
2. Unzip it into any folder and run `TinyHand.exe`.

The zip holds `TinyHand.exe` and a small `TinyHand.portable` file. While that file is next to the exe, TinyHand keeps its settings in `settings.json` in the same folder and writes nothing to `%APPDATA%` or the registry. It also does not offer to start with Windows. To remove it, quit TinyHand from its notification-area menu and delete the folder.

Only one copy of TinyHand runs at a time, so quit an installed copy before starting a portable one.

### Uninstall

Use **Settings › Apps › Installed apps › TinyHand › Uninstall**, or run:

```powershell
irm https://raw.githubusercontent.com/CredibleThought/tinyhand-windows/main/uninstall.ps1 | iex
```

This removes the app, its shortcuts and its saved settings.

## Use

| To | Do this |
| --- | --- |
| Turn the pointer on or off | Press **Ctrl+Alt+H**, or left-click the hand in the notification area |
| Point | Move the mouse |
| Tap | Click. The app under the pointer is not clicked |
| Draw | Drag. Pick pen, ellipse, rectangle or arrow on the toolbar |
| Draw circles, squares or straight arrows | Hold **Shift** while you drag |
| Highlight a spot | Double-click for a glow or a sparkle burst |
| Click an app for real | Hold **Alt** while you click |
| Scroll | Use the wheel or touchpad as usual; scrolling always reaches the app |
| Clear the drawing | **Ctrl+Alt+X** |
| Turn the pointer off | **Esc**, or the × on the toolbar |
| Change hands, ink, shapes or sound | Use the toolbar that appears when you move to the bottom of the screen |
| More settings | Right-click the notification-area hand › Settings |
| Quit | Right-click the notification-area hand › Quit TinyHand |

If you cannot see the hand in the notification area, click the **^** arrow next to the clock. You can drag the icon onto the taskbar to keep it in view.

Clicks on the taskbar and on TinyHand's own menus and settings window always go through, and the normal pointer shows there.

## Notes

- **The pointer is hidden by swapping the system cursors for blank ones.** TinyHand puts your cursors back when the pointer turns off or the app quits. If TinyHand is ever killed while the pointer is on (for example from Task Manager), the pointer stays hidden until you start TinyHand again, sign out, or run the uninstall command.
- Clicks are caught with a low-level mouse hook. It only blocks mouse buttons while the pointer is on; it never sees the keyboard and records nothing.
- Windows run as administrator can still be pointed at, but holding Alt may not click them, because Windows keeps normal apps from sending input to administrator apps.
- Settings are saved in `%APPDATA%\TinyHand\settings.json`, or next to the exe in the portable version.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Then:

```bash
git clone https://github.com/CredibleThought/tinyhand-windows.git
cd tinyhand-windows
./build.sh
```

`build.sh` writes `dist/TinyHand-x64.exe` and `dist/TinyHand-arm64.exe` for the installer, and the portable zips `dist/TinyHand-portable-x64.zip` and `dist/TinyHand-portable-arm64.zip`. It works on Windows (Git Bash), macOS and Linux. To run a debug build on Windows, use `dotnet run --project src/TinyHand`.

Set the environment variable `TINYHAND_PROFILE=1` before starting TinyHand to log frame timings to `%TEMP%\tinyhand-profile.txt`.

The hand images, sounds and their measurements in `assets/` are the same as in the Linux version, which prepares them from the macOS app's artwork.

## Credits

Windows port by Steve Fernandes. Based on [TinyHand for macOS](https://github.com/nilbuild/tinyhand) by Kamran Ahmed. MIT licence; see [LICENSE](LICENSE).
