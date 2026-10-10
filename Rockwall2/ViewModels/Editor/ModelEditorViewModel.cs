using Avalonia.Controls;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.ViewModels.Editor;
public class ModelEditorViewModel : ViewModelBase
{
    public ModelView? Scene { get; set; } = Design.IsDesignMode ? null : new ModelView();
}