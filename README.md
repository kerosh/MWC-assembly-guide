# Assembly Guide — see where a part goes in My Winter Car

An [MSCLoader](https://github.com/piotrulos/MSCModLoader) mod for **My Winter Car** that helps you build the Corris:

- a **ghost** of the part in your hands, drawn exactly where it goes;
- **green / yellow / red**: fits, fits but not original for your VIN, or can't be installed yet (and why);
- a **missing-parts list** in assembly order for whatever you are looking at;
- **x-ray bolt marks**: loose, tight and adjustment screws of the part under the crosshair.

> AI disclosure: most of the code was written with AI assistance (Claude). The mod says so in its MSCLoader description and its assembly attributes.

---

## Why

Building the car from a pile of parts means a lot of guessing: where does this go, why won't it attach, what have I forgotten,
which bolt is still loose, and will this part pass the historical inspection for my car's VIN?

The answers are all in the game's own logic. This mod reads that logic and shows it to you. **Nothing about specific parts is
hardcoded**: install rules, VIN rules and bolt limits are read from the game's PlayMaker FSMs when a save loads, so it works for
every save and keeps working when the game's part list changes.

## Features

### Ghost of the held part
While you hold a car part, a see-through copy of it is drawn at its place on the car, engine or other assembly:
- **green** – fits and can be installed now;
- **orange** – fits, but install another part first, because this one would block it
  (e.g. the distributor blocks the aux shaft): *"install first: Aux shaft (would be blocked)"*;
- **yellow** – fits, but it is not original for your car's VIN and would fail the historical (museum) inspection,
  e.g. *"not original for this VIN (Grille): Code is 'L', VIN needs 'ALL'"*; tuning parts are always yellow;
- **red** – can't be installed yet: *"remove first: Cylinder head"* or *"install first: Lower arm FR"*.

A short status line at the top of the screen tells the same in words.

### Missing parts list (Ctrl+Numpad7)
Shows what is still missing on the thing under your crosshair, in assembly order:
- looking at an **assembly** (engine block on the stand, cylinder head, the engine in the car…) – that assembly;
- looking at the **car body** – the places around the spot you look at (front suspension, dashboard…).

Green lines can be installed now and come first; orange ones also fit but would block a part that is still missing
("first: Aux shaft"); grey ones come later. Tuning parts are left out, and either/or parts (manual or automatic gear lever,
steel or fibreglass hood…) are shown once as "A / B".

### Bolt marks (Ctrl+Numpad8)
The part under the crosshair gets dots on its bolts, visible through everything:
- **red** – loose (or not fully tight), **green** – tight;
- **yellow** – adjustment: valve adjusters, carburettor idle/mixture screws, distributor timing, alternator belt tension,
  toe adjustment, adjustable shocks.

## Requirements
- My Winter Car (Steam).
- [MSCLoader](https://github.com/piotrulos/MSCModLoader) 1.3 or newer.

## Installation

### From a release
Copy `AssemblyGuide.dll` into `<game folder>\Mods\`.

### From source
Clone the repo into the game folder (`<game folder>\AssemblyGuide_src\`) and double-click `build.bat`. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Hotkeys
All remappable in *Mods → Keybinds*.

| Key | Action |
|---|---|
| Ctrl + Numpad7 | Show / hide the missing parts list |
| Ctrl + Numpad8 | Show / hide bolt marks |
| Ctrl + Numpad9 | Write diagnostics for the part in hands (see below) |

## Settings
- **Held part:** ghost on/off, ghost opacity, max ghost distance, status text on/off, VIN check on/off.
- **Bolt marks:** on/off, mark size.
- **Missing parts list:** max lines, look distance, car area radius, text size.

## Diagnostics
`Ctrl+Numpad9` (or the console command `aguide all`) writes a detailed report to `aguide_dump.txt` in the mod's settings folder:
the part in hands (or the last one held), its install rules and the full trace of the VIN check, your VIN table, the whole-car
missing list and the assembly order. Other console commands: `aguide held | socket NAME | list | vin | order | rescan`.
The in-game console only shows a one-line summary.

## Known limitations
- The historical inspection's paint colour and upholstery checks are not shown.
- Racing front exhaust pipe and side pipe block each other in the game's rules, so the list shows them as alternatives next to
  the stock front pipe; the correct grouping still has to be checked in game.
- Lifting the engine into the car with the hoist is only covered by the normal install rules.
- The carburettor throttle-linkage screw is not marked.

## Credits
- [MSCLoader](https://github.com/piotrulos/MSCModLoader) by piotrulos and contributors.
- My Winter Car by Amistech Games.

## License
[MIT](LICENSE)
