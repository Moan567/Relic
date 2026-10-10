using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Relic.Models.Morph;
using Relic.Utils.Animation;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Model.Utils;
using Rockwall2.Editor.Particles.Utils;
using Rockwall2.Views;
using SharpHook.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Relic.Models.CModel;

namespace Rockwall2.Editor.Common.Utils;
public static class FileHandler
{
    public static async void OpenMap()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Maps"));

        var customType = new FilePickerFileType("Rockwall Files")
        {
            Patterns = new[] { "*.rok" },
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            FileTypeFilter = new[] { customType },
            AllowMultiple = false,
            SuggestedStartLocation = startPart,
        });

        if (files.Count >= 1)
        {
            await using var stream = await files[0].OpenReadAsync();
            using var streamReader = new StreamReader(stream);

            var fileContent = await streamReader.ReadToEndAsync();
            MapTools.LoadMap(fileContent);
            MapTools.ActivePath = files[0].Path.LocalPath;
        }
        KeyboardManager.ClearKeys();
    }
    public static async void SaveMapAs()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Maps"));

        var customType = new FilePickerFileType("Rockwall Files")
        {
            Patterns = new[] { "*.rok" },
        };

        var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            DefaultExtension = ".rok",
            FileTypeChoices = new[] { customType },
            SuggestedStartLocation = startPart
        });

        if (files != null)
        {
            SaveMap(files.Path.LocalPath);
            MapTools.ActivePath = files.Path.LocalPath;
        }
        KeyboardManager.ClearKeys();
    }
    public static void SaveMap(string path)
    {
        MapTools.SaveMap(path);
    }
    public static void SaveCurrentMap()
    {
        if(!string.IsNullOrEmpty(MapTools.ActivePath))
        {
            SaveMap(MapTools.ActivePath);
            return;
        }

        SaveMapAs();
    }


    public static async void OpenParticle()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Particles"));

        var customType = new FilePickerFileType("Relic Particles")
        {
            Patterns = new[] { "*.crp" },
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            FileTypeFilter = new[] { customType },
            AllowMultiple = false,
            SuggestedStartLocation = startPart,
        });

        if (files.Count >= 1)
        {
            ParticleEditorFileOpener.OpenParticle(files[0].Path.LocalPath);
        }
        KeyboardManager.ClearKeys();
    }
    public static async void SaveParticleAs()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Particles"));

        var customType = new FilePickerFileType("Relic Particles")
        {
            Patterns = new[] { "*.crp" },
        };

        var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            DefaultExtension = ".rok",
            FileTypeChoices = new[] { customType },
            SuggestedStartLocation = startPart
        });

        if (files != null)
        {
            ParticleEditorFileOpener.SaveParticle(files.Path.LocalPath);
        }
        KeyboardManager.ClearKeys();
    }
    public static void SaveParticle(string path)
    {
        ParticleEditorFileOpener.SaveParticle(path);
    }
    public static void SaveCurrentParticle()
    {
        if (!string.IsNullOrEmpty(MapTools.ActivePath))
        {
            SaveParticle(MapTools.ActivePath);
            return;
        }

        SaveParticleAs();
    }


    public static async void OpenModel()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Models"));

        var customType = new FilePickerFileType("CCMDL Files")
        {
            Patterns = new[] { "*.ccmdl" },
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            FileTypeFilter = new[] { customType },
            AllowMultiple = false,
            SuggestedStartLocation = startPart
        });

        if (files.Count >= 1)
        {
            MainWindow.Instance.modelEditor.Clear();

            byte[] data = File.ReadAllBytes(files[0].Path.LocalPath);
            ModelEditorData.ActivePath = files[0].Path.LocalPath;
            ModelEditorData.ActiveModel = CCMDLWriter.LoadFromCCMDL(data, EditorHost.Instance.GraphicsDevice);

            ModelEditorData.SequencePlayer = null;
            if (ModelEditorData.ActiveModel.Animations != null && ModelEditorData.ActiveModel.Animations.Count > 0 && ModelEditorData.ActiveModel.Animations.Any(a => a.Name.ToLower().Equals("bindpose")))
            {
                CAnimDef bindpose = ModelEditorData.ActiveModel.Animations.Find(a => a.Name.ToLower().Equals("bindpose"));
                CAnimationPlayer bindposePlayer = new CAnimationPlayer(ModelEditorData.ActiveModel);
                bindposePlayer.AnimDef = bindpose;
                bindposePlayer.IsPlaying = false;
                bindposePlayer.Update(0f);
                var basePose = bindposePlayer.BoneSpaceTransforms.ToArray();
                ModelEditorData.ActiveModel.BoneTransforms = basePose;

                ModelEditorData.SequencePlayer = new CAnimationPlayer(ModelEditorData.ActiveModel);
                ModelEditorData.SequencePlayer.AnimDef = ModelEditorData.ActiveModel.Animations[0];
            }
            foreach (var bg in ModelEditorData.ActiveModel.Bodygroups)
            {
                if (bg.MorphTargets?.Count > 0)
                    bg.MorphApplicator = new CMorphApplicator(
                        EditorHost.Instance.GraphicsDevice, bg.MeshData.Vertices);
            }
            MainWindow.Instance.modelEditor.RefreshAll();
            ModelEditorData.MorphState.Initialize(ModelEditorData.ActiveModel);
        }
    }
    public static async void SaveModelAs()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(Path.Combine(GlobalEditorData.WorkingDirectory, "Models"));

        var customType = new FilePickerFileType("CCMDL Files")
        {
            Patterns = new[] { "*.ccmdl" },
        };

        var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            DefaultExtension = ".ccmdl",
            FileTypeChoices = new[] { customType },
            SuggestedStartLocation = startPart
        });

        if (files != null)
        {
            ModelEditorData.ActivePath = files.Path.LocalPath;
            SaveModel(files.Path.LocalPath);
        }
    }
    public static void SaveModel(string path)
    {
        byte[] data = CCMDLWriter.GetCCMDLWriteableData(ModelEditorData.ActiveModel);
        File.WriteAllBytes(path, data);
    }
}
