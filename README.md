<div align="center">
  <picture>
    <img width="230" height="230" alt="Relic -  smol png" src="https://github.com/user-attachments/assets/9d5ed02b-0f77-4f8c-bb6e-565d0346b3b6" />
  </picture>
  <h1>Relic engine</h1>
</div>

Relic engine is source available - but will not be excepting any contribution - Relic is a engine focused on "Boomer shooters" e.g.: Quake, Doom etc. Relic has a built in map compiler for brush based maps.

Please read the engine licence for any such edits to the engine.

Relic is **source available, not open source**. Contributions are not
accepted at this time.

# More stuff on Relic

### The compile loop

1. Edit your map in Rockwall2
2. **Save** — writes `Game/Working/Maps/<map>.rok`
3. **Compile** — writes `<map>.cmap` and `<map>.clm` beside the source, and
   copies them into the game's `Content/Maps` directory
4. **Run** — launches the game on the compiled map

The game loads maps from its own `Content/Maps` folder. Compiling from the
editor syncs the files across for you, so what you compile is what you run.
If you launch the game manually after a manual compile, copy the files
yourself or rebuild the Game project — `Game/Content/Content.mgcb` is what
normally copies `Working/` into `Content/`.


## Map formats

| Extension | Role |
| --- | --- |
| `.rok` | Rockwall editor source (JSON) |
| `.map` | TrenchBroom / Quake source |
| `.cmap` | Compiled world, binary — what the engine loads |
| `.clm` | Lightmap archive, read alongside the `.cmap` |
| `.edf` | Entity definition file, generated at build time |

The engine only ever loads `.cmap`. `.rok` and `.map` are inputs for the
compiler.

- **Engine** — governed by `Relic Engine License 1.0.txt`. Executables
  built with the engine must display the official Relic Engine splash
  screen; it may not be removed, hidden, replaced, modified, or obscured
  without written permission. The Relic name and associated artwork remain
  the property of the copyright holder.
- **Third-party components** — see `THIRD_PARTY_LICENSES.txt.txt`. Those
  components remain under their own licences.

Please read the engine licence before modifying or redistributing anything
built with it.

## Windows editor download

To create a self-contained Windows x64 package, run this from the repository
root in PowerShell with the .NET 8 SDK installed:

```powershell
.\tools\package-rockwall.ps1
```

The package is written to `dist/Rockwall-win-x64`. Distribute that whole folder
as a zip. On a Windows x64 PC, launch `Rockwall2.exe` from the extracted folder;
the package includes the game, editor assets, map compiler, and generated entity
data, and the editor opens without a per-user setup profile.
Pushing a version tag such as `v1.0.0` runs the Windows release workflow and
attaches this ZIP to the GitHub release.

Have a nice day! :D
