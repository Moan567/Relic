using Chisel.EXScript;
using Chisel.Utils;
using Engine.Compilation;
using Engine.Conditions;
using Engine.Console;
using Engine.Entities;
using Engine.Physics;
using Engine.Scripting.Sound;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.IO;
public class WorldEntityHost : IEXHost
{
    readonly WorldEntity self;
    readonly WorldEntity activator;
    readonly Box<string[]> chain;
    readonly Dictionary<string, EXValue> resolvedNames = new();

    readonly EntityType EntityTypeInstance = new();
    readonly ChainObjectType ChainTypeInstance = new();
    readonly GlobalStateObjectType GlobalStateTypeInstance = new();
    readonly MapGlobalsObjectType MapGlobalsTypeInstance = new();
    readonly TraceResultType TraceResultTypeInstance;

    public WorldEntityHost(WorldEntity self, WorldEntity activator, Box<string[]> chain)
    {
        this.self = self;
        this.activator = activator;
        this.chain = chain;
        TraceResultTypeInstance = new TraceResultType(EntityTypeInstance);
    }

    public bool ChainStopped { get; private set; }
    public EXValue GetMember(EXValue receiver, string name, EXScope scope) =>
        throw new EXScriptRuntimeError($"{receiver.Kind} has no member '{name}'.", -1);

    public void AssignMember(EXValue receiver, string name, EXValue value) =>
        throw new EXScriptRuntimeError($"{receiver.Kind} has no settable member '{name}'.", -1);
    public void ReportError(EXScriptRuntimeError error)
    {
        Logger.AppendError(error.Message);
    }
    public EXValue ResolveName(string name, EXScope scope)
    {
        switch (name)
        {
            case "_self":
                return EXValue.Of(new EXObject { Type = EntityTypeInstance, Instance = self });

            case "_activator":
                return activator != null
                    ? EXValue.Of(new EXObject { Type = EntityTypeInstance, Instance = activator })
                    : EXValue.Null();

            case "chain":
                return EXValue.Of(new EXObject { Type = ChainTypeInstance, Instance = chain });

            case "globals":
                return EXValue.Of(new EXObject { Type = GlobalStateTypeInstance, Instance = null });

            case "mapglobals":
                return EXValue.Of(new EXObject { Type = MapGlobalsTypeInstance, Instance = null });
        }

        if (resolvedNames.TryGetValue(name, out var cached)) return cached;

        var indices = EntityManager.FindEntityIndexByName(name);
        var result = (indices == null || indices.Length == 0)
            ? EXValue.Null()
            : EXValue.Of(new EXObject { Type = EntityTypeInstance, Instance = EntityManager.entities[indices[0]] });

        resolvedNames[name] = result;
        return result;
    }

    public EXValue Invoke(EXValue receiver, string name, EXValue[] args, EXScope scope)
    {
        if (receiver == null)
        {
            switch (name)
            {
                case "stopchain":
                    ChainStopped = true;
                    return EXValue.Null();

                case "spawnentity":
                    {
                        RequireArgs(args, 1, name);
                        var classname = EXValueConverter.AsString(args[0]);
                        var ent = EntityCompiler.GetFromClassname(classname);
                        EntityManager.SpawnEntity(ent);
                        return EXValue.Of(new EXObject { Type = EntityTypeInstance, Instance = ent }); // fixed
                    }

                case "phystrace":
                case "bsptrace":
                    return DoTrace(name, args);
                case "print":
                    {
                        var str = string.Join(" ", args.Select(EXRuntime.Stringify));
                        MainEngine.Instance.Console.WriteDirect(str);
                        return EXValue.Null();
                    }

                case "playsound":
                    {
                        RequireArgs(args, 1, 2, name);
                        var soundName = EXValueConverter.AsString(args[0]);
                        var pos = args.Length > 1 ? EXValueConverter.AsVector3(args[1]) : (Vector3?)null;
                        SoundScriptManager.PlaySound(soundName, pos);
                        return EXValue.Null();
                    }

                case "playparticle":
                    {
                        RequireArgs(args, 2, name);
                        var particlePath = EXValueConverter.AsString(args[0]);
                        var pos = EXValueConverter.AsVector3(args[1]);
                        ParticleManager.SpawnParticleSystem(pos, particlePath);
                        return EXValue.Null();
                    }

                case "entitiesinradius":
                    {
                        RequireArgs(args, 2, name);
                        var center = EXValueConverter.AsVector3(args[0]);
                        var radius = EXValueConverter.AsFloat(args[1]);
                        var found = Collision.GetEntitiesInSphere(center, radius);
                        return EXValue.Of(found
                            .Select(e => EXValue.Of(new EXObject { Type = EntityTypeInstance, Instance = e }))
                            .ToArray());
                    }

                case "debugpoint":
                    RequireArgs(args, 1, name);
                    MainEngine.DebugDrawPositions.Add(EXValueConverter.AsVector3(args[0]));
                    return EXValue.Null();

                case "debugline":
                    RequireArgs(args, 2, name);
                    MainEngine.DebugDrawRays.Add((EXValueConverter.AsVector3(args[0]), EXValueConverter.AsVector3(args[1])));
                    return EXValue.Null();

                case "ispathwalkable":
                    RequireArgs(args, 2, name);
                    return EXValue.Of(AINodeUtils.IsPathWalkable(EXValueConverter.AsVector3(args[0]), EXValueConverter.AsVector3(args[1]), out _));
            }
            throw new EXScriptRuntimeError($"Unknown function '{name}'.", -1);
        }

        if (receiver.Kind != EXKind.Object)
            throw new EXScriptRuntimeError($"Cannot call '.{name}(...)' on a {receiver.Kind} value.", -1);

        if (receiver.Object.Instance is not WorldEntity target)
            throw new EXScriptRuntimeError($"'{receiver.Object.Type.TypeName}' does not support method calls.", -1);

        switch (name)
        {
            case "applyimpulse":
                RequireArgs(args, 1, name);
                target.AddImpulse(EXValueConverter.AsVector3(args[0]));
                return EXValue.Null();

            case "applyimpulseat":
                RequireArgs(args, 2, name);
                target.AddImpulseAtPosition(EXValueConverter.AsVector3(args[0]), EXValueConverter.AsVector3(args[1]));
                return EXValue.Null();

            case "applyforce":
                RequireArgs(args, 1, name);
                target.AddForce(EXValueConverter.AsVector3(args[0]));
                return EXValue.Null();

            case "applyforceat":
                RequireArgs(args, 2, name);
                target.AddForceAtPosition(EXValueConverter.AsVector3(args[0]), EXValueConverter.AsVector3(args[1]));
                return EXValue.Null();

            case "addcondition":
                {
                    RequireArgs(args, 1, name);
                    var cName = EXValueConverter.AsString(args[0]);
                    var cType = ConditionManager.GetTypeByName(cName)
                        ?? throw new EXScriptRuntimeError($"Unknown condition '{cName}'.", -1);
                    target.AddCondition((ICondition)Activator.CreateInstance(cType));
                    return EXValue.Null();
                }

            case "removecondition":
                {
                    RequireArgs(args, 1, name);
                    var cName = EXValueConverter.AsString(args[0]);
                    var cType = ConditionManager.GetTypeByName(cName)
                        ?? throw new EXScriptRuntimeError($"Unknown condition '{cName}'.", -1);
                    target.RemoveCondition(cType);
                    return EXValue.Null();
                }

            case "moveto":
                {
                    RequireArgs(args, 1, name);
                    if (target.Controller is not AIEntityTemplate ai)
                        throw new EXScriptRuntimeError($"'.moveto()' requires an AI-driven entity.", -1);

                    object moveTarget = args[0].Kind switch
                    {
                        EXKind.Vector3D => (object)EXValueConverter.AsVector3(args[0]),
                        EXKind.Object when args[0].Object.Instance is WorldEntity e => e,
                        _ => throw new EXScriptRuntimeError($"'.moveto()' expects a Vector3D or entity.", -1)
                    };
                    ai.SetTarget(moveTarget);
                    return EXValue.Null();
                }

            case "ispathcompleted":
                RequireArgs(args, 0, name);
                if (target.Controller is not AIAgent agent)
                    throw new EXScriptRuntimeError($"'.ispathcompleted()' requires an AI-driven entity.", -1);
                return EXValue.Of(agent.IsPathCompleted());
        }

        string joined = name == "SetValue"
            ? string.Join(":", args.Select(EXRuntime.Stringify))
            : string.Join(",", args.Select(EXRuntime.Stringify));

        if (name == "SetValue" && args.Length != 2)
            throw new EXScriptRuntimeError($"Invalid argument count for 'SetValue'.", -1);

        var contributed = target.CallInput(name, joined, activator);
        if (contributed != null)
        {
            chain.Value = WorldEntity.MergePassVariables(chain.Value, contributed);
            return contributed.Length == 1
                ? EXRuntime.ParseFromString(StripName(contributed[0]))
                : EXValue.Of(contributed.Select(v => EXRuntime.ParseFromString(StripName(v))).ToArray());
        }
        return EXValue.Null();
    }
    public static string StripName(string passVar)
    {
        int colonIdx = passVar.IndexOf(':');
        return colonIdx >= 0 ? passVar.Substring(colonIdx + 1) : passVar;
    }
    public void ScheduleResume(Action resume, EXSignal signal) =>
        self.queuedTasks.Add((signal.WaitSeconds, resume));

    EXValue DoTrace(string name, EXValue[] args)
    {
        RequireArgs(args, 2, 3, name);
        var origin = EXValueConverter.AsVector3(args[0]);
        var dir = Vector3.Normalize(EXValueConverter.AsVector3(args[1]));
        var maxDist = args.Length > 2 ? EXValueConverter.AsFloat(args[2]) : 8192f;
        var ray = new Ray(origin, dir);

        var data = new TraceResultData();

        if (name == "phystrace")
        {
            if (Collision.CastPhysicsWorld(ray, maxDist, out var result, self.PhysicsBodyID))
            {
                data.Hit = true;
                data.Point = origin + dir * maxDist * result.Fraction;
                data.Normal = PhysicsEngine.GetFromBodyID(result.BodyID)
                    .GetWorldSpaceSurfaceNormal(result.subShapeID2, data.Point.ToNumerics());
                PhysicsEngine.BodyMapper.TryGetValue(result.BodyID, out data.Entity);
            }
        }
        else
        {
            var hit = BSPRoot.TraceRay(ray, maxDist);
            data.Hit = hit.Hit;
            data.Point = hit.Point;
            data.Normal = hit.Normal;
        }

        return EXValue.Of(new EXObject { Type = TraceResultTypeInstance, Instance = data });
    }

    static void RequireArgs(EXValue[] args, int exact, string name) => RequireArgs(args, exact, exact, name);
    static void RequireArgs(EXValue[] args, int min, int max, string name)
    {
        if (args.Length < min || args.Length > max)
            throw new EXScriptRuntimeError($"Invalid argument count for '{name}'.", -1);
    }
}