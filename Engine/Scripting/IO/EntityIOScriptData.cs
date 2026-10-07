using Chisel.EXScript;
using Chisel.Utils;
using Engine.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.IO;
public class EntityType : IEXObjectType
{
    public string TypeName => "entity";
    private WorldEntity Get(object instance)
    {
        if (instance is not WorldEntity e) throw new EXScriptRuntimeError("entity object has incorrect reference", -1);
        return e;
    }

    public bool TryGetIndexer(object instance, EXValue index, out EXValue value)
    {
        value = EXValue.Null();
        return false;
    }
    public bool TrySetIndexer(object instance, EXValue index, EXValue value)
    {
        return false;
    }

    public bool TryGetMember(object instance, string name, out EXValue value)
    {
        value = EXValue.Null();

        var ent = Get(instance) ?? throw new EXScriptRuntimeError($"entity backing data was null. Was the entity despawned properly?", -1);

        switch (name)
        {
            case "position": value = EXValueConverter.From(ent.Position); return true;
            case "velocity": value = EXValueConverter.From(ent.Velocity); return true;
            case "angles": value = EXValueConverter.From(CMath.ToEulerAngles(ent.Rotation)); return true;
            case "targetname": value = EXValueConverter.From(ent.Name); return true;

            case "classname": 
                value = EXValueConverter.From(ent.GetType().Name);
                return true;

            case "insidebrush": 
                if (ent is not BrushEntity be)
                    throw new EXScriptRuntimeError($"'.insidebrush' is only valid on brush-based volumes (got '{ent.GetType().Name}').", -1);
                value = EXValue.Of(be.insideBrush
                    .Select(e => EXValue.Of(new EXObject { Type = this, Instance = e }))
                    .ToArray());
                return true;

            default:
                return false;
        }
    }

    public bool TrySetMember(object instance, string name, EXValue value)
    {
        var ent = Get(instance) ?? throw new EXScriptRuntimeError($"entity backing data was null. Was the entity despawned properly?", -1);
        switch (name)
        {
            case "position":

                ent.Position = EXValueConverter.AsVector3(value);

                return true;
            case "velocity":

                ent.Velocity = EXValueConverter.AsVector3(value);

                return true;
            case "angles":

                ent.Rotation = CMath.ToQuaternion(EXValueConverter.AsVector3(value));

                return true;

            case "targetname":

                ent.Name = EXValueConverter.AsString(value);

                return true;

            default:
                return false;
        }
    }
    public bool TryGetPersistToken(object instance, out string token)
    {
        token = Get(instance).SaveID.ToString();
        return true;
    }
    public bool TryResolveFromToken(string token, out object instance)
    {
        instance = null;
        if (!Guid.TryParse(token, out var guid)) return false;

        var idx = EntityManager.entities.FindIndex(e => e != null && e.SaveID == guid);
        if (idx < 0) return false;

        instance = EntityManager.entities[idx];
        return true;
    }
}
class ChainObjectType : IEXObjectType
{
    public string TypeName => "chain";

    public bool TryGetMember(object instance, string name, out EXValue value) { value = EXValue.Null(); return false; }
    public bool TrySetMember(object instance, string name, EXValue value) => false;

    public bool TryGetIndexer(object instance, EXValue index, out EXValue value)
    {
        var box = (Box<string[]>)instance;
        var key = EXValueConverter.AsString(index);
        var found = box.Value?.FirstOrDefault(p => p != null && p.Split(':')[0] == key);
        value = found == null ? EXValue.Null() : EXRuntime.ParseFromString(WorldEntityHost.StripName(found));
        return true;
    }

    public bool TrySetIndexer(object instance, EXValue index, EXValue value)
    {
        var box = (Box<string[]>)instance;
        var key = EXValueConverter.AsString(index);
        box.Value = WorldEntity.MergePassVariables(box.Value, new[] { $"{key}:{EXRuntime.Stringify(value)}" });
        return true;
    }

    public bool TryGetPersistToken(object instance, out string token) { token = null; return false; }
    public bool TryResolveFromToken(string token, out object instance) { instance = null; return false; }
}

class GlobalStateObjectType : IEXObjectType
{
    public string TypeName => "globals";
    public bool TryGetMember(object i, string n, out EXValue v) { v = EXValue.Null(); return false; }
    public bool TrySetMember(object i, string n, EXValue v) => false;

    public bool TryGetIndexer(object instance, EXValue index, out EXValue value)
    {
        var key = EXValueConverter.AsString(index);
        value = GlobalState.HasState(key) ? GlobalState.ReadValue(key) : EXValue.Null();
        return true;
    }

    public bool TrySetIndexer(object instance, EXValue index, EXValue value) =>
        GlobalState.TrySetValue(EXValueConverter.AsString(index), value);

    public bool TryGetPersistToken(object i, out string t) { t = null; return false; }
    public bool TryResolveFromToken(string t, out object i) { i = null; return false; }
}

class MapGlobalsObjectType : IEXObjectType
{
    public string TypeName => "mapglobals";
    public bool TryGetMember(object i, string n, out EXValue v) { v = EXValue.Null(); return false; }
    public bool TrySetMember(object i, string n, EXValue v) => false;

    public bool TryGetIndexer(object instance, EXValue index, out EXValue value)
    {
        var key = EXValueConverter.AsString(index);
        value = MapGlobals.HasValue(key) ? MapGlobals.ReadValue(key) : EXValue.Null();
        return true;
    }

    public bool TrySetIndexer(object instance, EXValue index, EXValue value)
    {
        MapGlobals.SetValue(EXValueConverter.AsString(index), value);
        return true;
    }

    public bool TryGetPersistToken(object i, out string t) { t = null; return false; }
    public bool TryResolveFromToken(string t, out object i) { i = null; return false; }
}

struct TraceResultData
{
    public bool Hit;
    public Vector3 Point;
    public Vector3 Normal;
    public WorldEntity Entity;
}
class TraceResultType : IEXObjectType
{
    public string TypeName => "traceresult";
    readonly EntityType entityType;
    public TraceResultType(EntityType entityType) => this.entityType = entityType;

    public bool TryGetMember(object instance, string name, out EXValue value)
    {
        var hit = (TraceResultData)instance;
        switch (name)
        {
            case "hit": value = EXValue.Of(hit.Hit); return true;
            case "point": value = EXValueConverter.From(hit.Point); return true;
            case "normal": value = EXValueConverter.From(hit.Normal); return true;
            case "entity":
                value = hit.Entity != null
                    ? EXValue.Of(new EXObject { Type = entityType, Instance = hit.Entity })
                    : EXValue.Null();
                return true;
            default: value = EXValue.Null(); return false;
        }
    }
    public bool TrySetMember(object instance, string name, EXValue value) => false;
    public bool TryGetIndexer(object instance, EXValue index, out EXValue value) { value = EXValue.Null(); return false; }
    public bool TrySetIndexer(object instance, EXValue index, EXValue value) => false;

    public bool TryGetPersistToken(object instance, out string token) { token = null; return false; }
    public bool TryResolveFromToken(string token, out object instance) { instance = null; return false; }
}