using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using Rockwall2.Editor.Mapper.Tools;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Editor.Mapper.Utils;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Common.Input;
using Avalonia.Input;
using Rockwall2.Views;

namespace Rockwall2.Tools
{
    public class TranslateTool : Tool
    {
        bool wasDragging = false;

        public override void OnDeselected() { wasDragging = false; }
        public override void OnSelected() { }

        public override void OnRender(float delta)
        {
            MapperView.Instance.DrawGizmo();
        }

        public override void OnUpdate(float delta)
        {
            bool isDragging = GizmoTranslate.IsDragging;

            if (isDragging && !wasDragging)
                foreach (var obj in Toolbelt.SelectedObjects)
                    obj.PrepareMove();

            if (!isDragging && wasDragging)
                Toolbelt.CreateUndoStateForAllSelectedObjects();

            wasDragging = isDragging;

            GizmoTranslate.Update();
        }
    }
}