# Changelog

## 1.1.0 (2026-11-22)
- RimWorld 1.7 support: QualityBuilder and Misc. Robots exist for 1.7 now; the reflection targets were checked against their 1.7 DLLs (unchanged) and a 1.7 DLL was built.
- Builder bots on 1.7 come from Misc. Robots++ (the base Misc. Robots has only haulers and cleaners there).
- The release files are now arranged as `Common/` + version folders; the 1.6 build is the unchanged Workshop state.

## 1.0.0 (2026-10-02)
- First release: Misc. Robots / Misc. Robots++ builder robots may finish QualityBuilder frames when their quality roll can reach the minimum quality you set (Static Quality is read live when present).
- Robots roll with their own skill instead of the fixed 10 of the vanilla mechanoid rule. Colonists are unchanged.
