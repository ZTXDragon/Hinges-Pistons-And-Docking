# Hinges, Pistons and Docking - source

The C# source of `ZTX.HingesPistonsDocking.dll`, the DLL of the Cosmoteer mod Hinges, Pistons and Docking (`ztx.hinges_pistons_docking`). It
needs the YAML mod loader (Steam Workshop 3577650065) at run time.

## Layout

| Folder | What it holds |
|---|---|
| `Core/` | logging and version, shared helpers, the sim-dispose reset |
| `Ui/` | the part-window base and selection helpers |
| `Structure/` | frames, roofs, adjacency |
| `Joints/` | hinges, motors, pistons, rods, corridor joints |
| `Surgery/` | ship split and merge, repairs |
| `Assembly/` | jointed sections as one ship: facade, crew, supply, flight, FTL |
| `Docking/` | docking clamps and pads, the bay beam |
| `Bays/` | the Bay Terminal and Bay Station |
| `Rails/` | rails and carts |
| `Logic/Engine`, `Logic/Parts`, `Logic/Console` | the rule engine and storage, the logic parts, the Logic Console window |

`mod.rules.template` becomes the mod's `mod.rules` at install.

## Building

Needs the .NET SDK and a Cosmoteer install. From this folder:

```
powershell -NoProfile -ExecutionPolicy Bypass -File build_and_install.ps1
```

It builds Release against the game's `Bin` folder and the YAML loader, checks that `Core/Log.cs` and
`mod.rules.template` carry the same version, and copies the DLL and `mod.rules` into the installed mod folder.

## Notes

- AI was used during code generation of this mod.

## License

MIT, see `LICENSE`.
