# Changelog

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
