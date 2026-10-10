using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Rockwall;
using System;
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
        if (!Directory.Exists(confSavePath))
        {
            Directory.CreateDirectory(confSavePath);
            return;
        }

        configFiles = Directory.GetFiles(confSavePath);
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