using Avalonia.Controls;
using Rockwall2.Editor.Particles;

namespace Rockwall2.ViewModels.Editor;
public class ParticleEditorViewModel : ViewModelBase
{
    public ParticleView? Scene { get; set; } = Design.IsDesignMode ? null : new ParticleView();
}
