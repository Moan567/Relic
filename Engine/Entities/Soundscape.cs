using Engine;
using Engine.Compilation;
using Engine.Scripting.Sound;
using Engine.Sound;
using Microsoft.Xna.Framework;
using Rockwall;

namespace Engine.Entities
{
    [EntityDescriptor()]
    [EntityVisualize(typeof(SphereVisualizer))]
    [VisualizerProperty(nameof(SphereVisualizer.Radius), "Trigger Distance")]
    [ExposeEntityProperty("Soundscape", Rockwall.EntityPropertyType.String, "The name of the soundscape script to play.")]
    [ExposeEntityProperty("Trigger By Distance", Rockwall.EntityPropertyType.Bool, 
        "Whether this soundscape should be triggered by the listener coming within a certain distance. " +
        "Disabling this will allow you to trigger this soundscape to become active via inputs.")]
    [ExposeEntityProperty("Trigger Distance", Rockwall.EntityPropertyType.Float, "How big the trigger distance is.")]
    [ExposeEntityPropertyTarget("Sound Position 0")]
    [ExposeEntityPropertyTarget("Sound Position 1")]
    [ExposeEntityPropertyTarget("Sound Position 2")]
    [ExposeEntityPropertyTarget("Sound Position 3")]
    [ExposeEntityPropertyTarget("Sound Position 4")]
    [ExposeEntityPropertyTarget("Sound Position 5")]
    [ExposeEntityPropertyTarget("Sound Position 6")]
    [ExposeEntityPropertyTarget("Sound Position 7")]
    [RegisterEntityInputs("TriggerSoundscape")]
    public class Soundscape : WorldEntity
    {
        public Soundscape() 
        {
            IsSimulated = false;
            Controller = new SoundscapeController();
            RegisterInputLocally("TriggerSoundscape", (a, b) => { (Controller as SoundscapeController).Play(); });
        }
    }
    public class SoundscapeController : EntityController
    {
        bool triggerByDistance;
        float triggerDistance;
        string soundscapeName;

        public void Play()
        {
            for (int i = 0; i < 8; i++)
            {
                var ent = EntityManager.FindSingleEntityByName(entity.ReadProperty($"Sound Position {i}", Rockwall.EntityPropertyType.String) as string);

                if (ent != null)
                {
                    SoundscapeManager.SetAnchor(i, ent.Position);
                }
            }
            SoundscapeManager.StartSoundscape(soundscapeName);
        }

        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            triggerByDistance = (bool)(entity.ReadProperty("Trigger By Distance", Rockwall.EntityPropertyType.Bool) ?? false);
            triggerDistance = (float)(entity.ReadProperty("Trigger Distance", Rockwall.EntityPropertyType.Float) ?? 0f);
            soundscapeName = (string)(entity.ReadProperty("Soundscape", Rockwall.EntityPropertyType.String) ?? "");
        }

        public override void OnUpdate(GameTime gameTime)
        {
            if (!triggerByDistance && SoundscapeManager.CurrentSoundscape != soundscapeName) return;

            float distance = Vector3.Distance(SoundDevice.Device.ListenerPosition, entity.Position);

            if(distance < triggerDistance)
            {
                Play();
            }
        }
    }
}
