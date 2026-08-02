# Changelog

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
