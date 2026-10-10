using Avalonia.Controls;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.ViewModels.Editor;
public class MapEditorViewModel : ViewModelBase
{
    public MapperView? Scene { get; set; } = Design.IsDesignMode ? null : new MapperView();
}