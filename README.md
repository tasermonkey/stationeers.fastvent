# Fast Vent

A Stationeers mod that adds two vents:

- the **Fast Vent**: an Active Vent that moves gas several times faster, inward and outward, and in a room
  bigger than one grid also reaches the cells around it, like the large Powered Vent. Everything else (model,
  modes, pressure settings, power draw) is the Active Vent's.
- the **Pressure Relief Vent**: a Passive Vent that only lets gas out of its pipe above 5 MPa, and then only
  the gas above 5 MPa. Gas into the pipe flows as through a Passive Vent.

Needs BepInEx with [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad).
In multiplayer every player needs the mod.

## Fast Vent

- **Kit (Fast Vent)** comes from the Hydraulic Pipe Bender: 5 iron, 5 copper, 1 gold, like the Active Vent's.
- The vent's **Setting** is the multiplier: 1 to 20, 5 when built. Set it with a Logic Writer, or from a
  chip: `s d0 Setting 10`, or every Fast Vent on the network with `sb HASH("StructureFastVent") Setting 10`.
  Values outside 1–20 are clamped. `Maximum` reads 20.
- The vent's tooltip shows its multiplier.
- Each tick it first pumps its own grid cell, like the Active Vent. Whatever is left of that tick's limit
  goes to the cells open to it, up to as many cells away as the multigrid Powered Vent reaches (read from
  that prefab at load). It pulls from them when it's Inward and fills them when it's Outward, using the
  game's own `MultiGridAtmospherics`, and still stops at its pressure settings on both sides.
- In a sealed one-grid room, such as a 1x1x1 airlock, there are no open cells, so only the multiplier counts.
  With an airlock door open, the next room is open to it too.

## Pressure Relief Vent

- **Kit (Pressure Relief Vent)** comes from the Hydraulic Pipe Bender: 3 iron, like the Passive Vent's.
- Pipe at or below 5 MPa: gas only flows in, from the room, when the room is at a higher pressure.
- Pipe above 5 MPa: the gas above 5 MPa goes into the room, so the pipe is held at 5 MPa.
- Room above 5 MPa too: pipe and room even out, as through a Passive Vent.
- It adds as much volume to its pipe network as an in-line Small Tank (`StructureTankSmallInLine`) would.
- It has no logic; the 5 MPa is fixed.

## Building

`dotnet build -c Release` builds `FastVent.dll` and copies it, `About/` and `GameData/` to
`Documents\My Games\Stationeers\mods\FastVent\`. The game is expected in
`C:\Program Files (x86)\Steam\steamapps\common\Stationeers\`; pass `-p:StationeersDirectory=...` (with a
trailing `\`) if it's elsewhere, or `-p:ModDirectory=...` to copy the mod somewhere else.

## License

[AGPL-3.0-or-later](LICENSE), as IC10 Bench.
