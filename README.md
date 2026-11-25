# QualityBuilder × Misc. Robots

Source: https://github.com/jb-rimworld/qualitybuilder-misc-robots (MIT, see `LICENSE`).

RimWorld compatibility mod (`juliusberger00.qualitybuilderrobots`) for 1.6 and 1.7. Lets Misc. Robots / Misc. Robots++
builder robots finish QualityBuilder frames whose minimum quality their quality roll can actually reach.

## How it works

| Piece | Where | What |
|---|---|---|
| Frame context | `Patches.cs` – prefix/finalizer on `WorkGiver_ConstructFinishFrames.JobOnThing` | remembers which frame QB is judging (QB's gate only gets the pawn) |
| Gate | `Patches.cs` – prefix on `QualityBuilder._WorkGiver_ConstructFinishFrames.isPawnGoodEnoughToBuild(Pawn)` | robots only: replace QB's "best builder" rule with "can reach the frame's `desiredMinQuality`"; colonists/mechs untouched |
| Prediction | `QualityPredictor.cs` | Static Quality (exact/±1/±2/cheat, thresholds, Legendary-needs-inspiration; robots never inspired) read live via reflection; vanilla: exact closed-form chance of `GenerateQualityCreatedByPawn(int,bool)` (checked against 200k Monte-Carlo rolls) vs. a configurable minimum chance |
| Robot skill fix | `Patches.cs` – prefix/finalizer on `QualityUtility.GenerateQualityCreatedByPawn(Pawn,SkillDef,bool)` | robots have mechanoid flesh, so vanilla rolls with `mechFixedSkillLevel` (10); temporarily swaps in the robot's own def skill so the original method and other mods' patches (Static Quality) run unchanged |
| Settings | `QBRMod.cs` | toggles + live table robot × target quality |

Robots stay out of QB's "best constructor skill" (QB's `pawnCanConstruct` is left false for them), so colonists'
QB behaviour is exactly as without this mod. All three target mods are soft-referenced (`AccessTools`), each patch
self-disables in `Prepare()` if its target is missing.

QB "unofficial 1.6" (`hatti.qualitybuilder`, workshop 3512466087) ships only a DLL — decompile with
`ilspycmd` to re-check method names after QB updates (`isPawnGoodEnoughToBuild`, `getSettings`, `getCompQualityBuilder`,
`pawnCanConstruct`, `hasDesignation`).

## Build

```sh
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build -c Release Source/QualityBuilderRobots/QualityBuilderRobots.csproj
```
Output: `1.6/Assemblies/QualityBuilderRobots.dll`.

## Licence

MIT, see `LICENSE`.
