TrenchBroom (local copy)
========================

This folder is reserved for an upstream TrenchBroom checkout or a vendored copy.

Recommended workflow
- Add TrenchBroom as a git submodule under thirdparty/TrenchBroom (see ../../tools/add_trenchbroom.ps1)
- Build TrenchBroom using its build instructions (CMake + platform toolchain). See the upstream project for details: https://github.com/kduske/TrenchBroom
- Implement an exporter in the editor or a small standalone tool that writes .cmap (Rolonin compiled map) or .map for the mapcompiler to consume.

Exporter options
- In-editor plugin (C++): modify TrenchBroom source to add an export path for .cmap/.map.
- External exporter (C#): write a small CLI tool to convert TrenchBroom's .map or .tmx to .cmap and include it in pipeline.

Notes
- Verify TrenchBroom license before distributing modified source.
- This repository does not contain the TrenchBroom source; run tools/add_trenchbroom.ps1 locally to add it as a submodule.
