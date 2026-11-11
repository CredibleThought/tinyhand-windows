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
| Add your own hand | Right-click the notification-area hand › Add hand… (see below) |
| More settings | Right-click the notification-area hand › Settings |
| Quit | Right-click the notification-area hand › Quit TinyHand |

If you cannot see the hand in the notification area, click the **^** arrow next to the clock. You can drag the icon onto the taskbar to keep it in view.

Clicks on the taskbar and on TinyHand's own menus and settings window always go through, and the normal pointer shows there.

## Add your own hands

You can turn any picture of a hand holding a stick into a TinyHand pointer. The easiest way is to have ChatGPT or Gemini restyle the built-in hand:

1. Right-click the notification-area hand and choose **Add hand…**, or use **Add hand…** in Settings.
2. Click **Save template…** to save a picture of the Lavender hand on a white background.
3. Click **Copy prompt**, then open ChatGPT or Gemini, attach the template and paste the prompt. Replace the part in square brackets with the hand you want, for example "a golden robot hand" or "a furry cat paw".
4. Save the picture it makes, then click **Choose picture…** in TinyHand and pick it.
5. Check the red circle sits on the fingertip and the dashed line where the hand meets the stick. Click or drag in the picture to move the fingertip if needed.
6. Give the hand a name, change the ink colour if you like, and click **Add hand**.

The prompt is:

> Redraw the hand in this image as [describe your hand]. Keep exactly the same pose, size and position: the index finger points straight up near the top of the image, and the hand grips the top of a straight vertical stick that runs down to the bottom edge. Keep the stick plain and thin. Use a plain pure white background with no shadow, floor, text or border. Same 2:3 portrait image size.

Your own pictures work too, as long as one finger points up near the top, a straight stick runs down from the hand, and the background is white or transparent. TinyHand removes a white background, finds the fingertip and wrist, and warns you if something looks wrong.

Your hands appear in the toolbar's hand tray and the menus after the built-in ones. To delete one, choose it as the current hand and click **Remove** in Settings. They are saved in `%APPDATA%\TinyHand\hands`, or in a `hands` folder next to the exe in the portable version.

You can also save the template with `TinyHand.exe --save-template template.png`, and open the Add hand window with a picture already chosen with `TinyHand.exe --add-hand picture.png` (when TinyHand is not already running).

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
