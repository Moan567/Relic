using CommunityToolkit.Mvvm.ComponentModel;
using Rockwall2.ViewModels;
using System.Collections.ObjectModel;

namespace Rockwall2.ViewModels.Editor;

public partial class EntityInspectorViewModel : ViewModelBase
{
    public ObservableCollection<EntityOutputModel> OutputRows { get; } = new();
}