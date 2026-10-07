using Engine.SaveSystem;
using Engine.UI;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Engine.UI
{
    public class SaveEntry
    {
        public string SavePath { get; set; }
        public string SaveType { get; set; }
        public string DateDisplay { get; set; } 
        public Texture2D Screenshot { get; set; }
    }

    public class SaveViewMenu
    {
        public CGWindow Window { get; private set; }
        public bool Opened { get; private set; }

        private bool saveMode;
        private CGUISelectableList<SaveEntry> saveList;

        public void Initialize(bool saveMode)
        {
            this.saveMode = saveMode;
            var saves = GetSaves();

            Window = CGUI.Window($"{(saveMode ? ("Save") : ("Load"))} Game", 700, 500)
                .FillList(saves, (row, save) => row
                    .Thumbnail(save.Screenshot, width: 96)
                    .Text(save.DateDisplay, save.SaveType),
                    out saveList,
                    itemHeight: 72)
                .BottomBar(
                    ($"{(saveMode ? ("Save") : ("Load"))}", (w) => { if (saveList.Selected != null) OnOpenEntry(saveList.Selected.SavePath); }),
                    ("Delete", (w) => { if (saveList.Selected != null) OnDelete(saveList.Selected.SavePath); }),
                    ("Cancel", (w) => Close()))
                .Build();

            saveList.DoubleClicked += (entry) => { OnOpenEntry(entry.SavePath); };

            Opened = true;
        }

        IEnumerable<SaveEntry> GetSaves()
        {
            var saveFiles = SaveManager.GetAllSaves();

            List<SaveEntry> saves = new List<SaveEntry>();

            HashSet<string> sessions = new HashSet<string>();

            foreach(var file in saveFiles.OrderBy(s=>File.GetLastWriteTimeUtc(s)).Reverse())
            {
                if (file.EndsWith("png")) continue;
                if (file.EndsWith("cfg")) continue;

                if (saveMode && sessions.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                sessions.Add(Path.GetFileNameWithoutExtension(file));

                var time = File.GetCreationTime(file);

                var screenShotPath = $"{file}.png";

                var loadType = "Manual Save";

                if (file.Contains("quick")) loadType = "Quick Save";
                if (file.Contains("auto")) loadType = "Auto Save";

                saves.Add(new SaveEntry
                {
                    SavePath = file,
                    SaveType = loadType,
                    DateDisplay = time.ToString(),
                    Screenshot = Texture2D.FromFile(MainEngine.Instance.GraphicsDevice, screenShotPath),
                });
            }

            if(saveMode)
            {
                saves.Insert(0,new SaveEntry { DateDisplay = "New Save", SaveType = null });
            }

            return saves;
        }

        private void OnOpenEntry(string path)
        {
            if(saveMode)
            {
                if (string.IsNullOrEmpty(path)) SaveManager.SaveGame(SaveManager.SaveType.Manual);
                else SaveManager.SaveGame(SaveManager.SaveType.Manual, Path.GetFileNameWithoutExtension(path));

                Close();
            }
            else
            {
                SaveManager.LoadGame(Path.GetFileNameWithoutExtension(path));
            }
            Close();
            MainEngine.SetMouseToDefaultLockState();
        }
        private void OnDelete(string path)
        {
            void DeleteSave()
            {
                File.Delete(path);
                File.Delete($"{path}.png");
                Close();
                Initialize(saveMode);
                Opened = true;
            }

            CGUI.Window(" ", 300, 200)
                .Label("Are you sure you want to delete this save?")
                .BottomBar(
                    ("Yes", (w) => { w.Close(); DeleteSave(); }),
                    ("No", null)
                ).Build();
        }

        public void Open()
        {
            Opened = true;
            Window.Open();
        }

        public void Close()
        {
            Opened = false;
            Window.Close();
        }
    }
}