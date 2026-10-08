using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MapCompiler.Compilation;

/// <summary>
/// Loads TrenchBroom / Quake-format .map files (both "Standard" and Valve 220 face
/// formats) and converts them into the compiler's brush/entity data model.
///
/// TrenchBroom maps are Z-up; this engine is Y-up. All geometry is converted with
/// (x, y, z)_quake -> (x, z, -y)_engine, a proper rotation (determinant +1), so
/// plane windings and texture axes stay consistent.
/// </summary>
public sealed class TBMapLoader
{
    // 1 TrenchBroom unit == 1 engine unit. Change here if a project wants Quake-scale mapping.
    const float WorldScale = 1f;

    static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

    // Properties TB authors write in identifier style that the engine expects with spaces.
    static readonly Dictionary<string, string> PropertyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SpotAngle"] = "Spot Angle",
        ["InnerAngle"] = "Inner Angle",
        ["AmbientColor"] = "Ambient Color",
        ["AmbientIntensity"] = "Ambient Intensity",
        ["DefaultColor"] = "Default Color",
        ["DefaultIntensity"] = "Default Intensity",
        ["StartEnabled"] = "Start Enabled",
        ["CameraPriority"] = "Camera Priority",
        ["StartWithControl"] = "Start With Control",
        ["OpenOnUse"] = "Open on Use",
        ["SpriteMaterial"] = "Sprite Material",
        ["SpriteSize"] = "Sprite Size",
        ["AlphaMultiplier"] = "Alpha Multiplier",
        ["ColorMultiplier"] = "Color Multiplier",
        ["FogColor"] = "Fog Color",
        ["FogIntensity"] = "Fog Intensity",
        ["FogStart"] = "Fog Start",
        ["FogEnd"] = "Fog End",
        ["ScenePath"] = "Scene Path",
        ["SoundName"] = "Sound Name",
        ["TriggerByDistance"] = "Trigger By Distance",
        ["TriggerDistance"] = "Trigger Distance",
        ["DecalMaterial"] = "Decal Material",
        ["StateToSet"] = "State to Set",
        ["EntityToCreate"] = "Entity To Create",
        ["TickInterval"] = "Tick Interval",
        ["IsEnabled"] = "Is Enabled",
        ["InitialValue"] = "Initial Value",
        ["InitialComparisonValue"] = "Initial Comparison Value",
        ["DestinationAnchor"] = "Destination Anchor",
        ["ClearMapCache"] = "Clear Map Cache",
        ["SegmentCount"] = "Segment Count",
        ["AutoMaxDistance"] = "Auto Max Distance",
        ["MinDistance"] = "Min Distance",
        ["MaxDistance"] = "Max Distance",
        ["target"] = "Target",
    };

    // Property names whose values must be "r, g, b" comma-separated for the engine.
    static readonly HashSet<string> ColorProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "Ambient Color", "Default Color", "Color Multiplier", "Fog Color"
    };

    readonly struct TextureInfo
    {
        public readonly int MaterialIndex;   // -1 when unknown
        public readonly string MaterialName; // resolved material name (or requested name when unknown)
        public readonly int Width, Height;   // pixel size of the base texture (512 fallback)
        public readonly float TexelsPerUnit;

        public TextureInfo(int index, string name, int w, int h, float tpu)
        {
            MaterialIndex = index; MaterialName = name;
            Width = w; Height = h; TexelsPerUnit = tpu;
        }
    }

    sealed class TBFace
    {
        public Vector3 P1, P2, P3;
        public string Texture = "";
        public bool Valve220;
        public Vector3 UAxis, VAxis; // 220 only
        public float UOff, VOff;
        public float Rotation;       // standard only
        public float ScaleX = 1f, ScaleY = 1f;
    }

    sealed class TBEntity
    {
        public readonly List<(string Key, string Value)> Props = new();
        public readonly List<List<TBFace>> Brushes = new();
        public readonly List<TBEntity> Children = new();
        public string? Get(string key) => Props.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    readonly List<string> _warnings = new();
    readonly Dictionary<string, TextureInfo> _texInfoCache = new(StringComparer.OrdinalIgnoreCase);
    readonly string _textureRoot;

    TBMapLoader(string textureRoot)
    {
        _textureRoot = textureRoot;
    }

    public static (Brush[] brushes, EntityReference[] entities, Terrain[] terrains) Load(string path, string textureRoot)
    {
        return new TBMapLoader(textureRoot).LoadInternal(path);
    }

    (Brush[], EntityReference[], Terrain[]) LoadInternal(string path)
    {
        var topLevel = new List<TBEntity>();
        var tok = new Tokenizer(File.ReadAllText(path));
        string? t;
        while ((t = tok.Next()) != null)
        {
            if (t == "{") topLevel.Add(ParseEntity(tok));
            else if (t != "}") _warnings.Add($"Unexpected token '{t}' at top level, line {tok.Line}.");
        }

        var brushes = new List<Brush>();
        var entities = new List<EntityReference>();

        foreach (var ent in topLevel)
            ProcessEntity(ent, brushes, entities);

        CompilerConsole.Stat("Brushes", brushes.Count);
        CompilerConsole.Stat("Entities", entities.Count);

        var distinct = _warnings.Distinct().ToList();
        if (distinct.Count > 0)
        {
            CompilerConsole.Warn($"{_warnings.Count} import warning(s):");
            foreach (var w in distinct.Take(25))
                CompilerConsole.Warn("  " + w);
            if (distinct.Count > 25)
                CompilerConsole.Warn($"  ... and {distinct.Count - 25} more");
        }

        return (brushes.ToArray(), entities.ToArray(), Array.Empty<Terrain>());
    }

    /// <summary>
    /// Handles worldspawn, brush entities, point entities, and flattens TB groups/layers.
    /// </summary>
    void ProcessEntity(TBEntity ent, List<Brush> brushes, List<EntityReference> entities)
    {
        string type = ent.Get("_tb_type") ?? "";
        if (type == "_tb_group" || type == "_tb_layer")
        {
            if (ent.Get("_tb_linked_group_id") != null)
                _warnings.Add("Linked groups are flattened during import.");

            foreach (var b in ent.Brushes)
                TryAddBrush(b, brushes, null);
            foreach (var child in ent.Children)
                ProcessEntity(child, brushes, entities);
            return;
        }

        string classname = ent.Get("classname") ?? "";

        if (classname.Equals("worldspawn", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var b in ent.Brushes)
                TryAddBrush(b, brushes, null);
            return;
        }

        if (string.IsNullOrEmpty(classname))
        {
            _warnings.Add($"Skipping entity with no classname ({ent.Props.Count} properties).");
            return;
        }

        var brushIndices = new List<int>();
        foreach (var b in ent.Brushes)
            TryAddBrush(b, brushes, brushIndices);

        entities.Add(ConvertEntity(ent, classname, brushIndices));
    }

    void TryAddBrush(List<TBFace> tbFaces, List<Brush> brushes, List<int>? brushIndices)
    {
        int index = AddBrush(tbFaces, brushes);
        if (index >= 0) brushIndices?.Add(index);
    }

    EntityReference ConvertEntity(TBEntity src, string classname, List<int> brushIndices)
    {
        var props = new List<EntityProperty>();
        foreach (var (key, value) in src.Props)
        {
            if (key.Equals("classname", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("targetname", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("angle", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("angles", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("mangle", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("_tb_", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = PropertyAliases.TryGetValue(key, out var alias) ? alias : key;
            string val = value;
            if (ColorProperties.Contains(name))
                val = NormalizeColor(val);
            props.Add(new EntityProperty { Name = name, Value = val });
        }

        bool isBrushEntity = brushIndices.Count > 0;

        Vector3 position = Vector3.Zero;
        string? origin = src.Get("origin");
        if (origin != null && TryParseVec3(origin, out var oq))
            position = Conv(oq);
        else if (!isBrushEntity)
            _warnings.Add($"Point entity '{classname}' has no valid origin, placed at world origin.");

        return new EntityReference
        {
            EntityName = classname,
            Name = src.Get("targetname"),
            Position = position,
            SpawnRotation = ParseRotation(src),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Properties = props.ToArray(),
            EntityOutputs = new List<(string, EntityOutput)>(),
            BrushIndices = isBrushEntity ? brushIndices : null,
        };
    }

    /// <summary>
    /// TB "angle" is yaw-only (Quake degrees, 0 = +X/east). TB "angles"/"mangle" is
    /// "pitch yaw roll" (Quake convention, positive pitch = down). Engine SpawnRotation
    /// is (yaw, pitch, roll) degrees fed to Matrix.CreateFromYawPitchRoll, where the
    /// lighting convention is "direction towards the source".
    /// </summary>
    static Vector3 ParseRotation(TBEntity ent)
    {
        string? angles = ent.Get("angles") ?? ent.Get("mangle");
        if (angles != null && TryParseVec3(angles, out var a))
            return new Vector3(a.Y - 90f, -a.X, a.Z);

        string? angle = ent.Get("angle");
        if (angle != null && float.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
            return new Vector3(yaw - 90f, 0f, 0f);

        return Vector3.Zero;
    }

    int AddBrush(List<TBFace> tbFaces, List<Brush> brushes)
    {
        var centroid = Vector3.Zero;
        foreach (var f in tbFaces) centroid += f.P1 + f.P2 + f.P3;
        if (tbFaces.Count > 0) centroid /= (tbFaces.Count * 3f);

        var faces = new List<Face>();
        var faceSource = new List<TBFace>();
        foreach (var tb in tbFaces)
        {
            Vector3 normal = Vector3.Cross(tb.P2 - tb.P1, tb.P3 - tb.P1);
            if (normal.LengthSquared() < 1e-12f)
            {
                _warnings.Add($"Degenerate plane on texture '{tb.Texture}', face skipped.");
                continue;
            }
            normal = Vector3.Normalize(normal);

            // Interior must lie on the negative side (RecalculateBrushGeometry treats >0 as outside).
            if (Vector3.Dot(normal, centroid - tb.P1) > 0f)
                normal = -normal;

            var tex = ResolveTexture(tb.Texture);

            faces.Add(new Face
            {
                Plane = new Plane(normal, -Vector3.Dot(normal, tb.P1)),
                Normal = normal,
                Drawn = true,
                MaterialName = tex.MaterialName,
                Surface = tex.MaterialIndex >= 0 ? tex.MaterialIndex : DevFallbackIndex(),
                TScaleX = 1f,
                TScaleY = 1f,
                LuxelScale = 1f,
                UvProjectionMode = tb.Valve220 ? UVProjectionMode.Face : UVProjectionMode.World,
                UvRotation = 0f,
                Indices = Array.Empty<int>(),
                editorVerts = new List<VertexLightmapped>(),
                Decals = Array.Empty<EnvironmentalDecal>(),
            });
            faceSource.Add(tb);
        }

        var brush = new Brush
        {
            Faces = faces.ToArray(),
            Vertices = Array.Empty<Vector3>(),
            UVs = Array.Empty<Vector2>(),
            LightmapUVs = Array.Empty<Vector2>(),
            Position = Vector3.Zero,
        };

        BrushOperations.RecalculateBrushGeometry(ref brush);

        // Drop faces the geometry pass found degenerate, keeping the TB source face aligned.
        var keptFaces = new List<Face>();
        var keptSource = new List<TBFace>();
        for (int i = 0; i < brush.Faces.Length; i++)
        {
            if (brush.Faces[i].Indices != null && brush.Faces[i].Indices.Length >= 3)
            {
                keptFaces.Add(brush.Faces[i]);
                keptSource.Add(faceSource[i]);
            }
        }
        brush.Faces = keptFaces.ToArray();

        if (brush.Faces.Length < 4 || brush.Vertices.Length < 4)
        {
            _warnings.Add($"Degenerate brush ({brush.Faces.Length} faces) skipped.");
            return -1;
        }

        // Match the editor's convention: vertices are local to Brush.Position (min corner).
        Vector3 min = brush.Vertices.Aggregate(Vector3.Min);
        for (int i = 0; i < brush.Vertices.Length; i++)
            brush.Vertices[i] -= min;
        brush.Position = min;
        BrushOperations.RecalculateBrushPlanes(ref brush);

        var max = Vector3.Zero;
        foreach (var v in brush.Vertices) max = Vector3.Max(max, v);
        brush.Width = max.X; brush.Height = max.Y; brush.Length = max.Z;

        for (int f = 0; f < brush.Faces.Length; f++)
        {
            var face = brush.Faces[f];
            var tb = keptSource[f];
            var tex = ResolveTexture(tb.Texture);

            if (face.Plane.HasValue)
                face.Normal = Vector3.Normalize(face.Plane.Value.Normal);

            GetBakingAxes(tb, face, out Vector3 uAxis, out Vector3 vAxis);
            Vector3 n = face.Normal;
            face.Tangent = SafeNormalize(uAxis - n * Vector3.Dot(uAxis, n));
            face.Binormal = SafeNormalize(vAxis - n * Vector3.Dot(vAxis, n));

            // Seed scale signs before SetUVAxes (it may flip TScaleY to match the desired axes),
            // then store scale/offset in engine terms (UvCalculator assumes 512px textures)
            // so any downstream UV recompute lands in the same place.
            face.TScaleX = tb.ScaleX;
            face.TScaleY = tb.ScaleY;
            UvCalculator.SetUVAxes(ref face, uAxis, vAxis);
            face.TScaleX = tb.ScaleX * tex.Width * tex.TexelsPerUnit / 512f;
            face.TScaleY = (face.TScaleY < 0f ? -1f : 1f) * MathF.Abs(tb.ScaleY) * tex.Height * tex.TexelsPerUnit / 512f;
            face.TOffX = tb.UOff * 512f / tex.Width;
            face.TOffY = tb.VOff * 512f / tex.Height;

            brush.Faces[f] = face;
        }

        BakeUVs(ref brush, keptSource);

        brushes.Add(brush);
        return brushes.Count - 1;
    }

    /// <summary>
    /// World-space texture axes actually used for UV baking, in engine coordinates.
    /// </summary>
    void GetBakingAxes(TBFace tb, Face face, out Vector3 uAxis, out Vector3 vAxis)
    {
        if (tb.Valve220)
        {
            uAxis = tb.UAxis;
            vAxis = tb.VAxis;
        }
        else
        {
            // Quake "standard" axial projection (in Quake coordinates), then convert.
            QuakeAxialAxes(face.Normal, out Vector3 uq, out Vector3 vq);
            uAxis = Conv(uq);
            vAxis = Conv(vq);

            if (MathF.Abs(tb.Rotation) > 0.001f)
            {
                float rad = -tb.Rotation * MathF.PI / 180f;
                float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
                Vector3 u2 = cos * uAxis - sin * vAxis;
                Vector3 v2 = sin * uAxis + cos * vAxis;
                uAxis = u2; vAxis = v2;
            }
        }
    }

    static void QuakeAxialAxes(Vector3 engineNormal, out Vector3 uq, out Vector3 vq)
    {
        // Convert back to Quake coords to pick the classic axial base.
        Vector3 n = new(engineNormal.X, -engineNormal.Z, engineNormal.Y);
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (az >= ax && az >= ay) { uq = Vector3.UnitX; vq = -Vector3.UnitY; }
        else if (ax >= ay) { uq = Vector3.UnitY; vq = -Vector3.UnitZ; }
        else { uq = Vector3.UnitX; vq = -Vector3.UnitZ; }
    }

    void BakeUVs(ref Brush brush, List<TBFace> keptSource)
    {
        int vertCount = brush.Vertices.Length;
        var uvs = new Vector2[vertCount];
        var lmUvs = new Vector2[vertCount];

        for (int f = 0; f < brush.Faces.Length; f++)
        {
            var face = brush.Faces[f];
            if (face.Indices == null || face.Indices.Length == 0) continue;

            var tb = keptSource[f];
            var tex = ResolveTexture(tb.Texture);

            GetBakingAxes(tb, face, out Vector3 uAxis, out Vector3 vAxis);

            float sx = tb.ScaleX == 0f ? 1f : tb.ScaleX;
            float sy = tb.ScaleY == 0f ? 1f : tb.ScaleY;

            foreach (int vi in face.Indices)
            {
                Vector3 worldPos = brush.Vertices[vi] + brush.Position;
                float u = (Vector3.Dot(worldPos, uAxis) / sx + tb.UOff) / tex.Width;
                float v = (Vector3.Dot(worldPos, vAxis) / sy + tb.VOff) / tex.Height;
                uvs[vi] = new Vector2(u, v);

                lmUvs[vi] = new Vector2(
                    Vector3.Dot(worldPos, uAxis) / 512f,
                    Vector3.Dot(worldPos, vAxis) / 512f);
            }
        }

        brush.UVs = uvs;
        brush.LightmapUVs = lmUvs;
    }

    TextureInfo ResolveTexture(string tbName)
    {
        if (_texInfoCache.TryGetValue(tbName, out var cached))
            return cached;

        string name = tbName.Replace('\\', '/').Trim();
        var mats = GlobalMapData.LoadedMaterials;
        var index = GlobalMapData.MaterialNameToIndex;

        int found = -1;

        if (index != null && mats != null)
        {
            // 1. exact material name
            if (!index.TryGetValue(name, out found))
            {
                string baseName = name[(name.LastIndexOf('/') + 1)..];

                // 2. basename as material name
                if (!index.TryGetValue(baseName, out found))
                {
                    // 3. texture path match ("dev/devred" -> "Textures/dev/devred")
                    found = -1;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string? tp = mats[i].TextureName;
                        if (tp == null) continue;
                        string rel = tp.Replace('\\', '/');
                        if (rel.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            rel.Equals("textures/" + name, StringComparison.OrdinalIgnoreCase) ||
                            rel.Equals(tbName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = i;
                            break;
                        }
                    }

                    // 4. case-insensitive material name / basename
                    if (found < 0)
                    {
                        var ci = index.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (ci.Key == null)
                            ci = index.FirstOrDefault(kv => kv.Key.Equals(baseName, StringComparison.OrdinalIgnoreCase));
                        if (ci.Key != null) found = ci.Value;
                    }
                }
            }
        }

        if (found < 0)
        {
            _warnings.Add($"Unknown texture '{tbName}' - faces will use the fallback material.");
        }

        int w = 512, h = 512;
        float tpu = 512f;
        string resolvedName = found >= 0 && mats != null && found < mats.Length
            ? mats[found].Name
            : name[(name.LastIndexOf('/') + 1)..];

        if (found >= 0 && mats != null)
        {
            tpu = mats[found].EffectiveTexelsPerUnit;
            string? texPath = mats[found].TextureName;
            if (texPath != null)
            {
                foreach (var ext in TextureExtensions)
                {
                    string full = Path.Combine(_textureRoot, texPath + ext);
                    if (File.Exists(full) && TryGetImageSize(full, out w, out h))
                        break;
                }
            }
        }

        var info = new TextureInfo(found, resolvedName, w, h, tpu);
        _texInfoCache[tbName] = info;
        return info;
    }

    static int DevFallbackIndex()
    {
        var index = GlobalMapData.MaterialNameToIndex;
        if (index == null || index.Count == 0) return 0;
        var dev = index.FirstOrDefault(kv => kv.Key.StartsWith("Dev", StringComparison.OrdinalIgnoreCase));
        return dev.Key != null ? dev.Value : index.First().Value;
    }

    static bool TryGetImageSize(string path, out int w, out int h)
    {
        w = 512; h = 512;
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            w = bmp.Width; h = bmp.Height;
            return true;
        }
        catch { return false; }
    }

    static Vector3 SafeNormalize(Vector3 v)
        => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitX;

    static Vector3 Conv(Vector3 q) => new(q.X * WorldScale, q.Z * WorldScale, -q.Y * WorldScale);

    static bool TryParseVec3(string s, out Vector3 v)
    {
        v = Vector3.Zero;
        var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            return false;
        v = new Vector3(x, y, z);
        return true;
    }

    static string NormalizeColor(string value)
    {
        if (value.Contains(',')) return value;
        var parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? string.Join(", ", parts.Take(3)) : value;
    }

    #region Tokenizer / .map grammar

    sealed class Tokenizer
    {
        readonly string _text;
        int _pos;
        public int Line { get; private set; } = 1;

        public Tokenizer(string text) { _text = text; }

        void SkipTrivia()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == '\n') { Line++; _pos++; }
                else if (char.IsWhiteSpace(c)) _pos++;
                else if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
                    while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                else break;
            }
        }

        public string? Next()
        {
            SkipTrivia();
            if (_pos >= _text.Length) return null;

            char c = _text[_pos];
            if (c is '{' or '}' or '(' or ')' or '[' or ']')
            {
                _pos++;
                return c.ToString();
            }

            if (c == '"')
            {
                _pos++;
                int start = _pos;
                while (_pos < _text.Length && _text[_pos] != '"')
                {
                    if (_text[_pos] == '\n') Line++;
                    _pos++;
                }
                string s = _text[start.._pos];
                if (_pos < _text.Length) _pos++; // closing quote
                return s;
            }

            int wordStart = _pos;
            while (_pos < _text.Length &&
                   !char.IsWhiteSpace(_text[_pos]) &&
                   _text[_pos] is not ('{' or '}' or '(' or ')' or '[' or ']'))
                _pos++;
            return _text[wordStart.._pos];
        }

        public string? Peek()
        {
            int saved = _pos, savedLine = Line;
            var t = Next();
            _pos = saved; Line = savedLine;
            return t;
        }
    }

    static float ParseFloat(Tokenizer tok, string context)
    {
        string? t = tok.Next();
        if (t == null || !float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            throw new FormatException($"Expected number ({context}) at line {tok.Line}, got '{t}'.");
        return v;
    }

    static Vector3 ParsePoint(Tokenizer tok)
    {
        Expect(tok, "(");
        float x = ParseFloat(tok, "point x");
        float y = ParseFloat(tok, "point y");
        float z = ParseFloat(tok, "point z");
        Expect(tok, ")");
        return new Vector3(x, y, z);
    }

    static void Expect(Tokenizer tok, string expected)
    {
        string? t = tok.Next();
        if (t != expected)
            throw new FormatException($"Expected '{expected}' at line {tok.Line}, got '{t}'.");
    }

    static TBEntity ParseEntity(Tokenizer tok)
    {
        var ent = new TBEntity();
        while (true)
        {
            string? t = tok.Next();
            if (t == null) throw new FormatException("Unexpected end of file inside entity.");
            if (t == "}") return ent;

            if (t == "{")
            {
                // A brush starts with '(' (a plane point); a child entity starts with '"' (a key).
                if (tok.Peek() == "(")
                    ent.Brushes.Add(ParseBrush(tok));
                else
                    ent.Children.Add(ParseEntity(tok));
            }
            else
            {
                string? value = tok.Next();
                if (value == null) throw new FormatException($"Unexpected end of file after key '{t}'.");
                ent.Props.Add((t, value));
            }
        }
    }

    static List<TBFace> ParseBrush(Tokenizer tok)
    {
        var faces = new List<TBFace>();
        while (true)
        {
            string? t = tok.Next();
            if (t == null) throw new FormatException("Unexpected end of file inside brush.");
            if (t == "}") return faces;
            if (t != "(") throw new FormatException($"Expected '(' at line {tok.Line}, got '{t}'.");

            // The opening '(' was already consumed; parse the three numbers of point 1 directly.
            float x = ParseFloat(tok, "p1 x");
            float y = ParseFloat(tok, "p1 y");
            float z = ParseFloat(tok, "p1 z");
            Expect(tok, ")");

            var face = new TBFace
            {
                P1 = Conv(new Vector3(x, y, z)),
                P2 = Conv(ParsePoint(tok)),
                P3 = Conv(ParsePoint(tok)),
                Texture = tok.Next() ?? throw new FormatException($"Expected texture name at line {tok.Line}."),
            };

            if (tok.Peek() == "[")
            {
                face.Valve220 = true;
                Expect(tok, "[");
                float ux = ParseFloat(tok, "u x"), uy = ParseFloat(tok, "u y"),
                      uz = ParseFloat(tok, "u z"), uoff = ParseFloat(tok, "u off");
                Expect(tok, "]");
                Expect(tok, "[");
                float vx = ParseFloat(tok, "v x"), vy = ParseFloat(tok, "v y"),
                      vz = ParseFloat(tok, "v z"), voff = ParseFloat(tok, "v off");
                Expect(tok, "]");
                face.UAxis = Conv(new Vector3(ux, uy, uz));
                face.VAxis = Conv(new Vector3(vx, vy, vz));
                face.UOff = uoff;
                face.VOff = voff;
                face.Rotation = ParseFloat(tok, "rotation"); // parsed but 220 axes already encode it
                face.ScaleX = ParseFloat(tok, "scale x");
                face.ScaleY = ParseFloat(tok, "scale y");
            }
            else
            {
                face.UOff = ParseFloat(tok, "x offset");
                face.VOff = ParseFloat(tok, "y offset");
                face.Rotation = ParseFloat(tok, "rotation");
                face.ScaleX = ParseFloat(tok, "scale x");
                face.ScaleY = ParseFloat(tok, "scale y");
            }

            faces.Add(face);
        }
    }

    #endregion
}
