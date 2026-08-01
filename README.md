# GolfDeck

Companion app for the 3D-printed golf control box (hacked Xbox controller). Reads the pad through XInput and emulates keyboard presses for GSPro. Single exe, no install, no dependencies beyond stock Windows.

## Use

1. Copy `GolfDeck.exe` to the sim PC (any folder).
2. Run it. `mapping.txt` is created next to the exe on first run.
3. The window mirrors the physical board. Buttons light up when pressed, so it doubles as a wiring tester.
4. Tick "Start with Windows" to auto-start minimized to tray. Close button hides to tray; exit from the tray icon.

If GSPro runs elevated, use the tray menu "Restart as administrator" so keystrokes are not blocked by UIPI.

## Key send modes

Three modes, selectable at the bottom of the window:

- **Virtual keys** - normal SendInput. Default.
- **Scancodes** - SendInput with scancodes only. Try this first if GSPro ignores keys that Notepad receives.
- **Driver (Interception)** - keystrokes go through the Interception kernel driver and look like real keyboard hardware. Cannot be filtered by the game. GolfDeck talks to the driver directly; AutoHotkey/AutoHotInterception is not needed.

Driver mode needs a one-time setup on the sim PC:

1. Download `Interception.zip` from https://github.com/oblitum/Interception (Releases page).
2. In an admin command prompt run: `install-interception.exe /install`
3. Reboot.
4. Copy `library\x64\interception.dll` from the zip next to `GolfDeck.exe`.
5. Select "Driver (Interception)" in GolfDeck. The status line shows `driver: ok (keyboard N)` when working.

The Interception driver is free for personal use. Note: with the driver installed, a real keyboard must be plugged in (keystrokes are sent through the first detected keyboard device).

## Mapping

Edit `mapping.txt` (button in the GUI opens it), then "Reload mapping". Format:

```
input = keys | label | mode | repeat_ms
```

- inputs: `A B X Y LB RB LT RT Menu View LS RS`, `DPad_*`, `LS_*`, `RS_*` (stick directions)
- keys: `K`, `Ctrl+M`, `Shift+F5`, `'` etc.
- modes: `hold` (held while pressed), `tap` (once per press), `repeat` (every `repeat_ms` while held)

Defaults replicate the old JoyToKey profile. Labels are guesses at which physical button is wired where; fix them in `mapping.txt` if PUTT is not the A button etc.

## Build

Run `build.bat`. Uses the C# compiler that ships inside Windows (.NET Framework 4.8), so no SDK install is needed.
