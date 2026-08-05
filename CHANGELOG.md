# Changelog

## v2.0.2

- Removed the small chips that appeared along the top of the board listing mappings with no matching button. They looked like buttons or settings, and the status line already reports a mapping that does not fit the board.

## v2.0.1

- Fixed: on an 8-way hat switch, the four diagonal positions matched no direction at all, so diagonal aiming did nothing. Directions now overlap at the diagonals the way a d-pad does.
- Fixed: which axes a generic joystick has is now read from the device's capability flags instead of being inferred from the axis count, which was wrong for devices that expose (for example) a rudder axis but no Z axis.
- Fixed: a long device name in the status line could run underneath the OPTIONS button. The status line now clips with an ellipsis, and the connected-device text is shown in a compact form.
- Fixed: the double-press and battery timers used a zero timestamp as a sentinel, which would misbehave on the ~49-day tick counter rollover.
- Changed: when a device is connected whose mapping does not match while GolfDeck is in the tray, it now shows a tray notification instead of a dialog, so nothing steals focus from GSPro mid-round.
- Device scanning while nothing is plugged in no longer queries every joystick slot twice per pass.
- Removed dead code and corrected a wrong capability constant.

## v2.0

- Support for control boxes that are not Xbox-compatible. Units that enumerate as "Generic USB Joystick" (DirectInput/HID) were previously invisible, because GolfDeck only read XInput. It now reads both, with no added dependencies.
- Input device picker in Options: Auto (prefers an Xbox pad), or a specific device by name.
- Input monitor in Options: live view of the button numbers, axes and hat position the box reports, so a unit with unknown wiring can be mapped without any external tool.
- Mapping format gained generic input names: `Btn1`-`Btn32` (`Button01` accepted too), `Axis1n`/`Axis1p` through `Axis6n`/`Axis6p`, and `POV_Up`/`Down`/`Left`/`Right`.
- Default mappings for generic joysticks on both board layouts, taken from the maker's JoyToKey profiles (wired V1 and V2), plus a "Load defaults" button that writes the template matching the current board and device.
- Swapping between a wired box and a wireless one is detected: GolfDeck notices the mapping no longer matches the connected device and offers to load the right defaults.
- The first-launch layout prompt names the controller it detected.
- The status line warns when the loaded mapping is written for a different device family than the one connected.
- Status line now names the active device instead of the XInput player slot.

## v1.9

- Battery readout for wireless boxes: level (empty/low/medium/full) shown in the board status line and tray tooltip, refreshed every 5 seconds. Low battery turns the readout amber (red when empty) and pops a one-time tray warning. Wired connections show nothing (XInput reports no level for wired pads).

## v1.8

- Main window is now just the board: the bottom bar is gone. Status (controller, admin, last key) sits in the board's top-left, with ink colours that stay readable on every board colour, and a stylized OPTIONS chip sits top-right.
- Everything else moved into the Options window: Start with Windows, key send mode, keys-sent statistic (live), Edit/Reload mapping.
- Options controls restyled (minimal look): borderless dropdowns with accent underline opening dark menus, glyph toggles, all following the selected edition colours.

## v1.7

- Options moved from a dropdown menu to a proper settings window (layout, colours, presets, updates).
- New option, on by default: "Close button hides to the tray instead of exiting" - turn it off to make the X button quit.
- mapping.txt now lives in %AppData%\GolfDeck instead of next to the exe; existing files are migrated automatically. "Open mapping folder" button added.
- The exe now has an embedded icon (shows in Explorer, shortcuts and the taskbar).

## v1.6

- Dual-function buttons: new `doubletap` mapping mode - single press sends one key, two quick presses (500ms window) send another, matching the physical V2 box behaviour (confirmed with the maker). A `taphold` mode (hold past a threshold instead) is also available.
- V2 default mapping corrected to the maker's actual wiring (from the JoyToKey profile): all six green-print secondary functions (club up/down, scramble 1/2, tee left/right) now work, HIDE OBJECT sends B, and the WAKE button doubles as aim right.
- Board captions show both keys on dual-function buttons ("T / I").
- Click-to-test supports dual functions: single click for the primary, quick double click for the secondary.

## v1.5

- Full display-scaling overhaul: the app now derives one scale factor from display DPI (capped so the window always fits the screen) and drives all geometry and text through it. Fixes crushed and overlapping UI at 125/150/200% display scaling and on small screens.
- Status bar text clips with an ellipsis instead of running under the buttons.

## v1.4

- Built-in update checker against GitHub releases: prompts on launch with the new version and summarized release notes, downloads and swaps the exe in place on accept. Declining snoozes the prompt for 7 days; "Check for updates" in Options checks immediately.
- Releases now ship a bare GolfDeck.exe (no zip); the updater downloads it directly.
- Arrow buttons and the center cross redrawn as clean vector shapes.

## v1.3

- Board lettering switched to upright Bahnschrift SemiCondensed (was italic condensed), fixing crushed text at small sizes.
- Window now scales with display DPI, fixing crushed text on 125/150% displays.
- Center arrow cross redrawn as vector shapes, larger and crisp at any resolution.

## v1.2

- V2 board layout (SCORECARD / FAST FWD / HEATMAP / HIDE OBJECT) with first-launch layout picker.
- Edition presets: Original, Green Jacket, Red & White, Red White & Blue; individual board, button and letter color options.
- Green secondary print (C-up, C-down, S1, S2, tee marks, WAKE) placed as printed on the product.
- Click-to-test: mouse press on an on-screen button sends its key.
- Status bar shows last key sent and mapping errors; tray balloon and live tray tooltip; window position remembered.

## v1.1

- Options menu with box color choices.
- Arrow cluster laid out as an equal-arm plus.
- Default binds corrected to GSPro standard shortcuts, board slots anchored by label.

## v1.0

- First release: XInput to keyboard bridge, board-replica GUI, plain-text mapping, autostart, tray, virtual-key and scancode send modes.
