# GolfDeck

Companion app for the 3D-printed golf control box (hacked Xbox controller). Reads the pad through XInput and emulates keyboard presses for GSPro. Single exe, no install, no dependencies beyond stock Windows.

## Use

1. Copy `GolfDeck.exe` to the sim PC (any folder).
2. Run it. First launch asks which board layout you have (V1 = PUTT top-left, V2 = SCORECARD top-left) and writes a `mapping.txt` with that layout's GSPro default keys.
3. The window mirrors the physical board. Buttons light up when pressed, so it doubles as a wiring tester. Clicking an on-screen button sends its key (note: the click focuses GolfDeck itself, so use it to verify sending works, not to drive GSPro).
4. Tick "Start with Windows" to auto-start minimized to tray. Close button hides to tray; exit from the tray icon.

The Options button holds the board layout switch, edition presets (Original, Green Jacket, Red & White, Red White & Blue), and individual board/button/letter colours.

If GSPro runs elevated, use the tray menu "Restart as administrator" so keystrokes are not blocked by UIPI.

## Key send modes

- **Virtual keys** - normal SendInput. Default.
- **Scancodes** - SendInput with scancodes only. Try this if GSPro ignores keys that Notepad receives.

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
