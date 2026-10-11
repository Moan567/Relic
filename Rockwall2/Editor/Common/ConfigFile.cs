using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Rockwall2.Editor.Common;

public struct EditorOverrides
{
    public struct EntityOverrideAsset
    {
        public string name;
        public Vector3 boundsMin, boundsMax;
        [JsonIgnore] public Texture2D image;
        public EntityProperty[] defaultProperties;
    }
    public EntityOverrideAsset[] overrides;
    public string workingdir;
}

public struct ConfigFile
{
    public string GameName, GameEDF, CompileTool, EditorAssetsPath, GamePath;
}
public static class ConfigManager
{
    public static ConfigFile currentConfig;
    public static string[] configFiles;

    public static string confSavePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/Rockwall/Configs";

    public static void LoadAllConfigs()
    {
        configFiles = Array.Empty<string>();
        if (!Directory.Exists(confSavePath))
        {
            Directory.CreateDirectory(confSavePath);
            return;
        }

        var validConfigs = new List<string>();
        foreach (var path in Directory.EnumerateFiles(confSavePath))
        {
            try
            {
                var config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(config.GameName))
                    validConfigs.Add(path);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Skipping invalid editor config '{path}': {ex.Message}");
            }
        }

        configFiles = validConfigs.ToArray();
    }
    public static void LoadConfig(string path)
    {
        currentConfig = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(path));
    }
    public static void SaveConfig(string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(currentConfig));
    }
    public static void DeleteConfig(string path)
    {
        File.Delete(path);
    }
}