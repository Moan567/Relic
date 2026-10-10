using Rockwall2.Editor.Model.Utils;

namespace Rockwall2.ViewModels.Editor;
public class BodygroupViewModel
{
    public string name => ModelEditorData.ActiveModel?.Bodygroups[ID].Name ?? "";
    public bool isEye => ModelEditorData.ActiveModel?.Bodygroups[ID].IsEye ?? false;
    public int ID { get; set; }
    public BodygroupViewModel(int ID) => this.ID = ID;
}

public class AnimDefViewModel
{
    public string name => ModelEditorData.ActiveModel?.Animations[ID].Name ?? "";
    public int ID { get; set; }
    public AnimDefViewModel(int ID) => this.ID = ID;
}

public class SequenceViewModel
{
    public string name => ModelEditorData.ActiveModel?.Sequences[ID].Name ?? "";
    public int ID { get; set; }
    public SequenceViewModel(int ID) => this.ID = ID;
}

public class BlendEntryViewModel
{
    public int Index { get; set; }
    public string Label { get; set; }
    public BlendEntryViewModel(int index, string label)
    {
        Index = index;
        Label = label;
    }
}