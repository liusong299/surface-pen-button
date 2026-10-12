# PenButton

Small, dependency-free tools that give the **Surface Pen top button** more useful actions on Windows 11.

Windows lets each of the three pen-button gestures (single click, double-click, press and hold) open one program. PenButton is that program. What it does is set in a simple settings window (or an `.ini` file), and it can do something different depending on the app you are in.

[中文说明](README.zh-CN.md)

## What it can do

| Action | What happens |
|---|---|
| Press keys | Any key or combo: `Enter`, `Ctrl+Enter`, `RAlt` (right Alt, handy for voice-typing apps), arrow keys, F-keys, ... |
| Snip, then paste | 1st press copies a screenshot of the current window to the clipboard, 2nd press pastes it (Ctrl+V), 3rd press snips again, and so on |
| Snip only / Paste | One of the two, every time |
| Run a program | Starts any program or script |
| Nothing | Turns a gesture off |

After a screenshot or paste, a small label ("Captured" / "Pasted") shows next to the pen for under a second. It never takes focus.

**Per-app rules.** Each gesture can do something else in a given program or window, for example:

- PowerPoint slide show: click = next slide, double-click = previous slide (set up by default)
- Outlook: press and hold = `Ctrl+Enter` (send) instead of `Enter`

Rules match by program name (`OUTLOOK.EXE`), window class (`screenClass`) or text in the window title (`Gmail`). In the settings window you can pick the window from a list, or switch to it and let the tool read it after a 3-second countdown.

Default setup:

| Gesture | Default action |
|---|---|
| Single click | `RAlt` (right Alt) |
| Double-click | Snip, then paste |
| Press and hold | `Enter` |

## Install

1. Download `PenButton.zip` from [Releases](../../releases) and unzip it to a folder that will stay put, e.g. `C:\Tools\PenButton`. Or clone this repo there and double-click `build.bat` (no Visual Studio needed; it uses the C# compiler built into Windows).
2. Open **Settings > Bluetooth & devices > Pen & Windows Ink > Pen shortcuts**. For each gesture choose **Open a program** and pick:
   - Single click: `PenClick.exe`
   - Double-click: `PenDoubleClick.exe`
   - Press and hold: `PenHold.exe`
3. Run `PenButtonSettings.exe` to change what each gesture does. Click **Save**; the next press uses the new settings. No re-binding, no rebuild.

The exes are not code-signed, so Windows SmartScreen may warn the first time. Choose "More info > Run anyway", or build from source with `build.bat`.

If PowerPoint or another app grabs the pen button itself, turn off **Allow apps to override the shortcut button behavior** on the same Pen shortcuts page.

## Settings file

`PenButtonSettings.exe` reads and writes `PenButton.ini` next to the exes. You can also edit it in Notepad. It is created with these defaults on first run:

```ini
[Settings]
; wait before acting, so the pen-button event can finish (ms)
PreDelayMs=100
; how long keys are held down (ms)
KeyHoldMs=40
; window or screen
SnipMode=window
SnipSound=false
; above 0: if the last screenshot is older than this (s), the next press snips again
SnipResetSeconds=0
Toast=true
ToastMs=800
; empty = follow the Windows language
ToastSnipText=
ToastPasteText=
; true: write each press to PenButton.log
Log=false

; PowerPoint slide show and Presenter View
[class:screenClass|PodiumParent]
Click=keys:Right
DoubleClick=keys:Left

[Default]
Click=keys:RAlt
DoubleClick=snippaste
Hold=keys:Enter
```

Comments go on their own lines: `;` starts a comment only at the beginning of a line.

Rules are checked top to bottom. The first section that matches the current window **and** sets that gesture wins; anything not set falls back to `[Default]`. Use `|` to list several names: `[app:OUTLOOK.EXE|olk.exe]`.

Action syntax: `keys:<combo>`, `snippaste`, `snip`, `paste`, `run:<program> [args]`, `none`.
Key names: `Ctrl Shift Alt Win RAlt RCtrl Enter Tab Esc Space Backspace Delete Insert Home End PageUp PageDown Left Right Up Down F1`..`F24` `A`..`Z` `0`..`9` `Apps PrintScreen CapsLock`.

Not sure what a window is called? Turn on `Log=true`, press the pen button in that window, and read `PenButton.log`.

## How it works

- `PenClick.exe`, `PenDoubleClick.exe` and `PenHold.exe` are the same program; it reads its own file name to know which gesture fired (Windows cannot pass arguments to pen-shortcut programs).
- Keys are sent with `keybd_event`, with the extended-key flag where needed (right Alt, arrows, ...).
- Screenshots use the window's visible bounds (`DWMWA_EXTENDED_FRAME_BOUNDS`) with per-monitor DPI awareness, so they are sharp at 150 to 200% scaling. The image stays on the clipboard after the program exits.
- The snip/paste step is remembered in `%LOCALAPPDATA%\PenButton\PenSnip.state`. Delete it to make the next press a screenshot.
- Everything is built with the .NET Framework 4.x `csc.exe` that ships with Windows (C# 5, WinForms). No installer, no background process, no network access.

## Known limits

- Windows blocks simulated keys to apps running as administrator (UIPI), so key actions do nothing there.
- Snip/paste only knows what it did last. If you paste by hand with Ctrl+V after a snip, the next pen press still pastes once.
- Screenshots capture what is visible on screen, including anything covering the window.
- For Store (UWP) apps the program name is often `ApplicationFrameHost.exe`; match them by window title instead.

## Build from source

```
build.bat
```

Sources are in `src/`: `Core.cs` (ini model, rules, key names, Win32), `PenButton.cs` (the button program), `PenButtonSettings.cs` (the settings window). Keep to C# 5 syntax (no `$""`, no `?.`) so the built-in compiler can build it. `tests/CoreTests.cs` covers the ini parser and rule matching; CI builds and runs it on Windows.

## License

MIT
