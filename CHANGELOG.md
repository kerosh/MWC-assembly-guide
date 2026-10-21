# Changelog

## 1.2.0 (unreleased)
### Added
- Optional bolt list (setting): loose / tight / adjust bolts of the looked-at part as count × tool, e.g. "4 × 10 mm,
  1 × screwdriver". Tool names come from the game's tool logic (screwdriver, spark plug socket, lug wrench; spanners
  and sockets 5-15 mm).
- Loose bolts elsewhere on the same assembly are shown as smaller red dots (and counted).

### Changed
- Missing list: parts the game refuses because an installed part is the wrong kind (e.g. clutch parts with a flexplate),
  and the parts that only belong with them, are no longer counted as missing; they are shown as one dimmed
  "not needed with ..." line at the bottom.
- Bolt marks: tight (green) bolts are not drawn on parts that can't be removed right now (something else blocks taking
  them off), so a built engine is no longer covered in green dots.

### Fixed
- VIN check: checks that mix the held part with other parts used the other parts' current state instead of assuming
  them right, e.g. dashboard / dash covers shown yellow "(General): all-true(...) -> False" while the rest of the
  dashboard was not installed yet (also: rims compared with the other wheels, hubcaps "all four installed").
- Install rules with "all / any of these installed" checks that send the part back (refuse) were read as requirements,
  e.g. front shock absorber green before the brake although the shock blocks the brake.
- Bolt marks showed the bolts of a whole assembly (e.g. block + head + everything on it) when looking at an installed part,
  and nothing on installed body parts (e.g. steering column): parts are now recognised by their Data FSM, not by the
  PART tag, which the game changes on install. If the crosshair hits no part with bolts, the part whose bolt is nearest
  to the line of sight is used.
- Clutch cover plate vs flywheel (and radiator fan vs water pump pulley) shown as a conflict: install rules also read
  non-"installed" flags such as the flywheel's `Flexplate`; those are now compatibility checks ("not compatible with
  Flexplate") instead of order rules. "Bolted" checks stay order rules ("remove first: Gearbox crossmember (unbolt it)").
- VIN check blamed the held part for a check of a different, missing part (e.g. exhaust manifold shown yellow with
  "(Brakes): Type 1 is not allowed" while the brake master cylinder was not installed).

## 1.1.0 (unreleased)
### Added
- Parts that would block a still-missing part are orange instead of green, with the part to install first
  (e.g. distributor while the aux shaft is missing): orange ghost + "install first: Aux shaft (would be blocked)",
  and in the list they are sorted below the parts that are safe to install now.

### Fixed
- Parts that fit one of several identical places (main bearings, pistons, rockers, spark plugs, wheels, hubcaps, suspension arms,
  shocks, springs...) showed "no install point". They now use the game's group list (`Data.ArrayReference`) and the nearest free place.

## 1.0.0 (unreleased)
First version.

### Held part
- Ghost of the part in your hands at its place, coloured green (fits), yellow (not original for the car's VIN) or red (can't be installed yet).
- Status text with the reason: what to remove or install first, or which VIN rule the part breaks.
- Install rules read from each place's own FSM; VIN rules by running the game's historical-inspection FSM in a sandbox with the held part in place.

### Missing parts list
- Missing parts of the assembly or car area under the crosshair, in assembly order; green = can install now.
- Tuning parts left out, either/or parts shown once.

### Bolt marks
- X-ray marks on the bolts of the part under the crosshair: loose, tight and adjustment screws.

### Tools
- Diagnostics report (`Ctrl+Numpad9` / `aguide` console command) written to a file.
