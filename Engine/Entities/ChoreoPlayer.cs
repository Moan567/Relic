using Chisel;
using Engine.Compilation;
using Engine.Console;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities;

public interface ChoreoActor
{
    public bool IsActing { get; set; }
    public void OnLookAt(string targetEntity);
    public void OnPlayMorph(string targetEntity);
    public void OnPlayGesture(string targetEntity);
}

[EntityDescriptor]
[ExposeEntityProperty("Scene Path", Rockwall.EntityPropertyType.String, "Path to the choreo scene relative to the working/content folder. EG. 'Scene/myScene.choreo'.")]
[RegisterEntityInputs("PlayScene")]
[RegisterEntityOutputs("OnSceneComplete")]
public class ChoreoPlayer : WorldEntity
{
    public ChoreoPlayer()
    {
        IsSimulated = false;
        IgnoreCollision = true;
        AxisAlignedBox = true;

        Controller = new ChoreoPlayerController();

        RegisterInputLocally("PlayScene", async (args, from) =>
        {
            // If we're loading from a map in the working directory,
            // it's safe to assume we're expecting to use the scenes
            // from there as well. This is probably a hacky way to 
            // check, though.

            string normalizedTarget = Path.GetFullPath(MainEngine.Instance.ActiveMapPath).Replace('/', Path.DirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(MainEngine.FullPath).Replace('/', Path.DirectorySeparatorChar);

            var targPath = Path.Combine(MainEngine.FullPath, (string)ReadProperty("Scene Path", Rockwall.EntityPropertyType.String));

            if (!normalizedTarget.StartsWith(normalizedRoot))
            {
                // This should hopefully navigate out to the "Working" folder, and then eval from there.
                targPath = Path.Combine(Path.GetDirectoryName(MainEngine.Instance.ActiveMapPath), "..",
                                            (string)ReadProperty("Scene Path", Rockwall.EntityPropertyType.String));

                Logger.AppendInfo($"Detected map being run from working directory, retargeted scene search path to working directory.");
            }

            var scene = await ChoreoScene.LoadFromFile(targPath);

            if (scene == null) return;

            var playerController = (Controller as ChoreoPlayerController);
            playerController.activeScene = scene;

            playerController.choreoActors.Clear();

            foreach(var track in scene.Tracks)
            {
                var entities = EntityManager.FindEntityIndexByName(track.ActorName);

                if (entities == null || entities.Length <= 0) continue;

                foreach(var entity in entities)
                {
                    if (entity < 0) continue;
                    if (EntityManager.entities[entity]?.Controller is not ChoreoActor actor) continue;

                    playerController.choreoActors.Add(track.ActorName, (actor, EntityManager.entities[entity]));
                    actor.IsActing = true;
                    break;
                }
            }

            playerController.activeScene.OnEventTriggered = playerController.OnChoreoEvent;
            playerController.activeScene.SetEvents();
        });
    }

    internal class ChoreoPlayerController : EntityController
    {
        internal ChoreoScene activeScene = null;
        internal Dictionary<string, (ChoreoActor act, WorldEntity ent)> choreoActors = new Dictionary<string, (ChoreoActor act, WorldEntity ent)>();
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
        }

        public override void OnUpdate(GameTime gameTime)
        {
            if (activeScene == null) return;

            activeScene.Update(MainEngine.PreviousFrameDelta);

            if (activeScene.IsComplete)
            {
                activeScene = null;

                entity.CallOutput("OnSceneComplete", entity);

                foreach(var actor in choreoActors.Values)
                {
                    actor.act.IsActing = false;
                }
            }
        }

        internal void OnChoreoEvent(string actorName, ChoreoEvent evt)
        {
            if (!choreoActors.TryGetValue(actorName, out var actor)) return;

            switch(evt.EventType)
            {
                case ChoreoEventType.LookAt:

                    actor.act.OnLookAt(evt.Target);

                    break;
                case ChoreoEventType.PlayMorph:

                    actor.act.OnPlayMorph(evt.Target);

                    break;
                case ChoreoEventType.PlayGesture:

                    actor.act.OnPlayGesture(evt.Target);

                    break;
                case ChoreoEventType.EntityIO:

                    var targets = EntityManager.FindEntityIndexByName(evt.Target);

                    if (targets == null) break;

                    foreach(var targ in targets)
                    {
                        if (targ < 0) continue;

                        var ent = EntityManager.entities[targ];

                        ent.CallInput(evt.Data1, evt.Data2, actor.ent);
                    }

                    break;
            }
        }
    }
}
