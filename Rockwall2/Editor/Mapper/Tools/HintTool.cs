using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Mapper.Tools;
using Rockwall2.Views;

namespace Rockwall2.Tools
{
    public class HintTool : Tool
    {
        Point mousePos;
        Vector3 worldPos;
        VertexPosition[] nubs = BrushOperations.GetDebugEdges(new BoundingBox(Vector3.One * -0.25f, Vector3.One * 0.25f));

        bool waitDDown;

        public override void OnDeselected()
        {
        }

        public override void OnRender(float delta)
        {
            basicEffect.DiffuseColor = Vector3.One;
            basicEffect.World = Matrix.CreateWorld(
                Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize,
                Vector3.Forward, Vector3.Up);
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, nubs, 0, nubs.Length / 2);
            }
        }

        public override void OnSelected()
        {
        }

        public override async void OnUpdate(float delta)
        {
            mousePos = MouseLocal;

            var sceneRay = SceneRay;

            float minDist = float.MaxValue;
            var mapHit = MapTools.RaycastMapGeometry(sceneRay, true, !IsUsedIn2D);
            if (mapHit.distance > 0f && mapHit.face != -1 && mapHit.brush != -1)
            {
                worldPos = sceneRay.Position + sceneRay.Direction * mapHit.distance;
                minDist = mapHit.distance;
            }

            if (MouseManager.IsPressed(MouseButton.Left))
            {
                var pos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

                var hint = new Hint
                {
                    Position = pos,
                };

                Toolbelt.OpenHint(hint, () => waitDDown = false);

                MapTools.AddHint(hint);

                Toolbelt.UndoManager.DoOnUndo(() =>
                {
                    MapTools.RemoveHint(hint);
                    MapTools.FinalizeDeletedObjects();
                });
            }
        }
    }
}