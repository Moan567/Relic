using Relic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace MapCompiler
{
    internal class Program
    {
        public const int VersionMajor = 8;
        public const int VersionMinor = 4;
        public const int VersionPatch = 0;

        public static string WorkingDir;
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            PrintBanner();

            // Strip a leading '@' or '>' from each arg (legacy editor invocation prefix).
            for (int i = 0; i < args.Length; i++)
                if (args[i].Length > 1 && (args[i][0] == '@' || args[i][0] == '>'))
                    args[i] = args[i][1..];

            if (args.Length < 1) { CompilerConsole.Error("No map file specified."); PrintUsage(); return -100; }
            if (args.Length < 2) { CompilerConsole.Error("No materials file specified."); PrintUsage(); return -100; }
            if (args.Length < 3) { CompilerConsole.Error("No texture path specified."); PrintUsage(); return -100; }

            string mapPath = args[0];
            string edfPath = args[1];
            WorkingDir = args[2];

            if (!File.Exists(mapPath)) { CompilerConsole.Error($"Map file not found: {mapPath}"); return -101; }
            if (!File.Exists(edfPath)) { CompilerConsole.Error($"Materials file not found: {edfPath}"); return -101; }
            if (!Directory.Exists(WorkingDir)) { CompilerConsole.Error($"Texture path not found: {WorkingDir}"); return -101; }

            // Environment-variable overrides let build scripts tune quality without recompiling
            int lightmapUnitSize = int.Parse(Environment.GetEnvironmentVariable("lightmapUnitSize") ?? "4");
            bool fastVis = bool.Parse(Environment.GetEnvironmentVariable("fastVis") ?? "false");

#if !DEBUG
            try
            {
#endif
                CompilerConsole.Header("Loading Assets");

                CompilerConsole.Step("Loading EDF...");
                LoadEDF(edfPath);

                Brush[] brushes;
                Rockwall.EntityReference[] entities;
                Rockwall.Terrain[] terrains;

                if (Path.GetExtension(mapPath).Equals(".map", StringComparison.OrdinalIgnoreCase))
                {
                    CompilerConsole.Step("Loading TrenchBroom map file...");
                    (brushes, entities, terrains) = Compilation.TBMapLoader.Load(mapPath, WorkingDir);
                }
                else
                {
                    CompilerConsole.Step("Loading map file...");
                    (brushes, entities, terrains) = LoadMap(File.ReadAllText(mapPath));
                }

                CompilerConsole.Step("Syncing material indices...");
                ResyncSurfaces(brushes);

                CompilerConsole.Step("Loading textures...");
                var (textures, matColors) = TextureLoader.Load(WorkingDir, brushes);

                CompilerConsole.Stat("Lightmap unit size", lightmapUnitSize);
                CompilerConsole.Stat("Fast vis", fastVis);

                if (File.Exists(Path.ChangeExtension(mapPath, "leak"))) File.Delete(Path.ChangeExtension(mapPath, "leak"));

                MapCompileOrchestrator.Compile(
                    brushes, entities, terrains, textures, matColors,
                    mapPath, lightmapUnitSize, fastVis);

                return 0;
#if !DEBUG
            }
            catch (Exception e)
            {
                CompilerConsole.Error($"Compilation failed: {e}");
                return -100;
            }
#endif
        }

        static void PrintUsage()
        {
            CompilerConsole.Info("Usage: Mapcompiler <mapfile> <edf file> <texture root>");
            CompilerConsole.Info("  mapfile: .map (TrenchBroom/Quake) or .rok/.json (Rockwall editor format)");
        }

        static void PrintBanner()
        {
            Console.WriteLine();

            (string text, ConsoleColor color)[] lines =
            {
                (" ██████╗ ███████╗██╗     ██╗ ██████╗", ConsoleColor.Magenta),
                (" ██╔══██╗██╔════╝██║     ██║██╔════╝", ConsoleColor.Magenta),
                (" ██████╔╝█████╗  ██║     ██║██║     ", ConsoleColor.White),
                (" ██╔══██╗██╔══╝  ██║     ██║██║     ", ConsoleColor.White),
                (" ██║  ██║███████╗███████╗██║╚██████╗", ConsoleColor.Yellow),
                (" ╚═╝  ╚═╝╚══════╝╚══════╝╚═╝ ╚═════╝", ConsoleColor.Yellow),
            };

            foreach (var (text, color) in lines)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(@"           M A P  C O M P I L E R   v"+ $"{VersionMajor}.{VersionMinor}.{VersionPatch}", ConsoleColor.DarkCyan);
            Console.ResetColor();
            Console.WriteLine();
        }

        static (Brush[] brushes, Rockwall.EntityReference[] entities, Rockwall.Terrain[] terrains) LoadMap(string json)
        {
            var map = Relic.Formatter.MapMigration.LoadAndMigrate(json);
            return (map.Brushes, map.EntityReferences, map.Terrains);
        }

        static void LoadEDF(string edsPath)
        {
            MaterialLoader.MountMaterials(EntityDataIndex.Read(edsPath).MaterialsPath);
        }


        // Legacy maps carry Surface indices baked against the material set that existed
        // when they were saved. Re-resolve against the freshly mounted materials so stale
        // (or out-of-range) indices can't crash the texture loader.
        static void ResyncSurfaces(Brush[] brushes)
        {
            var index = GlobalMapData.MaterialNameToIndex;
            if (index == null || index.Count == 0) return;

            var fallbackKv = index.FirstOrDefault(kv => kv.Key.StartsWith("Dev", StringComparison.OrdinalIgnoreCase));
            int fallback = fallbackKv.Key != null ? fallbackKv.Value : index.First().Value;

            foreach (var brush in brushes)
            {
                for (int f = 0; f < brush.Faces.Length; f++)
                {
                    var name = brush.Faces[f].MaterialName;
                    if (name != null && index.TryGetValue(name, out int surf))
                        brush.Faces[f].Surface = surf;
                    else if (brush.Faces[f].Surface < 0 || brush.Faces[f].Surface >= GlobalMapData.LoadedMaterials.Length)
                        brush.Faces[f].Surface = fallback;
                }
            }
        }
    }
}
