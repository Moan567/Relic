using Engine.Compilation;
using Engine.Console;
using Engine.Entities.BrushEntities;
using Engine.SaveSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.LogicEntities;

[EntityDescriptor()]
[RegisterEntityInputs("ChangeLevel")]
public class LogicChangeLevel : WorldEntity
{
    public LogicChangeLevel()
    {
        AxisAlignedBox = true;
        IgnoreCollision = true;
        IsSimulated = false;

        RegisterInputLocally("ChangeLevel", (s, e) =>
        {
            // map paths are relative to the original map path.
            string absolutePath = Path.Combine(Path.GetDirectoryName(MainEngine.Instance.ActiveMapPath), s);
            MainEngine.Instance.LoadMap(absolutePath); 
        });
    }
}
[EntityDescriptor()]
[ExposeEntityPropertyTarget("Transition Volume", "Target name for the FuncTrigger to carry entities into a new level.")]
[ExposeEntityProperty("Destination Anchor", Rockwall.EntityPropertyType.String, "Name of the LogicTransitionLevel entity in the destination map to align to.")]
[ExposeEntityProperty("Clear Map Cache", Rockwall.EntityPropertyType.Bool, "Clears the existing map cache once the next level is loaded, useful if there is a point where the player cannot backtrack.")]
[RegisterEntityInputs("TransitionLevel")]
public class LogicTransitionLevel : WorldEntity
{
    public LogicTransitionLevel()
    {
        AxisAlignedBox = true;
        IgnoreCollision = true;
        IsSimulated = false;

        RegisterInputLocally("TransitionLevel", (mapPath, e) =>
        {
            var volumeName = ReadProperty("Transition Volume", Rockwall.EntityPropertyType.String) as string;
            var volumeIndices = EntityManager.FindEntityIndexByName(volumeName);

            if (volumeIndices == null || volumeIndices.Length == 0)
            {
                Logger.AppendError("Cannot transition level without a valid volume!");
                return;
            }

            var volume = EntityManager.entities[volumeIndices[0]];
            if (volume is not FuncTrigger trigger)
            {
                Logger.AppendError("Cannot transition level without a valid volume!");
                return;
            }

            string destAnchorName = ReadProperty("Destination Anchor", Rockwall.EntityPropertyType.String) as string ?? Name;

            bool shouldClear = ((bool)ReadProperty("Clear Map Cache",Rockwall.EntityPropertyType.Bool));

            if (shouldClear)
                SaveManager.ClearSessionMapStates();

            SaveManager.QueueLevelTransition(
                mapPath,
                trigger.insideBrush.Where(e => e is not LogicTransitionLevel && e is not BrushEntity).ToList(),
                Position,           // this entity's world position is the origin anchor
                destAnchorName
            );
        });
    }
}
public abstract class LogicFilter : WorldEntity
{
    public LogicFilter()
    {
        IsSimulated = false;
        AxisAlignedBox = true;
        IgnoreCollision = true;
    }
    public abstract bool Test(WorldEntity entity);
}
[EntityDescriptor()]
[ExposeEntityProperty("Classname", Rockwall.EntityPropertyType.String, "The allowed classname through this filter")]
public class LogicClassFilter : LogicFilter
{
    public override bool Test(WorldEntity entity)
    {
        var classname = entity.GetType().Name;
        return string.Equals(classname, ReadProperty("Classname",Rockwall.EntityPropertyType.String) as string);
    }
}