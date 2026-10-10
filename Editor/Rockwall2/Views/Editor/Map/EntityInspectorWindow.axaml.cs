using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.ViewModels.Editor;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rockwall2;

public struct EntityPropertyInspector
{
    public string name { get; set; }
    public string hint { get; set; }
    public EntityPropertyType type { get; set; }
    public string[] options { get; set; }
    public string targetFilter { get; set; }
    public float min { get; set; }
    public float max { get; set; }
}

public struct EntityOutputModel
{
    public int index { get; set; }
    public string outputFrom { get; set; }
    public string entityTarget { get; set; }
    public string entityInputTarget { get; set; }
    public string inputParameters { get; set; }
    public float delay { get; set; }
    public int refire { get; set; }
    public bool isScripted { get; set; }
}

public partial class EntityInspectorWindow : Window
{
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    private EntityReference[] entityRefs;
    private int selectedOutput = -1;
    private int copyFromOutput = -1;
    private bool dontUpdateOutputs = false;
    private bool dontRunCallbacks = false;

    private ScriptEditorWindow? openScriptWindow;
    private bool scriptWindowDocked = false;
    private PixelPoint inspectorDragStartPos;
    private PixelPoint scriptDragStartPos;
    private const int SnapThreshold = 24;

    private EntityInspectorViewModel VM => (EntityInspectorViewModel)DataContext!;
    private ObservableCollection<EntityOutputModel> outputRows => VM.OutputRows;

    public EntityInspectorWindow() : this(Array.Empty<EntityReference>()) { }

    public EntityInspectorWindow(IEnumerable<EntityReference> entityRefs)
    {
        DataContext = new EntityInspectorViewModel();
        InitializeComponent();

        if (Design.IsDesignMode) return;

        Refresh(entityRefs);

        // Wire events in code to avoid XamlX compiled-bindings rejecting handler strings
        titleBar.PointerPressed += TitleBar_PointerPressed;
        titleBar.PointerMoved += TitleBar_PointerMoved;
        titleBar.PointerReleased += TitleBar_PointerReleased;
        classnameBox.TextChanged += classnameBox_TextChanged;
        classnameBox.SelectionChanged += classnameBox_SelectionChanged;
        propertyList.SelectionChanged += propertyList_SelectionChanged;
        outputVisualizer.SelectionChanged += outputVisualizer_SelectionChanged;
        outputFrom.SelectionChanged += outputFrom_SelectionChanged;
        entityTarget.SelectionChanged += entityTarget_SelectionChanged;
        entityInput.SelectionChanged += entityInput_SelectionChanged;
        inputParameter.TextChanged += inputParameter_TextChanged;
        inputDelay.ValueChanged += inputDelay_ValueChanged;
        inputRefire.ValueChanged += inputRefire_ValueChanged;
        btnAdd.Click += Add_Click;
        btnCopy.Click += Copy_Click;
        btnPaste.Click += Paste_Click;
        btnDelete.Click += Delete_Click;
        btnMoveUp.Click += MoveUp_Click;
        btnMoveDown.Click += MoveDown_Click;
        isScriptedToggle.IsCheckedChanged += isScriptedToggle_IsCheckedChanged;
        btnOpenScript.Click += OpenScript_Click;

        Closing += (s, arg) => { openScriptWindow?.Close(); };

        // Populate the classname AutoCompleteBox
        classnameBox.ItemsSource = GlobalEditorData.RegisteredClassnames;
        classnameBox.IsEnabled = true;
    }
    protected override void OnClosed(EventArgs e)
    {
        titleBar.PointerPressed -= TitleBar_PointerPressed;
        titleBar.PointerMoved -= TitleBar_PointerMoved;
        titleBar.PointerReleased -= TitleBar_PointerReleased;
        classnameBox.TextChanged -= classnameBox_TextChanged;
        classnameBox.SelectionChanged -= classnameBox_SelectionChanged;
        propertyList.SelectionChanged -= propertyList_SelectionChanged;
        outputVisualizer.SelectionChanged -= outputVisualizer_SelectionChanged;
        outputFrom.SelectionChanged -= outputFrom_SelectionChanged;
        entityTarget.SelectionChanged -= entityTarget_SelectionChanged;
        entityInput.SelectionChanged -= entityInput_SelectionChanged;
        inputParameter.TextChanged -= inputParameter_TextChanged;
        inputDelay.ValueChanged -= inputDelay_ValueChanged;
        inputRefire.ValueChanged -= inputRefire_ValueChanged;
        btnAdd.Click -= Add_Click;
        btnCopy.Click -= Copy_Click;
        btnPaste.Click -= Paste_Click;
        btnDelete.Click -= Delete_Click;
        btnMoveUp.Click -= MoveUp_Click;
        btnMoveDown.Click -= MoveDown_Click;
        isScriptedToggle.IsCheckedChanged -= isScriptedToggle_IsCheckedChanged;
        btnOpenScript.Click -= OpenScript_Click;

        base.OnClosed(e);
    }

    public void Refresh(IEnumerable<EntityReference> entityRefs)
    {
        this.entityRefs = entityRefs.ToArray();

        if (this.entityRefs.Length > 1)
        {
            outputTab.IsEnabled = false;
        }
        else
        {
            BuildOutputList();
        }

        ShowCombinedClassname([.. this.entityRefs.Select(e => e.EntityName)]);

        propertyEditorHost.IsEnabled = false;

        BuildCommonPropertyList();
    }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);

        inspectorDragStartPos = Position;
        if (scriptWindowDocked && openScriptWindow != null)
            scriptDragStartPos = openScriptWindow.Position;
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isWindowDragInEffect) return;

        Point current = e.GetPosition(this);
        Point delta = current - cursorDragStart;
        var newPos = this.PointToScreen(delta);
        Position = newPos;

        if (scriptWindowDocked && openScriptWindow != null)
        {
            var totalDelta = newPos - inspectorDragStartPos;
            openScriptWindow.Position = new PixelPoint(
                scriptDragStartPos.X + totalDelta.X,
                scriptDragStartPos.Y + totalDelta.Y);
        }
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => isWindowDragInEffect = false;

    private void RefreshOutput()
    {
        dontUpdateOutputs = true;
        var outp = entityRefs[0].EntityOutputs[selectedOutput];
        var id = selectedOutput;

        outputRows[selectedOutput] = new EntityOutputModel
        {
            index = id + 1,
            outputFrom = outp.Item1,
            entityTarget = outp.Item2.EntityTarget,
            entityInputTarget = outp.Item2.EntityInputTarget,
            inputParameters = outp.Item2.InputParameters,
            delay = outp.Item2.Delay,
            refire = outp.Item2.Refire,
        };

        outputVisualizer.SelectedIndex = id;
        selectedOutput = id;
        dontUpdateOutputs = false;
    }

    private static IEnumerable<string> GetAllTargetableEntityNames(string classFilter = null)
    {
        IEnumerable<EntityReference> entities = MapTools.Entities;

        if (!string.IsNullOrEmpty(classFilter))
            entities = entities.Where(e => e.EntityName == classFilter);

        return entities.Select(e => e.Name).Where(e => !string.IsNullOrEmpty(e)).Distinct();
    }

    private void BuildOutputList()
    {
        if (entityRefs[0].EntityOutputs == null) return;

        outputRows.Clear();

        for (int i = 0; i < entityRefs[0].EntityOutputs.Count; i++)
        {
            var outp = entityRefs[0].EntityOutputs[i];
            outputRows.Add(new EntityOutputModel
            {
                index = i + 1,
                outputFrom = outp.Item1,
                entityTarget = outp.Item2.EntityTarget,
                entityInputTarget = outp.Item2.EntityInputTarget,
                inputParameters = outp.Item2.InputParameters,
                delay = outp.Item2.Delay,
                refire = outp.Item2.Refire,
                isScripted = outp.Item2.Script != null,
            });
        }

        if (!GlobalEditorData.RegisteredEntityMeta.TryGetValue(entityRefs[0].EntityName, out var meta)) return;

        outputFrom.Items.Clear();
        foreach (var item in meta.Outputs)
            if (!outputFrom.Items.Contains(item)) outputFrom.Items.Add(item);

        entityTarget.Items.Clear();
        entityTarget.Items.Add("_activator");
        foreach (var entityName in GetAllTargetableEntityNames())
            entityTarget.Items.Add(entityName);
    }

    private void BuildOutputPropertyEditors()
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;

        dontUpdateOutputs = true;
        outputFrom.SelectedItem = entityRefs[0].EntityOutputs[selectedOutput].Item1;
        entityTarget.SelectedItem = entityRefs[0].EntityOutputs[selectedOutput].Item2.EntityTarget;
        UpdateInputs();
        entityInput.SelectedItem = entityRefs[0].EntityOutputs[selectedOutput].Item2.EntityInputTarget;
        inputParameter.Text = entityRefs[0].EntityOutputs[selectedOutput].Item2.InputParameters;
        inputDelay.Text = entityRefs[0].EntityOutputs[selectedOutput].Item2.Delay.ToString();
        inputRefire.Text = entityRefs[0].EntityOutputs[selectedOutput].Item2.Refire.ToString();

        isScriptedToggle.IsChecked = entityRefs[0].EntityOutputs[selectedOutput].Item2.ScriptSource != null;

        UpdateScriptModeEnabledState();

        dontUpdateOutputs = false;

        UpdateMoveButtonStates();
    }

    private void UpdateInputs()
    {
        var targetName = entityTarget.SelectedItem as string;
        if (string.IsNullOrEmpty(targetName)) return;

        string searchName;
        if (targetName == "_activator")
        {
            searchName = "PointLight"; // _activator has no placed entity to resolve
        }
        else
        {
            var targetEntity = MapTools.Entities.FirstOrDefault(e => e.Name == targetName);
            if (targetEntity == null) return;
            searchName = targetEntity.EntityName;
        }

        if (entityInput.Items != null && entityInput.Items.Count > 0)
        {
            dontRunCallbacks = true;
            entityInput.SelectedIndex = -1;
            entityInput.Items.Clear();
            dontRunCallbacks = false;
        }

        if (!GlobalEditorData.RegisteredEntityMeta.TryGetValue(searchName, out var meta)) return;

        foreach (var item in meta.Inputs)
            if (!entityInput.Items.Contains(item)) entityInput.Items.Add(item);
    }

    private void BuildCommonPropertyList()
    {
        var commonNames = new HashSet<string>();

        foreach (var entity in entityRefs)
        {
            if (!GlobalEditorData.RegisteredEntityMeta.TryGetValue(entity.EntityName, out var meta))
            {
                commonNames.Clear();
                break;
            }
            var thisNames = new HashSet<string>(meta.Properties.Select(p => p.Name));
            if (commonNames.Count == 0) commonNames = thisNames; else commonNames.IntersectWith(thisNames);
        }

        propertyList.Items.Clear();
        propertyList.Items.Add(new EntityPropertyInspector { name = "Target Name", hint = "The name by which other entities can target this entity.", type = EntityPropertyType.String });
        propertyList.Items.Add(new EntityPropertyInspector { name = "Move Parent", hint = "If this entity is not simulated, it will be parented to the entity selected.", type = EntityPropertyType.EntityTarget });
        propertyList.Items.Add(new EntityPropertyInspector { name = "Spawn Pitch", hint = "The spawn rotation around the X axis, in degrees", type = EntityPropertyType.Float });
        propertyList.Items.Add(new EntityPropertyInspector { name = "Spawn Yaw", hint = "The spawn rotation around the Y axis, in degrees", type = EntityPropertyType.Float });
        propertyList.Items.Add(new EntityPropertyInspector { name = "Spawn Roll", hint = "The spawn rotation around the Z axis, in degrees", type = EntityPropertyType.Float });

        foreach (var entity in entityRefs)
        {
            if (!GlobalEditorData.RegisteredEntityMeta.TryGetValue(entity.EntityName, out var meta))
                continue;

            foreach (var prop in meta.Properties)
            {
                if (commonNames.Contains(prop.Name) && prop.Name != "Target Name")
                {
                    commonNames.Remove(prop.Name);
                    propertyList.Items.Add(new EntityPropertyInspector
                    {
                        name = prop.Name,
                        hint = prop.Hint,
                        type = prop.Type,
                        options = prop.Options,
                        targetFilter = prop.TargetFilter,
                        min = prop.Min,
                        max = prop.Max
                    });
                }
            }
        }
    }

    private void classnameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {

    }

    private void classnameBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (classnameBox.Text == "<multiple values>") return;

        var selected = classnameBox.SelectedItem as string;
        if (selected == null || !GlobalEditorData.RegisteredClassnames.Contains(selected)) return;

        for (int i = 0; i < entityRefs.Length; i++)
        {
            entityRefs[i].EntityName = selected;
            var id = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == entityRefs[i].EntityName);
            var defaults = id != -1 ? GlobalEditorData.EditorOverrides.overrides[id].defaultProperties
                : GlobalEditorData.RegisteredEntityMeta.TryGetValue(selected, out var meta) ? meta.DefaultProperties : null;

            if (defaults != null)
            {
                var oldProperties = entityRefs[i].Properties;
                entityRefs[i].Properties = new EntityProperty[defaults.Length];
                Array.Copy(defaults, entityRefs[i].Properties, entityRefs[i].Properties.Length);

                for (int j = 0; j < entityRefs[i].Properties.Length; j++)
                {
                    if (oldProperties != null && oldProperties.Any(t => t.Name == entityRefs[i].Properties[j].Name))
                    {
                        var old = oldProperties.First(t => t.Name == entityRefs[i].Properties[j].Name);
                        entityRefs[i].Properties[j].Value = old.Value;
                    }
                }
            }
        }

        BuildCommonPropertyList();
        RefreshPropertySelection();

        BuildOutputList();
    }

    private void CommitPropertyValue(EntityPropertyInspector selected, string value)
    {
        if (value == "<multiple values>") return;

        if (selected.name == "Target Name")
        {
            for (int i = 0; i < entityRefs.Length; i++) entityRefs[i].Name = value ?? string.Empty;
            return;
        }
        if (selected.name == "Move Parent")
        {
            for (int i = 0; i < entityRefs.Length; i++) entityRefs[i].entityMoveParentName = value ?? string.Empty;
            return;
        }
        if (selected.name is "Spawn Pitch" or "Spawn Yaw" or "Spawn Roll")
        {
            if (!float.TryParse(value, out float d)) return;
            for (int i = 0; i < entityRefs.Length; i++)
            {
                if (selected.name == "Spawn Pitch") entityRefs[i].SpawnRotation.Y = d;
                else if (selected.name == "Spawn Yaw") entityRefs[i].SpawnRotation.X = d;
                else entityRefs[i].SpawnRotation.Z = d;
            }
            return;
        }

        for (int i = 0; i < entityRefs.Length; i++)
        {
            int pID = -1;
            if (entityRefs[i].Properties != null)
                pID = Array.FindIndex(entityRefs[i].Properties, x => x.Name == selected.name);

            if (pID == -1)
            {
                var property = new EntityProperty { Name = selected.name, Value = value ?? string.Empty };
                entityRefs[i].Properties = (entityRefs[i].Properties ?? Array.Empty<EntityProperty>()).Append(property).ToArray();
                continue;
            }

            entityRefs[i].Properties[pID].Value = value ?? string.Empty;
        }
    }

    private void propertyList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshPropertySelection();
    }

    private void RefreshPropertySelection()
    {
        if (propertyList.SelectedItem is not EntityPropertyInspector selected)
        {
            propertyEditorHost.IsEnabled = false;
            return;
        }

        List<string> values = selected.name switch
        {
            "Target Name" => entityRefs.Select(i => i.Name).Distinct().ToList(),
            "Move Parent" => entityRefs.Select(i => i.entityMoveParentName).Distinct().ToList(),
            "Spawn Pitch" => entityRefs.Select(i => i.SpawnRotation.Y.ToString()).Distinct().ToList(),
            "Spawn Yaw" => entityRefs.Select(i => i.SpawnRotation.X.ToString()).Distinct().ToList(),
            "Spawn Roll" => entityRefs.Select(i => i.SpawnRotation.Z.ToString()).Distinct().ToList(),
            _ => entityRefs.Select(i =>
            {
                var p = i.Properties != null ? Array.Find(i.Properties, x => x.Name == selected.name) : default;
                return p.Value ?? string.Empty;
            }).ToList()
        };

        propertyEditorHost.IsEnabled = true;
        propertyEditorHost.Content = BuildEditorFor(selected, values);
        expectedPropType.Text = $"Expected Value Type: {selected.type}";
        hintText.Text = selected.hint;
    }

    private Control BuildEditorFor(EntityPropertyInspector prop, List<string> currentValues)
    {
        bool allSame = currentValues.Distinct().Count() == 1;
        string current = allSame ? currentValues.FirstOrDefault() ?? string.Empty : "<multiple values>";

        switch (prop.type)
        {
            case EntityPropertyType.Enum:
                var combo = new ComboBox { ItemsSource = prop.options, SelectedItem = allSame ? current : null, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SelectionChanged += (s, e) => CommitPropertyValue(prop, combo.SelectedItem as string);
                return combo;

            case EntityPropertyType.EntityTarget:
                var targets = GetAllTargetableEntityNames(prop.targetFilter).ToList();
                targets.Insert(0, "");
                var targetCombo = new ComboBox { ItemsSource = targets, SelectedItem = allSame ? current : null, HorizontalAlignment = HorizontalAlignment.Stretch };
                targetCombo.SelectionChanged += (s, e) => CommitPropertyValue(prop, targetCombo.SelectedItem as string);
                return targetCombo;

            case EntityPropertyType.Float:
                var num = new NumericUpDown { Value = decimal.TryParse(current, out var d) ? d : 0, FormatString = "0.###" };
                if (prop.max > prop.min) { num.Minimum = (decimal)prop.min; num.Maximum = (decimal)prop.max; }
                num.ValueChanged += (s, e) => CommitPropertyValue(prop, (num.Value ?? 0).ToString());
                return num;

            case EntityPropertyType.Bool:
                var check = new CheckBox { IsChecked = current == "1" };
                check.IsCheckedChanged += (s, e) => CommitPropertyValue(prop, check.IsChecked == true ? "1" : "0");
                return check;

            case EntityPropertyType.Material:
                var matPanel = new DockPanel { LastChildFill = true };

                var matPreview = new Image
                {
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                DockPanel.SetDock(matPreview, Dock.Left);

                var browseButton = new Button { Content = "Browse...", Margin = new Thickness(6, 0, 0, 0) };
                DockPanel.SetDock(browseButton, Dock.Right);

                var matLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };

                void RefreshMatPreview(string materialName)
                {
                    matLabel.Text = string.IsNullOrEmpty(materialName) ? "(none)" : materialName;
                    if (!string.IsNullOrEmpty(materialName)
                        && GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int idx)
                        && GlobalEditorData.TexturesAsImages != null
                        && idx < GlobalEditorData.TexturesAsImages.Length)
                    {
                        matPreview.Source = GlobalEditorData.TexturesAsImages[idx].Image;
                        matPreview.IsVisible = true;
                    }
                    else
                    {
                        matPreview.IsVisible = false;
                    }
                }
                RefreshMatPreview(allSame ? current : "");

                browseButton.Click += async (s, e) =>
                {
                    int result = await MaterialPicker.PickAsync(this);
                    if (result == -1)
                    {
                        return;
                    }

                    string name = GlobalMapData.LoadedMaterials[result].Name;
                    RefreshMatPreview(name);
                    CommitPropertyValue(prop, name);
                };

                matPanel.Children.Add(matPreview);
                matPanel.Children.Add(browseButton);
                matPanel.Children.Add(matLabel);
                return matPanel;

            default:
                var box = new TextBox { Text = current, FontSize = 12, FontWeight = FontWeight.Light };
                box.TextChanged += (s, e) => CommitPropertyValue(prop, box.Text);
                return box;
        }
    }

    private void ShowCombinedClassname(List<string> values)
    {
        bool allSame = values.Distinct().Count() == 1;
        classnameBox.Text = allSame ? values[0] : "<multiple values>";
        classnameBox.IsEnabled = allSame;
    }

    private void outputVisualizer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        selectedOutput = outputVisualizer.SelectedIndex;
        BuildOutputPropertyEditors();
    }

    private void outputFrom_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (outputFrom.SelectedItem == null || string.IsNullOrEmpty(outputFrom.SelectedItem as string)) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item1 = outputFrom.SelectedItem as string;
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        RefreshOutput();
    }

    private void entityTarget_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (entityTarget.SelectedItem == null || string.IsNullOrEmpty(entityTarget.SelectedItem as string)) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item2.EntityTarget = entityTarget.SelectedItem as string;
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        UpdateInputs();
        RefreshOutput();
    }

    private void entityInput_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (dontRunCallbacks) return;
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (entityInput.SelectedItem == null || string.IsNullOrEmpty(entityInput.SelectedItem as string)) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item2.EntityInputTarget = entityInput.SelectedItem as string;
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        RefreshOutput();
    }

    private void inputParameter_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (dontRunCallbacks) return;
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (entityRefs[0].EntityOutputs[selectedOutput].Item2.InputParameters == inputParameter.Text) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item2.InputParameters = inputParameter.Text ?? string.Empty;
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        RefreshOutput();
    }
    private void inputDelay_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item2.Delay = (float)(inputDelay.Value ?? 0);
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        RefreshOutput();
    }

    private void inputRefire_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        if (dontUpdateOutputs) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        item.Item2.Refire = (int)(inputRefire.Value ?? -1);
        entityRefs[0].EntityOutputs[selectedOutput] = item;
        RefreshOutput();
    }

    private void Add_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        entityRefs[0].EntityOutputs ??= new List<(string, EntityOutput)>();
        entityRefs[0].EntityOutputs.Add(new("", new EntityOutput()));
        BuildOutputList();
    }

    private void Delete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;

        if (openScriptWindow != null)
        {
            if (openScriptWindow.OutputIndex == selectedOutput)
                openScriptWindow.Close();
            else if (openScriptWindow.OutputIndex > selectedOutput)
                openScriptWindow.Retarget(openScriptWindow.OutputIndex - 1);
        }

        entityRefs[0].EntityOutputs.RemoveAt(selectedOutput);
        BuildOutputList();
    }

    private void Copy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        copyFromOutput = selectedOutput;
    }

    private void Paste_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (copyFromOutput == -1 || copyFromOutput >= entityRefs[0].EntityOutputs.Count) return;
        var from = entityRefs[0].EntityOutputs[copyFromOutput];
        entityRefs[0].EntityOutputs.Add(new(from.Item1, from.Item2));
        BuildOutputList();
    }

    private void UpdateMoveButtonStates()
    {
        int count = entityRefs[0].EntityOutputs?.Count ?? 0;
        btnMoveUp.IsEnabled = selectedOutput > 0 && selectedOutput < count;
        btnMoveDown.IsEnabled = selectedOutput >= 0 && selectedOutput < count - 1;
    }

    private void SwapOutputs(int a, int b)
    {
        (entityRefs[0].EntityOutputs[a], entityRefs[0].EntityOutputs[b]) =
            (entityRefs[0].EntityOutputs[b], entityRefs[0].EntityOutputs[a]);

        copyFromOutput = -1; // shuffle invalidates prev copy

        if (openScriptWindow != null)
        {
            if (openScriptWindow.OutputIndex == a) openScriptWindow.Retarget(b);
            else if (openScriptWindow.OutputIndex == b) openScriptWindow.Retarget(a);
        }

        BuildOutputList();
        outputVisualizer.SelectedIndex = b;

        UpdateMoveButtonStates();
    }

    private void MoveUp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (selectedOutput <= 0 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;
        SwapOutputs(selectedOutput, selectedOutput - 1);
    }

    private void MoveDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count - 1) return;
        SwapOutputs(selectedOutput, selectedOutput + 1);
    }


    private void isScriptedToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (dontUpdateOutputs) return;
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;

        var item = entityRefs[0].EntityOutputs[selectedOutput];
        if (!isScriptedToggle.IsChecked == true) item.Item2.ScriptSource = null;
        if (isScriptedToggle.IsChecked == true) item.Item2.ScriptSource = string.Empty;
        entityRefs[0].EntityOutputs[selectedOutput] = item;

        UpdateScriptModeEnabledState();
        RefreshOutput();
    }
    private void OpenScript_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedOutput == -1 || selectedOutput >= entityRefs[0].EntityOutputs.Count) return;

        if (openScriptWindow != null)
        {
            openScriptWindow.Retarget(selectedOutput);
            openScriptWindow.Activate();
            return;
        }

        openScriptWindow = new ScriptEditorWindow(this, selectedOutput);
        openScriptWindow.Closed += (_, _) => { openScriptWindow = null; scriptWindowDocked = false; };

        openScriptWindow.Position = new PixelPoint(Position.X + (int)Bounds.Width, Position.Y);
        scriptWindowDocked = true;

        openScriptWindow.Show(MainWindow.Instance);
    }
    private void UpdateScriptModeEnabledState()
    {
        bool scripted = isScriptedToggle.IsChecked == true;
        entityTarget.IsEnabled = !scripted;
        entityInput.IsEnabled = !scripted;
        inputParameter.IsEnabled = !scripted;
        btnOpenScript.IsEnabled = scripted;
    }

    public string GetScriptSource(int outputIndex) => outputIndex >= 0 && outputIndex < entityRefs[0].EntityOutputs.Count
                                                        ? entityRefs[0].EntityOutputs[outputIndex].Item2.ScriptSource ?? string.Empty
                                                        : string.Empty;

    public void SetScriptSource(int outputIndex, string source)
    {
        if (outputIndex < 0 || outputIndex >= entityRefs[0].EntityOutputs.Count) return;
        var item = entityRefs[0].EntityOutputs[outputIndex];
        item.Item2.ScriptSource = source;
        entityRefs[0].EntityOutputs[outputIndex] = item;
    }
    public void OnScriptWindowDragEnded(ScriptEditorWindow scriptWindow)
    {
        var inspectorTopRight = new PixelPoint(Position.X + (int)Bounds.Width, Position.Y);
        var scriptTopLeft = scriptWindow.Position;

        bool closeEnough =
            Math.Abs(scriptTopLeft.X - inspectorTopRight.X) <= SnapThreshold &&
            Math.Abs(scriptTopLeft.Y - inspectorTopRight.Y) <= SnapThreshold;

        if (closeEnough)
        {
            scriptWindow.Position = inspectorTopRight; // snap exactly flush
            scriptWindowDocked = true;
        }
        else
        {
            scriptWindowDocked = false;
        }
    }
}