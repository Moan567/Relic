using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;

public static class MaterialLoader
{
    /// <summary>
    /// Mounts all .cmt material files from <paramref name="pathToRoot"/> and its subdirectories.
    /// </summary>
    /// <param name="pathToRoot">Path to the root of the materials folder.</param>
    public static void MountMaterials(string pathToRoot)
    {
        var materials = new List<Material>();

        foreach (var file in Directory.EnumerateFiles(pathToRoot, "*.cmt", SearchOption.AllDirectories))
        {
            var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
            if (!string.IsNullOrEmpty(mat.Name)) materials.Add(mat);
        }

        // Rebuild rather than append. This runs on every editor load and may be
        // called more than once; appending duplicated the array and made the
        // name->index map throw on duplicate keys.
        GlobalMapData.LoadedMaterials = materials.ToArray();

        var matNames = new Dictionary<string, int>();
        for (int i = 0; i < GlobalMapData.LoadedMaterials.Length; i++)
            matNames[GlobalMapData.LoadedMaterials[i].Name] = i;

        GlobalMapData.MaterialNameToIndex = matNames;
    }
}