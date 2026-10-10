using Relic.Particles;
using Newtonsoft.Json;
using Rockwall2.Editor.Common;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Particles.Utils;
public static class ParticleEditorFileOpener
{
    public static void NewParticle()
    {
        ParticleManager.Clear();
        int id = ParticleManager.SpawnParticleSystem(Vector3.Zero, new ParticleSystemBehavior() { subsystemBehaviors = new ParticleSubsystemBehavior[1] { new() } });
        ParticleView.Instance.ActiveSpawner = ParticleManager.GetSpawner(id);
        MainWindow.Instance.particleEditor.Refresh();
    }
    public static void OpenParticle(string filePath)
    {
        ParticleManager.Clear();
        int id = ParticleManager.SpawnParticleSystem(Vector3.Zero, filePath);
        ParticleView.Instance.ActiveSpawner = ParticleManager.GetSpawner(id);
        MainWindow.Instance.particleEditor.Refresh();
    }

    public static void SaveParticle(string filePath)
    {
        if (ParticleView.Instance.ActiveSpawner == null) return;
        ParticleView.Instance.ActiveSpawner.system.behavior.subsystemBehaviors =
            ParticleView.Instance.ActiveSpawner.system.particleSubsystems.Select(s => s.behavior).ToArray();
        File.WriteAllText(filePath,
            JsonConvert.SerializeObject(ParticleView.Instance.ActiveSpawner.system.behavior, new JsonSerializerSettings { Converters = { new ColorJsonConverter() } }));
    }
}
