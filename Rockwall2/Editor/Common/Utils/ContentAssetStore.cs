using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rockwall2.Editor.Common.Utils
{
    /// <summary>
    /// Reads and writes the game's *source* content assets.
    ///
    /// Layout on disk:
    ///   Working/Materials/**/*.cmt      - material definitions (source of truth, version controlled)
    ///   Working/Textures/**/*.png       - loose texture images (source of truth)
    ///   Content/Content.mgcb            - build script listing every Working file explicitly
    ///   bin/&lt;cfg&gt;/&lt;tfm&gt;/Content/... - MGCB output (gitignored, what the engine actually reads)
    ///
    /// Content.mgcb has no directory globs, so anything new must be registered there or
    /// neither the game nor the editor's viewport will ever see it.
    /// </summary>
    public static class ContentAssetStore
    {
        public const string TextureExtension = ".png";

        static readonly string[] TextureSearchExtensions = { ".png", ".jpg", ".jpeg", ".tga" };
        static readonly Dictionary<string, Texture2D> liveTextureCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Working\Textures -- where loose source images live.</summary>
        public static string WorkingTexturesRoot => Path.Combine(GlobalEditorData.WorkingDirectory, "Textures");

        /// <summary>Working\Materials -- where source .cmt definitions live.</summary>
        public static string WorkingMaterialsRoot => Path.Combine(GlobalEditorData.WorkingDirectory, "Materials");

        /// <summary>
        /// Content\Content.mgcb, derived from the Working root's sibling. Null when the
        /// working directory isn't inside a game checkout (e.g. an ad-hoc config).
        /// </summary>
        public static string ContentMgcbPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(GlobalEditorData.WorkingDirectory)) return null;

                string working = GlobalEditorData.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string gameRoot = Path.GetDirectoryName(working);
                if (string.IsNullOrEmpty(gameRoot)) return null;

                string candidate = Path.Combine(gameRoot, "Content", "Content.mgcb");
                return File.Exists(candidate) ? candidate : null;
            }
        }

        /// <summary>Turns a display name into something safe to use as a file name.</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "texture";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }

            return sb.Length == 0 ? "texture" : sb.ToString();
        }

        /// <summary>Material names are map-facing identifiers; keep them unique-ish and readable.</summary>
        public static string SanitizeMaterialName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "New Material";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }

            return sb.Length == 0 ? "New Material" : sb.ToString();
        }

        public static bool MaterialNameExists(string name)
        {
            return GlobalMapData.MaterialNameToIndex != null
                && GlobalMapData.MaterialNameToIndex.ContainsKey(name);
        }

        public static string UniqueMaterialName(string desired)
        {
            string name = SanitizeMaterialName(desired);
            if (!MaterialNameExists(name)) return name;

            for (int i = 2; ; i++)
            {
                string candidate = $"{name}_{i}";
                if (!MaterialNameExists(candidate)) return candidate;
            }
        }

        // ---- material (de)serialization -------------------------------------------------

        /// <summary>
        /// Writes a .cmt by hand rather than via JsonConvert.SerializeObject.
        ///
        /// Material keeps unknown keys in a [JsonExtensionData] field that OnDeserialized
        /// nulls out after moving them into the [JsonIgnore] TexturePaths dictionary, so a
        /// plain round-trip would silently drop every texture slot ("texture", "normal", ...).
        /// </summary>
        public static string SerializeMaterial(Material material)
        {
            var obj = new JObject();

            if (!string.IsNullOrEmpty(material.Name)) obj["name"] = material.Name;

            if (material.TexturePaths != null)
            {
                foreach (var kv in material.TexturePaths)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                    obj[kv.Key] = kv.Value;
                }
            }

            if (!string.IsNullOrEmpty(material.SurfaceType)) obj["surfaceType"] = material.SurfaceType;
            if (!string.IsNullOrEmpty(material.ShaderName)) obj["shaderName"] = material.ShaderName;

            obj["reflectivity"] = material.Reflectivity;

            if (material.Transparent) obj["transparent"] = true;
            if (material.AlphaClip) obj["alphaClip"] = true;
            if (material.NoCull) obj["noCull"] = true;
            if (material.TexelsPerUnit > 0) obj["texelsPerUnit"] = material.TexelsPerUnit;

            if (material.ShaderFlags != null && material.ShaderFlags.Count > 0)
            {
                obj["shaderFlags"] = JObject.FromObject(material.ShaderFlags);
            }

            return obj.ToString(Formatting.Indented);
        }

        public static void WriteMaterial(Material material, string cmtPath)
        {
            string dir = Path.GetDirectoryName(cmtPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(cmtPath, SerializeMaterial(material));
        }

/// <summary>Locates the .cmt that defines a material, preferring the versioned source.</summary>
    public static string FindMaterialFile(string materialName)
    {
        foreach (string root in new[] { WorkingMaterialsRoot, GlobalEditorData.MountedMaterialsPath })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            foreach (string file in Directory.EnumerateFiles(root, "*.cmt", SearchOption.AllDirectories))
            {
                var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
                if (mat.Name == materialName) return file;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a material name -> absolute ".cmt path" index in one pass.
    ///
    /// FindMaterialFile re-scans and re-parses the whole tree per call, which is fine for a
    /// one-off lookup but O(n^2) for filling a list of every material. Mounted copies are
    /// scanned first so the versioned source file wins.
    /// </summary>
    public static Dictionary<string, string> ScanMaterialFiles()
    {
        var index = new Dictionary<string, string>();

        foreach (string root in new[] { GlobalEditorData.MountedMaterialsPath, WorkingMaterialsRoot })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            foreach (string file in Directory.EnumerateFiles(root, "*.cmt", SearchOption.AllDirectories))
            {
                var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
                if (string.IsNullOrEmpty(mat.Name)) continue;

                index[mat.Name] = file;
            }
        }

        return index;
    }

    /// <summary>Path shown in the UI, e.g. "developer/devgrey.cmt", relative to whichever root holds it.</summary>
    public static string ToDisplayPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return "";

        foreach (string root in new[] { WorkingMaterialsRoot, GlobalEditorData.MountedMaterialsPath })
        {
            if (string.IsNullOrEmpty(root)) continue;
            if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

            return Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
        }

        return Path.GetFileName(absolutePath);
    }

        /// <summary>Source path of a material's colour texture, or null if it isn't on disk.</summary>
        public static string FindTextureFile(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath)) return null;

            string direct = Path.GetFullPath(Path.Combine(GlobalEditorData.WorkingDirectory, texturePath));
            foreach (string ext in TextureSearchExtensions)
            {
                string candidate = Path.ChangeExtension(direct, ext);
                if (File.Exists(candidate)) return candidate;
            }

            // Texture paths are stored without an extension and are conventionally lowercase,
            // but hand-written .cmt files don't always agree. Fall back to a case-insensitive scan.
            string dir = Path.GetDirectoryName(direct);
            string stem = Path.GetFileNameWithoutExtension(direct);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (!TextureSearchExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        // ---- import --------------------------------------------------------------------

        public class ImportResult
        {
            public bool Success;
            public string Message;
            public Material Material;
            public string MaterialFile;
            public string TextureFile;
        }

        /// <summary>
        /// Copies an image into Working\Textures, writes the matching .cmt, registers both in
        /// Content.mgcb and mirrors them into the MGCB output folder so the editor can use the
        /// new material immediately. The engine still needs a Game rebuild for the .xnb.
        /// </summary>
        public static ImportResult ImportTexture(string sourceImage, string materialName, string subFolder)
        {
            var result = new ImportResult();

            if (string.IsNullOrWhiteSpace(sourceImage) || !File.Exists(sourceImage))
            {
                result.Message = $"Source image not found: {sourceImage}";
                return result;
            }

            if (string.IsNullOrWhiteSpace(GlobalEditorData.WorkingDirectory))
            {
                result.Message = "No working directory configured - set it in the config editor first.";
                return result;
            }

            subFolder = NormalizeSubFolder(subFolder);

            string name = UniqueMaterialName(materialName);
            string fileStem = SanitizeFileName(name);

            string textureRelPath = $"Textures/{subFolder}/{fileStem}{TextureExtension}".Replace('\\', '/');
            string textureAbsPath = Path.Combine(GlobalEditorData.WorkingDirectory,
                textureRelPath.Replace('/', Path.DirectorySeparatorChar));

            string materialRelPath = $"Materials/{subFolder}/{fileStem}.cmt".Replace('\\', '/');
            string materialAbsPath = Path.Combine(GlobalEditorData.WorkingDirectory,
                materialRelPath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(textureAbsPath));
                Directory.CreateDirectory(Path.GetDirectoryName(materialAbsPath));

                // Always land on PNG so downstream MGCB settings stay predictable.
                if (!string.Equals(Path.GetExtension(sourceImage), TextureExtension, StringComparison.OrdinalIgnoreCase))
                {
                    ConvertToPng(sourceImage, textureAbsPath);
                }
                else
                {
                    File.Copy(sourceImage, textureAbsPath, overwrite: true);
                }

                var material = new Material
                {
                    Name = name,
                    SurfaceType = "concrete",
                    Reflectivity = 1.0f,
                    TexelsPerUnit = 512,
                    TexturePaths = new Dictionary<string, string>
                    {
                        ["texture"] = textureRelPath
                    }
                };

                WriteMaterial(material, materialAbsPath);

                RegisterInContentMgcb(textureRelPath, build: true);
                RegisterInContentMgcb(materialRelPath, build: false);

                MirrorIntoMgcOutput(textureAbsPath, textureRelPath);
                MirrorIntoMgcOutput(materialAbsPath, materialRelPath);

                AppendMaterial(material);

                result.Success = true;
                result.Material = material;
                result.MaterialFile = materialAbsPath;
                result.TextureFile = textureAbsPath;
                result.Message = $"Imported '{name}'.\n\nRebuild the Game project to build the .xnb so it renders in the viewport.";
            }
            catch (Exception e)
            {
                result.Message = $"Import failed: {e.Message}";
            }

            return result;
        }

        static string NormalizeSubFolder(string subFolder)
        {
            if (string.IsNullOrWhiteSpace(subFolder)) return "imported";

            var parts = subFolder
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => SanitizeFileName(p))
                .Where(p => p.Length > 0);

            string joined = string.Join("/", parts);
            return joined.Length == 0 ? "imported" : joined;
        }

        /// <summary>Re-encodes any supported image to PNG. Falls back to a byte copy if decoding fails.</summary>
        static void ConvertToPng(string source, string destination)
        {
            try
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(source);
                bitmap.Save(destination);
            }
            catch
            {
                File.Copy(source, destination, overwrite: true);
            }
        }

        /// <summary>Copies a Working file into the MGCB output folder so it exists without a rebuild.</summary>
        public static void MirrorIntoMgcOutput(string absoluteWorkingPath, string workingRelativePath)
        {
            if (string.IsNullOrEmpty(GlobalEditorData.MountedContentPath)) return;
            if (!File.Exists(absoluteWorkingPath)) return;

            try
            {
                string dest = Path.Combine(GlobalEditorData.MountedContentPath,
                    workingRelativePath.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(absoluteWorkingPath, dest, overwrite: true);
            }
            catch
            {
                // Read-only bin folder (game running) - the rebuild will pick it up anyway.
            }
        }

        /// <summary>
        /// Appends a Content.mgcb entry if one isn't already present. mgcb paths are relative
        /// to the .mgcb's own folder (Game\Content), hence the "../Working" prefix.
        /// </summary>
        public static bool RegisterInContentMgcb(string workingRelativePath, bool build)
        {
            string mgcb = ContentMgcbPath;
            if (mgcb == null) return false;

            string rel = workingRelativePath.Replace('\\', '/');
            string source = $"../Working/{rel}";
            string dest = rel;
            string directive = build ? $"/build:{source};{dest}" : $"/copy:{source};{dest}";

            string text = File.ReadAllText(mgcb);

            // Already listed? Nothing to do.
            if (text.Contains($"#begin {source}", StringComparison.OrdinalIgnoreCase))
                return false;

            // Seed the per-image options the existing entries use, otherwise MGCB rejects the build.
            string options = "";
            if (build)
            {
                options = "/platform:DesktopGL" + Environment.NewLine
                    + "/processor:TextureProcessor" + Environment.NewLine
                    + "/param:ColorKeyEnabled=False" + Environment.NewLine;
            }

            string entry = Environment.NewLine
                + "#begin " + source + Environment.NewLine
                + (options.Length > 0 ? options : "")
                + directive + Environment.NewLine;

            File.AppendAllText(mgcb, entry);
            return true;
        }

        // ---- live reload ----------------------------------------------------------------

        /// <summary>
        /// Adds (or replaces) a material in the live tables.
        ///
        /// Deliberately appends rather than re-running MountMaterials: Face.Surface and
        /// Terrain.Surface are cached indices into LoadedMaterials, and rebuilding the array
        /// would renumber every existing face behind the renderer's back. Appending keeps all
        /// existing indices valid, and newly painted faces pick up the new index.
        /// </summary>
        public static void AppendMaterial(Material material)
        {
            if (GlobalMapData.LoadedMaterials == null || string.IsNullOrEmpty(material.Name)) return;

            GlobalMapData.MaterialNameToIndex ??= new Dictionary<string, int>();

            if (GlobalMapData.MaterialNameToIndex.TryGetValue(material.Name, out int existing))
            {
                material = RefreshChangedTextureSlots(GlobalMapData.LoadedMaterials[existing], material);
                GlobalMapData.LoadedMaterials[existing] = material;
            }
            else
            {
                material = RefreshChangedTextureSlots(default, material);
                var list = GlobalMapData.LoadedMaterials.ToList();
                list.Add(material);
                GlobalMapData.LoadedMaterials = list.ToArray();
                GlobalMapData.MaterialNameToIndex[material.Name] = GlobalMapData.LoadedMaterials.Length - 1;
            }

            GlobalEditorData.LoadTex(true);
        }

        static Material RefreshChangedTextureSlots(Material previous, Material material)
        {
            var textures = previous.Textures == null
                ? new Dictionary<string, Texture2D>()
                : new Dictionary<string, Texture2D>(previous.Textures);

            if (material.TexturePaths == null)
            {
                textures.Clear();
            }
            else
            {
                foreach (string key in textures.Keys.ToList())
                    if (!material.TexturePaths.ContainsKey(key)) textures.Remove(key);

                foreach (var slot in material.TexturePaths)
                {
                    if (previous.GetPath(slot.Key) == slot.Value
                        && textures.TryGetValue(slot.Key, out var current)
                        && current != null)
                        continue;

                    var texture = LoadMaterialTexture(slot.Value);
                    if (texture == null) textures.Remove(slot.Key);
                    else textures[slot.Key] = texture;
                }
            }

            material.Textures = textures;
            return material;
        }

        public static Texture2D LoadMaterialTexture(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath)) return null;

            string sourcePath = FindTextureFile(texturePath);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                string fullPath = Path.GetFullPath(sourcePath);
                if (liveTextureCache.TryGetValue(fullPath, out var cached)) return cached;

                using var stream = File.OpenRead(fullPath);
                var texture = Texture2D.FromStream(EditorHost.Instance.GraphicsDevice, stream);
                liveTextureCache[fullPath] = texture;
                return texture;
            }

            try
            {
                return EditorHost.Instance.Content.Load<Texture2D>(texturePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reloads a saved source image into every live material slot that uses it.</summary>
        public static bool ReloadLiveTextureFile(string textureFilePath)
        {
            if (GlobalMapData.LoadedMaterials == null || !File.Exists(textureFilePath)) return false;

            string fullPath = Path.GetFullPath(textureFilePath);
            var references = new List<(int MaterialIndex, string Slot)>();

            for (int i = 0; i < GlobalMapData.LoadedMaterials.Length; i++)
            {
                var paths = GlobalMapData.LoadedMaterials[i].TexturePaths;
                if (paths == null) continue;

                foreach (var slot in paths)
                {
                    string sourcePath = FindTextureFile(slot.Value);
                    if (!string.IsNullOrEmpty(sourcePath)
                        && string.Equals(Path.GetFullPath(sourcePath), fullPath, StringComparison.OrdinalIgnoreCase))
                        references.Add((i, slot.Key));
                }
            }

            if (references.Count == 0) return true;

            Texture2D replacement;
            try
            {
                using var stream = File.OpenRead(fullPath);
                replacement = Texture2D.FromStream(EditorHost.Instance.GraphicsDevice, stream);
            }
            catch
            {
                return false;
            }

            liveTextureCache.TryGetValue(fullPath, out var previousTexture);
            liveTextureCache[fullPath] = replacement;

            foreach (var reference in references)
            {
                var material = GlobalMapData.LoadedMaterials[reference.MaterialIndex];
                material.SetTexture(reference.Slot, replacement);
                GlobalMapData.LoadedMaterials[reference.MaterialIndex] = material;
            }

            previousTexture?.Dispose();
            return true;
        }
    }
}