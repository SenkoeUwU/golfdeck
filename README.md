# GolfDeck

Companion app for 3D-printed GSPro control boxes built from Xbox controller internals. Reads the box through XInput and sends the matching GSPro keyboard shortcuts. Single portable exe, no install, no dependencies beyond stock Windows 10/11.

**[Download the latest release](https://github.com/SenkoeUwU/golfdeck/releases/latest/download/GolfDeck.zip)**

![V1 board](docs/board-v1.png)

## Features

- On-screen replica of the physical board. Buttons light up when pressed, so it doubles as a wiring tester. Click a button with the mouse to test its key.
- V1 and V2 board layouts, chosen on first launch, switchable later.
- Edition presets matching the box colorways (Original, Green Jacket, Red & White, Red White & Blue), plus individual board, button and letter colors.
- All bindings in a plain-text `mapping.txt` (per-button key, label, hold/tap/repeat mode). Defaults are the standard GSPro shortcuts.
- Start with Windows (minimized to tray), tray status, restart-as-administrator for elevated GSPro.
- Two key injection modes (virtual keys and scancodes) for games that filter one or the other.

![V2 board, Green Jacket](docs/board-v2.png)
![V2 board, Red White & Blue](docs/edition-rwb.png)

## Install

1. Download `GolfDeck.zip` from the link above.
2. Right-click the zip, Properties, check **Unblock**, OK. Then extract anywhere (e.g. `C:\GolfDeck`).
3. Run `GolfDeck.exe` and pick your board layout (V1 = PUTT top-left, V2 = SCORECARD top-left).
4. Plug in the box. Press buttons and watch them light up.
5. Optional: tick "Start with Windows".

Windows SmartScreen may warn about an unknown publisher the first time. The app is unsigned; click "More info", then "Run anyway". See SETUP.txt inside the zip for the full walkthrough.

If GSPro runs as administrator, use the tray menu "Restart as administrator" once so keystrokes are not blocked.

## Mapping

`mapping.txt` sits next to the exe and is created on first run. Format:

```
input = keys | label | mode | repeat_ms
```

- inputs: `A B X Y LB RB LT RT Menu View LS RS`, `DPad_*`, `LS_*`, `RS_*` (stick directions)
- keys: `K`, `Ctrl+M`, `Shift+F5`, `Space` and so on
- modes: `hold` (held while pressed), `tap` (once per press), `repeat` (every `repeat_ms` while held)

The board GUI attaches mappings by label, so if a physical button lights the wrong spot, swap the labels in `mapping.txt`. Edit and reload from inside the app.

## Build from source

Run `build.bat`. It uses the C# compiler that ships inside Windows (.NET Framework 4.8), so no SDK install is needed. The whole app is one file, `Program.cs`.

## License

MIT. See [LICENSE](LICENSE).
