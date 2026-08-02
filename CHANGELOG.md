# Changelog

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
