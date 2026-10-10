using Relic.Models;
using Relic.Utils.Animation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Utils;
using System.IO;
using System.Linq;
using static Relic.Models.CModel;

namespace Rockwall2.Editor.Mapper.Utils;
internal class ModelDisplay
{
    CModel model;
    CAnimationPlayer sequencePlayer;
    Effect modelEffect;

    private static Vector3 sunDirection = Vector3.Normalize(new Vector3(0.25f, 2, 0.5f));
    private static Color sunColor = new Color(255, 255, 255), ambientColor = new Color(255, 255, 255), fogColor = new Color(0, 0, 0);
    private static float sunStrength = 0.8f, ambientStrength = 0.0f, fogStart = 1f, fogEnd = 10f, fogStrength = 0f;

    public ModelDisplay(string path, EditorHost host)
    {
        byte[] data = File.ReadAllBytes(path);
        model = CCMDLWriter.LoadFromCCMDL(data, EditorHost.Instance.GraphicsDevice);

        modelEffect = host.Content.Load<Effect>("Shaders/ModelDefault");

        sequencePlayer = null;

        // Possibly add animation support for these?
        if (model.Animations != null && model.Animations.Count > 0 && model.Animations.Any(a => a.Name.ToLower().Equals("bindpose")))
        {
            CAnimDef bindpose = model.Animations.Find(a => a.Name.ToLower().Equals("bindpose"));
            CAnimationPlayer bindposePlayer = new CAnimationPlayer(model);
            bindposePlayer.AnimDef = bindpose;
            bindposePlayer.IsPlaying = false;
            bindposePlayer.Update(0f);
            var basePose = bindposePlayer.BoneSpaceTransforms.ToArray();
            model.BoneTransforms = basePose;

            sequencePlayer = new CAnimationPlayer(model);
            sequencePlayer.AnimDef = model.Animations[0];
        }
    }

    protected void PrepModelShaders(int material, Matrix world, Matrix view, Matrix projection)
    {
        var shader = modelEffect;

        shader.Parameters["DiffuseLightDirection"]?.SetValue(Vector3.Normalize(sunDirection));
        shader.Parameters["DiffuseColor"]?.SetValue(sunColor.ToVector3());

        shader.Parameters["World"].SetValue(world);
        shader.Parameters["View"].SetValue(view);
        shader.Parameters["Projection"].SetValue(projection);

        shader.Parameters["MainTex"].SetValue(GlobalMapData.LoadedMaterials[material].Texture);
        shader.Parameters["SpecTex"].SetValue(GlobalMapData.LoadedMaterials[material].Specular);
        shader.Parameters["NormalTex"]?.SetValue(GlobalMapData.LoadedMaterials[material].Normal);
        shader.Parameters["shine"].SetValue(GlobalMapData.LoadedMaterials[material].Reflectivity);

        shader.Parameters["cameraPos"]?.SetValue(view.Translation);
        shader.Parameters["cameraForward"]?.SetValue(view.Forward);
        shader.Parameters["WorldInverseTranspose"].SetValue(Matrix.Invert(Matrix.Transpose(world)));
        shader.Parameters["DiffuseIntensity"].SetValue(sunStrength);

        shader.Parameters["AmbientColor"].SetValue(new Vector4(ambientColor.ToVector3() * 2, 1f));
        shader.Parameters["AmbientIntensity"].SetValue(ambientStrength);

        shader.CurrentTechnique = shader.Techniques["High"];
    }
    public void Draw(Matrix world, Matrix view, Matrix projection)
    {
        if(model != null)
        {
            foreach(var bg in model.Bodygroups)
            {
                PrepModelShaders(bg.MaterialID,bg.Offset*world,view,projection);

                foreach (var pass in modelEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    bg.Mesh.Draw();
                }
            }
        }
    }
}
