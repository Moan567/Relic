using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Mapper.Tools;
using Rockwall2.Editor.Mapper.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2.Tools
{
    public class EntityTool : Tool
    {
        Point mousePos;
        Vector3 worldPos;
        VertexPosition[] nubs = BrushOperations.GetDebugEdges(new BoundingBox(Vector3.One * -0.25f, Vector3.One * 0.25f));

        string defaultClassname = "PointLight";
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
            var mapHit = MapTools.RaycastAllGeometry(sceneRay);
            worldPos = sceneRay.Position + sceneRay.Direction * mapHit;

            if (MouseManager.IsPressed(MouseButton.Left))
            {
                var pos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

                var entity = new EntityReference
                {
                    Position = pos,
                    EntityName = defaultClassname,
                    EntityOutputs = new System.Collections.Generic.List<(string, EntityOutput)>(),
                    Scale = Vector3.One,
                };

                var id = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == entity.EntityName);
                if (id != -1 && GlobalEditorData.EditorOverrides.overrides[id].defaultProperties != null)
                {
                    entity.Properties = new EntityProperty[GlobalEditorData.EditorOverrides.overrides[id].defaultProperties.Length];
                    Array.Copy(GlobalEditorData.EditorOverrides.overrides[id].defaultProperties, entity.Properties, entity.Properties.Length);
                }

                // Capture the entity reference itself so undo doesn't rely on a stable index
                MapTools.AddEntity(entity);
                var entityRef = entity;

                Toolbelt.UndoManager.DoOnUndo(() =>
                {
                    MapTools.RemoveEntity(entityRef);
                    MapTools.FinalizeDeletedObjects();
                });
            }

            if (MouseManager.IsPressed(MouseButton.Right) && !waitDDown)
            {
                waitDDown = true;
                Dispatcher.UIThread.Post(() =>
                {
                    var menu = new ContextMenu();
                    menu.MaxHeight = 300;
                    menu.ItemsSource = GlobalEditorData.RegisteredClassnames
                        .Select(name =>
                        {
                            var item = new MenuItem { Header = name };
                            item.Click += (s, e) => defaultClassname = name;
                            return item;
                        }).ToList();

                    menu.Closed += (s, e) => waitDDown = false;

                    menu.Open(MainWindow.Instance);
                });
            }
            else
            {
                waitDDown = false;
            }
        }
    }
}