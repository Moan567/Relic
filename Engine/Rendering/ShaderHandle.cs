using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Engine
{
    public sealed class ShaderHandle
    {
        public Effect Effect { get; }
        public GLSLEffect GLSLEffect { get; }

        public bool IsDisposed => GLSLEffect != null ? GLSLEffect.IsDisposed : Effect.IsDisposed;
        public GraphicsDevice GraphicsDevice => GLSLEffect != null ? GLSLEffect.GraphicsDevice : Effect.GraphicsDevice;

        private readonly Dictionary<string, ShaderParamHandle> _paramCache = new();

        public ShaderHandle(Effect effect)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        }

        public ShaderHandle(GLSLEffect glslEffect)
        {
            GLSLEffect = glslEffect ?? throw new ArgumentNullException(nameof(glslEffect));
        }

        public static implicit operator ShaderHandle(Effect effect) => effect == null ? null : new ShaderHandle(effect);
        public static implicit operator ShaderHandle(GLSLEffect glslEffect) => glslEffect == null ? null : new ShaderHandle(glslEffect);

        public ShaderParamHandle Param(string name)
        {
            if (_paramCache.TryGetValue(name, out var cached))
                return cached;

            var handle = GLSLEffect != null
                ? new ShaderParamHandle(GLSLEffect.Parameters[name])
                : new ShaderParamHandle(Effect.Parameters[name]);

            _paramCache[name] = handle;
            return handle;
        }

        public string CurrentTechniqueName => GLSLEffect != null ? GLSLEffect.CurrentTechnique.Name : Effect.CurrentTechnique.Name;

        public void SetTechnique(string techniqueName)
        {
            if (GLSLEffect != null) GLSLEffect.CurrentTechnique = GLSLEffect.Techniques[techniqueName];
            else Effect.CurrentTechnique = Effect.Techniques[techniqueName];
        }

        public void ApplyPass(int passIndex = 0)
        {
            if (GLSLEffect != null) GLSLEffect.CurrentTechnique.Passes[passIndex].Apply();
            else Effect.CurrentTechnique.Passes[passIndex].Apply();
        }

        public void RenderEachPass(Action drawCall)
        {
            if (GLSLEffect != null)
            {
                foreach (var pass in GLSLEffect.CurrentTechnique.Passes) { pass.Apply(); drawCall(); }
            }
            else
            {
                foreach (var pass in Effect.CurrentTechnique.Passes) { pass.Apply(); drawCall(); }
            }
        }

        public override bool Equals(object obj) =>
            obj is ShaderHandle other && ReferenceEquals(Effect ?? (object)GLSLEffect, other.Effect ?? (object)other.GLSLEffect);

        public override int GetHashCode() => (Effect ?? (object)GLSLEffect).GetHashCode();
    }

    public sealed class ShaderParamHandle
    {
        private readonly EffectParameter effectParam;
        private readonly GLSLEffectParameter glslParam;

        internal ShaderParamHandle(EffectParameter effectParam) { this.effectParam = effectParam; }
        internal ShaderParamHandle(GLSLEffectParameter glslParam) { this.glslParam = glslParam; }

        public void SetValue(bool value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(int value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(float value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Vector2 value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Vector3 value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Vector4 value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Matrix value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Texture2D value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(TextureCube value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(float[] value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Vector4[] value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Vector3[] value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }
        public void SetValue(Microsoft.Xna.Framework.Matrix[] value) { glslParam?.SetValue(value); effectParam?.SetValue(value); }

        public void SetValue(Texture value)
        {
            switch (value)
            {
                case Texture2D t2d: glslParam?.SetValue(t2d); break;
                case TextureCube tc: glslParam?.SetValue(tc); break;
                case null: break;
                default:
                    if (glslParam != null)
                        throw new NotSupportedException($"GLSLEffect: unsupported texture type {value.GetType().Name}.");
                    break;
            }
            effectParam?.SetValue(value);
        }
    }
}
