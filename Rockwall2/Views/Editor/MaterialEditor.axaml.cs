using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaInside.MonoGame;
using MsBox.Avalonia;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2;

public partial class MaterialEditor : UserControl
{
    // ---- list / header ---------------------------------------------------------------
    ListBox materialList;
    TextBox filterBox;
    TextBlock countLabel, headerName, headerPath, workingFileLabel, statusText;
    Image headerThumbnail;
    TabControl tabControl;

    // ---- general ---------------------------------------------------------------------
    TextBox nameBox, surfaceTypeBox, shaderBox;
    Slider reflectivitySlider;
    NumericUpDown reflectivityBox, texelsPerUnitBox;

    // ---- rendering -------------------------------------------------------------------
    CheckBox transparentBox, alphaClipBox, noCullBox;

    // ---- texture slots --------------------------------------------------------------
    StackPanel slotPanel;
    TextBox extraSlotNameBox;

    // ---- 3D preview ------------------------------------------------------------------
    MonoGameControl previewControl;

    // ---- shader flags ---------------------------------------------------------------
    StackPanel flagPanel;

    // ---- image tab ------------------------------------------------------------------
    Image imagePreview;
    WriteableBitmap workingImage;
    string workingImagePath;
    NumericUpDown cropX, cropY, cropW, cropH, resizeW, resizeH;
    ColorPicker tintPicker;
    Slider tintAmount;

    // ---- state ----------------------------------------------------------------------
    Material editing;
    string editingFile;
    string editingRelativeFile;
    bool hasSelection;

    readonly MaterialPreviewView previewView = new();
    Dictionary<string, string> materialFiles = new();
    Dictionary<string, Bitmap> thumbnailCache = new();
    string filterText = "";

    public MaterialEditor()
    {
        InitializeComponent();

        BuildUi();
        AttachedToVisualTree += (_, _) => ReloadMaterials();
    }

    // ===================================================================================
    //  Layout
    // ===================================================================================

    void BuildUi()
    {
        var rootGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("280,*,*"),
            RowDefinitions = new RowDefinitions("*")
        };

        var left = BuildLeftPanel();
        var center = BuildCenterPanel();
        var right = BuildRightPanel();

        Grid.SetColumn(left, 0);
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);

        rootGrid.Children.Add(left);
        rootGrid.Children.Add(center);
        rootGrid.Children.Add(right);

        root.Child = rootGrid;
    }

    Control BuildLeftPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(8, 4, 4, 4)
        };
        Grid.SetColumn(panel, 0);

        filterBox = new TextBox { Watermark = "Search materials", Margin = new Thickness(0, 0, 0, 6) };
        filterBox.TextChanged += (_, _) => { filterText = (filterBox.Text ?? "").Trim(); ApplyFilter(); };
        Grid.SetRow(filterBox, 1);
        panel.Children.Add(filterBox);

        materialList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a))
        };
        materialList.SelectionChanged += (_, _) => Select(materialList.SelectedIndex);
        Grid.SetRow(materialList, 2);
        panel.Children.Add(materialList);

        countLabel = new TextBlock { FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4) };
        Grid.SetRow(countLabel, 3);
        panel.Children.Add(countLabel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var newButton = new Button { Content = "New", Padding = new Thickness(10, 3) };
        newButton.Click += (_, _) => NewMaterial();
        var duplicateButton = new Button { Content = "Duplicate", Padding = new Thickness(10, 3) };
        duplicateButton.Click += (_, _) => DuplicateMaterial();
        var refreshButton = new Button { Content = "Refresh", Padding = new Thickness(10, 3) };
        refreshButton.Click += (_, _) => { thumbnailCache.Clear(); ReloadMaterials(); };

        buttons.Children.Add(newButton);
        buttons.Children.Add(duplicateButton);
        buttons.Children.Add(refreshButton);
        Grid.SetRow(buttons, 4);
        panel.Children.Add(buttons);

        return panel;
    }

    Control BuildCenterPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4)
        };
        // ---- header: thumbnail + name + relative path ----
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(4, 4, 4, 10) };

        headerThumbnail = new Image { Width = 128, Height = 128, Stretch = Stretch.Uniform };
        var thumbFrame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            Child = headerThumbnail
        };
        header.Children.Add(thumbFrame);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        headerName = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        headerPath = new TextBlock { FontSize = 11, Foreground = Brushes.DarkGray, TextWrapping = TextWrapping.Wrap };
        titles.Children.Add(headerName);
        titles.Children.Add(headerPath);
        header.Children.Add(titles);

        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        // ---- tabs: properties / image editing ----
        tabControl = new TabControl { Padding = new Thickness(0, 4, 0, 4) };
        tabControl.Items.Add(new TabItem { Header = "Material", Content = BuildPropertiesTab() });
        tabControl.Items.Add(new TabItem { Header = "Image", Content = BuildImageTab() });
        Grid.SetRow(tabControl, 1);
        panel.Children.Add(tabControl);

        // ---- footer: working file + revert/save ----
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(4, 6, 4, 4) };
        workingFileLabel = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(workingFileLabel, 0);
        footer.Children.Add(workingFileLabel);

        var deleteButton = new Button { Content = "Delete", Margin = new Thickness(12, 0, 0, 0) };
        deleteButton.Click += (_, _) => DeleteMaterial();
        Grid.SetColumn(deleteButton, 1);
        footer.Children.Add(deleteButton);

        var revertButton = new Button { Content = "Revert", Margin = new Thickness(6, 0, 0, 0) };
        revertButton.Click += (_, _) => Revert();
        Grid.SetColumn(revertButton, 2);
        footer.Children.Add(revertButton);

        var saveButton = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0) };
        saveButton.Click += (_, _) => SaveMaterial();
        Grid.SetColumn(saveButton, 3);
        footer.Children.Add(saveButton);

        Grid.SetRow(footer, 2);
        panel.Children.Add(footer);

        return panel;
    }

    Control BuildPropertiesTab()
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(4) };
        scroll.Content = stack;

        // ---- General ----
        // The rows stack must be a *different* object from the one the Group border is
        // parented into -- nesting a panel inside itself overflows the visual tree.
        var generalRows = new StackPanel { Spacing = 8 };
        var generalGroup = Group("General", generalRows, null);
        var general = generalRows;

        nameBox = new TextBox();
        nameBox.TextChanged += (_, _) => { if (hasSelection) editing.Name = nameBox.Text; };
        general.Children.Add(Row("Name", nameBox));

        surfaceTypeBox = new TextBox();
        surfaceTypeBox.TextChanged += (_, _) => { if (hasSelection) editing.SurfaceType = surfaceTypeBox.Text; };
        general.Children.Add(Row("Surface type", surfaceTypeBox));

        var reflectivityRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        reflectivitySlider = new Slider { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        reflectivitySlider.ValueChanged += (_, _) =>
        {
            if (hasSelection) editing.Reflectivity = (float)reflectivitySlider.Value;
            reflectivityBox.Value = (decimal)reflectivitySlider.Value;
            previewView.SetSurface(editing.Reflectivity, ShineFromReflectivity(editing.Reflectivity));
        };
        reflectivityBox = new NumericUpDown { Minimum = 0, Maximum = 1, Increment = 0.05m, Width = 70 };
        reflectivityBox.ValueChanged += (_, _) =>
        {
            if (!hasSelection) return;
            double v = (double)(reflectivityBox.Value ?? 0m);
            editing.Reflectivity = (float)v;
            reflectivitySlider.Value = v;
            previewView.SetSurface(editing.Reflectivity, ShineFromReflectivity(editing.Reflectivity));
        };
        Grid.SetColumn(reflectivityBox, 1);
        reflectivityRow.Children.Add(reflectivitySlider);
        reflectivityRow.Children.Add(reflectivityBox);
        general.Children.Add(Row("Reflectivity", reflectivityRow));

        texelsPerUnitBox = new NumericUpDown { Minimum = 0, Maximum = 8192, Increment = 1, Width = 70 };
        texelsPerUnitBox.ValueChanged += (_, _) => { if (hasSelection) editing.TexelsPerUnit = (int)(texelsPerUnitBox.Value ?? 0m); };

        var texelsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        texelsRow.Children.Add(texelsPerUnitBox);
        texelsRow.Children.Add(Hint("0 = default (512)"));
        general.Children.Add(Row("Texels per unit", texelsRow));

        stack.Children.Add(generalGroup);

        // ---- Rendering ----
        var renderingRows = new StackPanel { Spacing = 8 };
        var renderingGroup = Group("Rendering", renderingRows, null);
        var rendering = renderingRows;

        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        transparentBox = new CheckBox { Content = "Transparent" };
        transparentBox.IsCheckedChanged += (_, _) => { if (hasSelection) editing.Transparent = transparentBox.IsChecked == true; };
        alphaClipBox = new CheckBox { Content = "Alpha clip" };
        alphaClipBox.IsCheckedChanged += (_, _) => { if (hasSelection) editing.AlphaClip = alphaClipBox.IsChecked == true; };
        noCullBox = new CheckBox { Content = "No cull" };
        noCullBox.IsCheckedChanged += (_, _) => { if (hasSelection) editing.NoCull = noCullBox.IsChecked == true; };
        flags.Children.Add(transparentBox);
        flags.Children.Add(alphaClipBox);
        flags.Children.Add(noCullBox);
        rendering.Children.Add(flags);

        shaderBox = new TextBox { Watermark = "(default world shader)" };
        shaderBox.TextChanged += (_, _) => { if (hasSelection) editing.ShaderName = shaderBox.Text; };
        rendering.Children.Add(Row("Shader", shaderBox));

        stack.Children.Add(renderingGroup);

        // ---- Textures ----
        slotPanel = new StackPanel { Spacing = 6 };

        var addSlot = new Button { Content = "Add slot", Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Right };
        addSlot.Click += (_, _) => AddSlotRow(extraSlotNameBox?.Text, null, false);
        extraSlotNameBox = new TextBox { Watermark = "custom slot name", Width = 140, VerticalAlignment = VerticalAlignment.Center };

        var slotHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var slotTitle = new TextBlock { Text = "Textures", FontWeight = FontWeight.Bold, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slotTitle, 0);
        slotHeader.Children.Add(slotTitle);
        Grid.SetColumn(extraSlotNameBox, 1);
        slotHeader.Children.Add(extraSlotNameBox);
        Grid.SetColumn(addSlot, 2);
        slotHeader.Children.Add(addSlot);

        stack.Children.Add(Group(null, slotPanel, slotHeader));

        // ---- Shader flags ----
        flagPanel = new StackPanel { Spacing = 4 };
        var addFlag = new Button { Content = "Add flag", Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Right };

        var flagHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var flagTitle = new TextBlock { Text = "Shader flags", FontWeight = FontWeight.Bold, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(flagTitle, 0);
        flagHeader.Children.Add(flagTitle);
        Grid.SetColumn(addFlag, 1);
        flagHeader.Children.Add(addFlag);
        addFlag.Click += (_, _) => AddShaderFlag();

        stack.Children.Add(Group(null, flagPanel, flagHeader));

        return scroll;
    }

    Control BuildImageTab()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(4) };

        imagePreview = new Image { Stretch = Stretch.Uniform, Height = 400, Width = 400 };
        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            Child = imagePreview
        };
        Grid.SetColumn(frame, 0);
        grid.Children.Add(frame);

        var controls = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0), Width = 340 };
        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);

        controls.Children.Add(SectionHeader("Crop"));
        cropX = Num(0); cropY = Num(0); cropW = Num(0); cropH = Num(0);
        controls.Children.Add(CompactRow("X", cropX, "Y", cropY));
        controls.Children.Add(CompactRow("W", cropW, "H", cropH));
        var cropButton = new Button { Content = "Apply Crop", HorizontalAlignment = HorizontalAlignment.Left };
        cropButton.Click += (_, _) => ApplyCrop();
        controls.Children.Add(cropButton);

        controls.Children.Add(SectionHeader("Resize"));
        resizeW = Num(0); resizeH = Num(0);
        controls.Children.Add(CompactRow("W", resizeW, "H", resizeH));
        var resizeButton = new Button { Content = "Apply Resize", HorizontalAlignment = HorizontalAlignment.Left };
        resizeButton.Click += (_, _) => ApplyResize();
        controls.Children.Add(resizeButton);

        controls.Children.Add(SectionHeader("Recolour"));
        tintPicker = new ColorPicker { Height = 90, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        tintAmount = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Width = 200 };
        controls.Children.Add(tintPicker);
        controls.Children.Add(tintAmount);
        var tintButton = new Button { Content = "Apply Tint", HorizontalAlignment = HorizontalAlignment.Left };
        tintButton.Click += (_, _) => ApplyTint();
        controls.Children.Add(tintButton);

        var editButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var revertImage = new Button { Content = "Revert Image" };
        revertImage.Click += (_, _) => LoadWorkingImage(true);
        var saveImage = new Button { Content = "Save Image" };
        saveImage.Click += (_, _) => SaveWorkingImage();
        editButtons.Children.Add(revertImage);
        editButtons.Children.Add(saveImage);
        controls.Children.Add(editButtons);

        statusText = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        Grid.SetRowSpan(statusText, 1);
        controls.Children.Add(statusText);

        return grid;
    }

    Control BuildRightPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4)
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        var title = new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var shapeBox = new ComboBox { Width = 110 };
        foreach (var shape in Enum.GetValues<MaterialPreviewView.PreviewShape>())
            shapeBox.Items.Add(shape);
        shapeBox.SelectedIndex = 0;
        shapeBox.SelectionChanged += (_, _) =>
        {
            if (shapeBox.SelectedItem is MaterialPreviewView.PreviewShape s) previewView.Shape = s;
        };
        Grid.SetColumn(shapeBox, 1);
        header.Children.Add(shapeBox);

        var resetLight = new Button { Content = "Reset light", Margin = new Thickness(6, 0, 0, 0) };
        resetLight.Click += (_, _) => previewView.ResetLight();
        Grid.SetColumn(resetLight, 2);
        header.Children.Add(resetLight);

        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        previewControl = new MonoGameControl { Host = App.Host, Scene = previewView };
        previewView.SetInputSurface(previewControl);
        previewControl.PointerEntered += (_, _) => previewControl.Focus();
        Grid.SetRow(previewControl, 1);
        panel.Children.Add(previewControl);

        // A lone sphere is a poor reflection test: one smooth surface, one highlight, one
        // reflection direction. Scattered rotated cubes give flat faces at many angles,
        // which is what actually reveals fresnel falloff and a mirror finish.
        var footerPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };

        var testToggle = new CheckBox { Content = "Reflection test scene" };
        testToggle.IsCheckedChanged += (_, _) => previewView.TestSceneEnabled = testToggle.IsChecked == true;
        footerPanel.Children.Add(testToggle);

        var cubeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        cubeRow.Children.Add(new TextBlock { Text = "Cubes", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });

        var cubeCount = new NumericUpDown { Minimum = 1, Maximum = 64, Increment = 1, Value = 24, Width = 80 };
        cubeCount.ValueChanged += (_, _) => previewView.SetTestSceneCubeCount((int)(cubeCount.Value ?? 24));
        cubeRow.Children.Add(cubeCount);

        var pbrToggle = new CheckBox { Content = "Reflections", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        pbrToggle.IsCheckedChanged += (_, _) => previewView.UsePbr = pbrToggle.IsChecked == true;
        cubeRow.Children.Add(pbrToggle);

        footerPanel.Children.Add(cubeRow);

        footerPanel.Children.Add(new TextBlock
        {
            Text = "Left drag: orbit    Shift + left drag: pan    Scroll: zoom    Drag light gizmo: light",
            FontSize = 10,
            Foreground = Brushes.DarkGray
        });

        Grid.SetRow(footerPanel, 2);
        panel.Children.Add(footerPanel);

        return panel;
    }

    // ===================================================================================
    //  Material list
    // ===================================================================================

    void ReloadMaterials()
    {
        if (GlobalMapData.LoadedMaterials == null) return;

        string previous = hasSelection ? editing.Name : null;

        materialFiles = ContentAssetStore.ScanMaterialFiles();
        materialList.Items.Clear();

        // Group by the folder the .cmt lives in, matching how the material tree is authored.
        var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < GlobalMapData.LoadedMaterials.Length; i++)
        {
            var name = GlobalMapData.LoadedMaterials[i].Name;

            string folder = ".";
            if (materialFiles.TryGetValue(name, out var absolute))
            {
                string relative = ContentAssetStore.ToDisplayPath(absolute);
                int slash = relative.LastIndexOf('/');
                if (slash > 0) folder = relative.Substring(0, slash);
            }

            if (!groups.TryGetValue(folder, out var bucket))
                groups[folder] = bucket = new List<int>();

            bucket.Add(i);
        }

        ListBoxItem selectAfter = null;

        foreach (var group in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var header = new TextBlock
            {
                Text = group.Key == "." ? "(root)" : group.Key,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.DarkGray,
                Margin = new Thickness(4, 8, 4, 2)
            };
            materialList.Items.Add(header);

            foreach (int index in group.Value.OrderBy(i => GlobalMapData.LoadedMaterials[i].Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = BuildListItem(index, out string name);
                materialList.Items.Add(item);

                if (name == previous) selectAfter = item;
            }
        }

        ApplyFilter();

        // Report the source folder, not the build output -- that's the one people author in.
        string root = ContentAssetStore.WorkingMaterialsRoot;
        if (!Directory.Exists(root)) root = GlobalEditorData.MountedMaterialsPath;
        countLabel.Text = $"{GlobalMapData.LoadedMaterials.Length} materials in {root}";

        if (selectAfter != null) materialList.SelectedItem = selectAfter;
        else SelectFirstItem();
    }

    /// <summary>
    /// Land on something rather than showing an empty editor. Also guards the case where
    /// every material is filtered out -- picking a hidden item would desync the list.
    /// </summary>
    void SelectFirstItem()
    {
        foreach (var obj in materialList.Items)
            if (obj is ListBoxItem item && item.IsVisible)
            {
                materialList.SelectedItem = item;
                return;
            }
    }

    ListBoxItem BuildListItem(int index, out string name)
    {
        var material = GlobalMapData.LoadedMaterials[index];
        name = material.Name;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2) };
        panel.Children.Add(new Image
        {
            Source = GetThumbnail(material),
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform
        });

        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = material.Name, FontSize = 12, Foreground = Brushes.White });
        text.Children.Add(new TextBlock
        {
            Text = materialFiles.TryGetValue(material.Name, out var abs)
                ? ContentAssetStore.ToDisplayPath(abs)
                : material.Name,
            FontSize = 9,
            Foreground = Brushes.DarkGray
        });
        panel.Children.Add(text);

        return new ListBoxItem { Content = panel, Tag = index };
    }

    void ApplyFilter()
    {
        int visible = 0;

        foreach (var obj in materialList.Items)
        {
            if (obj is not ListBoxItem item) continue;

            var material = GlobalMapData.LoadedMaterials[(int)item.Tag];
            bool match = filterText.Length == 0
                || material.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0
                || (materialFiles.TryGetValue(material.Name, out var abs)
                    && ContentAssetStore.ToDisplayPath(abs).IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);

            item.IsVisible = match;
            if (match) visible++;
        }

        if (filterText.Length > 0 && statusText != null)
        {
            if (visible == 0)
            {
                materialList.SelectedItem = null;
                hasSelection = false;
            }
            else if (materialList.SelectedItem is ListBoxItem sel && !sel.IsVisible)
            {
                SelectFirstItem();
            }

            statusText.Text = $"{visible} of {GlobalMapData.LoadedMaterials.Length} materials match '{filterText}'.";
        }
    }

    Bitmap GetThumbnail(Material material)
    {
        string key = material.Name + "|" + material.TextureName;
        if (thumbnailCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = TryLoadBitmap(ContentAssetStore.FindTextureFile(material.TextureName));
        thumbnailCache[key] = bitmap;
        return bitmap;
    }

    // ===================================================================================
    //  Selection
    // ===================================================================================

    void Select(int listIndex)
    {
        if (listIndex < 0 || listIndex >= materialList.Items.Count) return;
        if (materialList.Items[listIndex] is not ListBoxItem item) return;

        var material = GlobalMapData.LoadedMaterials[(int)item.Tag];

        // Material is a struct, so the assignment copies every field -- but TexturePaths is a
        // reference and would stay shared with the live entry in GlobalMapData. Clone it, or
        // typing in a slot box would silently edit the live material.
        editing = material;
        editing.TexturePaths = material.TexturePaths == null
            ? null
            : new Dictionary<string, string>(material.TexturePaths);
        editing.ShaderFlags = material.ShaderFlags == null
            ? null
            : new Dictionary<string, bool>(material.ShaderFlags);

        editingFile = materialFiles.TryGetValue(material.Name, out var file) ? file : null;
        editingRelativeFile = ContentAssetStore.ToDisplayPath(editingFile);
        hasSelection = true;

        headerName.Text = material.Name;
        headerPath.Text = editingFile ?? editingRelativeFile;
        headerThumbnail.Source = GetThumbnail(material);
        workingFileLabel.Text = editingFile != null ? $"Working File: {editingRelativeFile}" : "Working File: (unsaved)";

        // Assigning these fires their change handlers, which write straight back into
        // `editing` -- harmless because each one round-trips the same value.
        nameBox.Text = material.Name;
        surfaceTypeBox.Text = material.SurfaceType;
        shaderBox.Text = material.ShaderName;
        reflectivitySlider.Value = material.Reflectivity;
        reflectivityBox.Value = (decimal)material.Reflectivity;
        texelsPerUnitBox.Value = material.TexelsPerUnit;
        transparentBox.IsChecked = material.Transparent;
        alphaClipBox.IsChecked = material.AlphaClip;
        noCullBox.IsChecked = material.NoCull;

        RebuildSlotRows();
        RebuildFlagRows();
        LoadWorkingImage(false);
        previewView.SetMaterial(editing);
        previewView.SetSurface(editing.Reflectivity, ShineFromReflectivity(editing.Reflectivity));
    }

    // ===================================================================================
    //  Texture slots
    // ===================================================================================

    /// <summary>Well-known slots get friendly labels; anything else shows its raw key.</summary>
    static string SlotLabel(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "texture" => "Albedo",
            "specular" => "Specular",
            "normal" => "Normal",
            _ => key
        };
    }

    void RebuildSlotRows()
    {
        slotPanel.Children.Clear();

        var keys = new List<string>();
        if (editing.TexturePaths != null) keys.AddRange(editing.TexturePaths.Keys);

        // Always show the three standard slots, even when the material doesn't use them.
        foreach (var standard in new[] { "texture", "specular", "normal" })
            if (!keys.Contains(standard)) keys.Insert(Math.Min(keys.Count, StandardOrder(standard)), standard);

        foreach (var key in keys)
        {
            string value = editing.TexturePaths != null && editing.TexturePaths.TryGetValue(key, out var v) ? v : null;
            AddSlotRow(key, value, true);
        }
    }

    static int StandardOrder(string key) => key switch { "texture" => 0, "specular" => 1, _ => 2 };

    void AddSlotRow(string key, string value, bool existing)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,140,*,Auto,Auto,Auto"), Margin = new Thickness(0, 2, 0, 2) };

        var thumb = new Image
        {
            Source = TryLoadBitmap(ContentAssetStore.FindTextureFile(value)),
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        var thumbFrame = new Border { Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), Child = thumb };
        Grid.SetColumn(thumbFrame, 0);
        row.Children.Add(thumbFrame);

        var label = new TextBlock
        {
            Text = SlotLabel(key),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = Brushes.WhiteSmoke
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        var box = new TextBox { Text = value ?? "", VerticalAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            SetSlot(key, box.Text);
            thumb.Source = TryLoadBitmap(ContentAssetStore.FindTextureFile(box.Text));
            previewView.SetMaterial(editing);
        };
        Grid.SetColumn(box, 2);
        row.Children.Add(box);

        var browse = new Button { Content = "Browse...", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) => await BrowseSlot(box);
        Grid.SetColumn(browse, 3);
        row.Children.Add(browse);

        var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        clear.Click += (_, _) => { box.Text = ""; };
        Grid.SetColumn(clear, 4);
        row.Children.Add(clear);

        if (!existing && !IsStandardSlot(key))
        {
            var remove = new Button { Content = "x", Margin = new Thickness(4, 0, 0, 0) };
            remove.Click += (_, _) => { editing.TexturePaths?.Remove(key); RebuildSlotRows(); };
            Grid.SetColumn(remove, 5);
            row.Children.Add(remove);
        }

        slotPanel.Children.Add(row);
    }

    static bool IsStandardSlot(string key) => key is "texture" or "specular" or "normal";

    void SetSlot(string key, string value)
    {
        if (!hasSelection) return;

        editing.TexturePaths ??= new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(value)) editing.TexturePaths.Remove(key);
        else editing.TexturePaths[key] = value;
    }

    async Task BrowseSlot(TextBox box)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Texture",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files == null || files.Count == 0) return;

        string path = files[0].TryGetLocalPath();
        if (path == null) return;

        // .cmt paths are stored relative to the working root, without an extension.
        string relative = Path.GetRelativePath(GlobalEditorData.WorkingDirectory, path)
            .Replace('\\', '/');
        box.Text = Path.ChangeExtension(relative, null);
    }

    // ===================================================================================
    //  Shader flags
    // ===================================================================================

    void RebuildFlagRows()
    {
        flagPanel.Children.Clear();

        if (editing.ShaderFlags == null || editing.ShaderFlags.Count == 0)
        {
            flagPanel.Children.Add(Hint("No shader flags set."));
            return;
        }

        foreach (var key in editing.ShaderFlags.Keys.ToList())
            AddFlagRow(key, editing.ShaderFlags[key]);
    }

    void AddFlagRow(string key, bool value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 2) };

        var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        check.IsCheckedChanged += (_, _) => { if (hasSelection) editing.ShaderFlags[key] = check.IsChecked == true; };
        Grid.SetColumn(check, 0);
        row.Children.Add(check);

        var box = new TextBox { Text = key, Margin = new Thickness(6, 0, 6, 0) };
        box.TextChanged += (_, _) =>
        {
            if (!hasSelection || box.Text == key || editing.ShaderFlags == null) return;

            bool wasSet = editing.ShaderFlags.Remove(key);
            editing.ShaderFlags[box.Text] = wasSet;
            key = box.Text;
        };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);

        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) => { editing.ShaderFlags?.Remove(key); RebuildFlagRows(); };
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        flagPanel.Children.Add(row);
    }

    void AddShaderFlag()
    {
        editing.ShaderFlags ??= new Dictionary<string, bool>();

        string key = "flag";
        for (int i = 1; editing.ShaderFlags.ContainsKey(key); i++) key = $"flag{i}";

        editing.ShaderFlags[key] = true;
        RebuildFlagRows();
    }

    // ===================================================================================
    //  Image editing
    // ===================================================================================

    void LoadWorkingImage(bool resetControls)
    {
        workingImage = null;
        workingImagePath = null;

        if (!hasSelection) return;

        string path = ContentAssetStore.FindTextureFile(editing.TextureName);
        if (path == null)
        {
            imagePreview.Source = null;
            SetStatus("No colour texture file found for this material.");
            return;
        }

        workingImagePath = path;

        try
        {
            workingImage = ImageEditing.Load(path);
            imagePreview.Source = workingImage;

            if (resetControls || (cropW.Value ?? 0) == 0 || (cropH.Value ?? 0) == 0)
                SyncSizeControls();

            SetStatus($"{Path.GetFileName(path)} ({workingImage.PixelSize.Width}x{workingImage.PixelSize.Height}). " +
                "Edits apply to the working copy; use Save Image to write it back.");
        }
        catch (Exception e)
        {
            SetStatus($"Could not load texture: {e.Message}");
        }
    }

    void ApplyCrop()
    {
        if (workingImage == null) return;

        workingImage = ImageEditing.Crop(workingImage,
            (int)(cropX.Value ?? 0), (int)(cropY.Value ?? 0),
            (int)(cropW.Value ?? 0), (int)(cropH.Value ?? 0));

        imagePreview.Source = workingImage;
        SyncSizeControls();
        RefreshPreview();
    }

    void ApplyResize()
    {
        if (workingImage == null) return;

        workingImage = ImageEditing.Resize(workingImage, (int)(resizeW.Value ?? 0), (int)(resizeH.Value ?? 0));

        imagePreview.Source = workingImage;
        SyncSizeControls();
        RefreshPreview();
    }

    void ApplyTint()
    {
        if (workingImage == null) return;

        var colour = tintPicker.Color;

        // Recolor returns a new bitmap on purpose -- see ImageEditing.Recolor.
        workingImage = ImageEditing.Recolor(workingImage, colour.R, colour.G, colour.B, tintAmount.Value);

        imagePreview.Source = workingImage;
        RefreshPreview();
        SetStatus("Tint applied to the working copy. Save Image to write it back to disk.");
    }

    /// <summary>
    /// Pushes the in-memory working image to the 3D preview.
    ///
    /// The preview loads the texture off disk, so an unsaved edit would otherwise be
    /// invisible until Save. Spool the working bitmap to a temp PNG and preview that
    /// instead -- it keeps the preview live while you tweak, and Save still writes the
    /// real file.
    /// </summary>
    void RefreshPreview()
    {
        if (workingImage == null) return;

        try
        {
            string temp = Path.Combine(Path.GetTempPath(), "rockwall2_matpreview.png");
            ImageEditing.Save(workingImage, temp);

            if (!File.Exists(temp))
            {
                SetStatus("Preview update failed: the working image could not be written to disk.");
                return;
            }

            previewView.SetPreviewImage(temp);
        }
        catch (Exception e)
        {
            // Previously swallowed, which made a failed write indistinguishable from
            // "my edit did nothing".
            SetStatus("Preview update failed: " + e.Message);
        }
    }

    void SaveWorkingImage()
    {
        if (workingImage == null || workingImagePath == null) return;

        try
        {
            ImageEditing.Save(workingImage, workingImagePath);
            bool liveTextureReloaded = ContentAssetStore.ReloadLiveTextureFile(workingImagePath);

            string rel = Path.GetRelativePath(GlobalEditorData.WorkingDirectory, workingImagePath).Replace('\\', '/');
            ContentAssetStore.RegisterInContentMgcb(rel, build: true);
            ContentAssetStore.MirrorIntoMgcOutput(workingImagePath, rel);

            thumbnailCache.Clear();
            GlobalEditorData.LoadTex(true);
            ReloadMaterials();

            // Discard the temp preview so the next edit re-spools from the saved file.
            previewView.ClearPreviewOverride();
            previewView.ReloadTexture();

            SetStatus(liveTextureReloaded
                ? $"Saved {rel}. Live materials refreshed; rebuild Game to regenerate the .xnb."
                : $"Saved {rel}, but the live material texture could not be refreshed.");
        }
        catch (Exception e)
        {
            SetStatus($"Save failed: {e.Message}");
        }
    }

    void SyncSizeControls()
    {
        if (workingImage == null) return;

        cropX.Value = 0;
        cropY.Value = 0;
        cropW.Value = workingImage.PixelSize.Width;
        cropH.Value = workingImage.PixelSize.Height;
        resizeW.Value = workingImage.PixelSize.Width;
        resizeH.Value = workingImage.PixelSize.Height;
    }

    // ===================================================================================
    //  Save / revert / create
    // ===================================================================================

    void SaveMaterial()
    {
        if (!hasSelection) return;

        try
        {
            string name = ContentAssetStore.SanitizeMaterialName(editing.Name);

            string file = editingFile;
            if (file == null)
            {
                // Duplicate/New have no backing file yet. Keep them in the folder the original
                // lived in -- writing to the materials root would scatter them and lose the
                // grouping the material tree is organised around.
                string folder = "";
                int slash = (editingRelativeFile ?? "").LastIndexOf('/');
                if (slash > 0) folder = editingRelativeFile.Substring(0, slash + 1);

                file = Path.Combine(ContentAssetStore.WorkingMaterialsRoot,
                    folder + ContentAssetStore.SanitizeFileName(name) + ".cmt");
            }

            editing.Name = name;
            ContentAssetStore.WriteMaterial(editing, file);

            string rel = Path.GetRelativePath(GlobalEditorData.WorkingDirectory, file).Replace('\\', '/');
            ContentAssetStore.RegisterInContentMgcb(rel, build: false);
            ContentAssetStore.MirrorIntoMgcOutput(file, rel);

            editingFile = file;
            editingRelativeFile = rel;
            ContentAssetStore.AppendMaterial(editing);

            workingFileLabel.Text = $"Working File: {rel}";
            thumbnailCache.Clear();
            ReloadMaterials();
            SetStatus($"Saved {rel}.");
        }
        catch (Exception e)
        {
            SetStatus($"Save failed: {e.Message}");
        }
    }

    void Revert()
    {
        if (!hasSelection) return;
        Select(materialList.SelectedIndex);
        SetStatus("Reverted to the saved material.");
    }

    void NewMaterial()
    {
        editing = new Material
        {
            Name = ContentAssetStore.UniqueMaterialName("New Material"),
            SurfaceType = "concrete",
            Reflectivity = 1.0f,
            TexelsPerUnit = 0,
            TexturePaths = new Dictionary<string, string>()
        };

        editingFile = null;
        editingRelativeFile = "";
        hasSelection = true;

        headerName.Text = editing.Name;
        headerPath.Text = "(unsaved)";
        headerThumbnail.Source = null;
        workingFileLabel.Text = "Working File: (unsaved)";

        nameBox.Text = editing.Name;
        surfaceTypeBox.Text = editing.SurfaceType;
        shaderBox.Text = "";
        reflectivitySlider.Value = editing.Reflectivity;
        reflectivityBox.Value = 1m;
        texelsPerUnitBox.Value = 0;
        transparentBox.IsChecked = false;
        alphaClipBox.IsChecked = false;
        noCullBox.IsChecked = false;

        RebuildSlotRows();
        RebuildFlagRows();
        LoadWorkingImage(false);
        previewView.SetMaterial(editing);
        previewView.SetSurface(editing.Reflectivity, ShineFromReflectivity(editing.Reflectivity));

        tabControl.SelectedIndex = 0;
        SetStatus("New material. Fill in a texture slot, then Save.");
    }

    void DuplicateMaterial()
    {
        if (!hasSelection) return;

        Material copy = editing;
        copy.Name = ContentAssetStore.UniqueMaterialName(editing.Name + " Copy");

        // Clear the backing file so Save writes a new .cmt instead of overwriting the original,
        // but keep the relative path so the duplicate lands in the same folder.
        editingFile = null;
        editing = copy;
        hasSelection = true;

        headerName.Text = copy.Name;
        headerPath.Text = "(unsaved copy)";
        workingFileLabel.Text = "Working File: (unsaved)";
        nameBox.Text = copy.Name;

        SetStatus($"Duplicated to '{copy.Name}'. Save to write it out.");
    }

    async void DeleteMaterial()
    {
        if (!hasSelection) return;

        string name = editing.Name;

        // Tool materials drive editor behaviour (nodraw, clip, trigger, skybox); deleting one
        // silently breaks every brush that references it, so make the user confirm.
        if (FaceTextureOperations.IsToolMaterial(name)
            && !await Confirm($"'{name}' is a tool material. Brushes using it will lose their behaviour flags. Delete it anyway?"))
            return;

        if (!await Confirm($"Delete material '{name}'?"))
            return;

        try
        {
            if (editingFile != null && File.Exists(editingFile))
                File.Delete(editingFile);

            if (GlobalMapData.MaterialNameToIndex != null
                && GlobalMapData.MaterialNameToIndex.TryGetValue(name, out int index))
            {
                var list = GlobalMapData.LoadedMaterials.ToList();
                if (index < list.Count) list.RemoveAt(index);
                GlobalMapData.LoadedMaterials = list.ToArray();

                GlobalMapData.MaterialNameToIndex = list
                    .Select((m, i) => new { m.Name, i })
                    .ToDictionary(x => x.Name, x => x.i);
            }

            thumbnailCache.Clear();
            hasSelection = false;
            editingFile = null;
            GlobalEditorData.LoadTex(true);
            ReloadMaterials();

            SetStatus($"Deleted '{name}'.");
        }
        catch (Exception e)
        {
            SetStatus($"Delete failed: {e.Message}");
        }
    }

    public void TriggerImport() => ImportTexture();

    async void ImportTexture()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Texture",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                FilePickerFileTypes.ImageAll,
                new FilePickerFileType("Texture") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" } }
            }
        });

        if (files == null || files.Count == 0) return;

        string source = files[0].TryGetLocalPath();
        if (source == null) return;

        var result = ContentAssetStore.ImportTexture(source, Path.GetFileNameWithoutExtension(source), "imported");
        SetStatus(result.Message);

        if (!result.Success) return;

        thumbnailCache.Clear();
        ReloadMaterials();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var obj in materialList.Items)
            {
                if (obj is ListBoxItem item
                    && GlobalMapData.LoadedMaterials[(int)item.Tag].Name == result.Material.Name)
                {
                    materialList.SelectedItem = item;
                    materialList.ScrollIntoView(item);
                    break;
                }
            }
        });
    }

    // ===================================================================================
    //  Helpers
    // ===================================================================================

    void SetStatus(string message)
    {
        if (statusText != null) statusText.Text = message;
    }

    async Task<bool> Confirm(string message)
    {
        var result = await MessageBoxManager
            .GetMessageBoxStandard("Confirm", message, MsBox.Avalonia.Enums.ButtonEnum.YesNo)
            .ShowAsPopupAsync(Rockwall2.Views.MainWindow.Instance);

        return result == MsBox.Avalonia.Enums.ButtonResult.Yes;
    }

    /// <summary>
    /// Derives the preview's specular tightness from Reflectivity.
    ///
    /// The engine binds Reflectivity straight to the shader's "shine" parameter, which is a
    /// Blinn-Phong *exponent* -- but the editor slider is capped at 1.0, and shine=0 makes
    /// pow(x, 0) == 1, i.e. a flat highlight everywhere. So the same 0..1 slider drives two
    /// different things here: reflection strength directly, and a usable exponent scaled up
    /// from it. This is the same conflation the engine has, just made visible.
    /// </summary>
    static float ShineFromReflectivity(float reflectivity)
    {
        return Math.Clamp(reflectivity, 0f, 1f) * 2f;
    }

    static Bitmap TryLoadBitmap(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try { return new Bitmap(path); }
        catch { return null; }
    }

    /// <summary>A titled, bordered panel matching the grouped sections in the reference layout.</summary>
    static Control Group(string title, Control content, Control headerRight)
    {
        var rows = new StackPanel { Spacing = 4 };

        if (title != null || headerRight != null)
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            if (title != null)
            {
                var text = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.WhiteSmoke,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(text, 0);
                header.Children.Add(text);
            }

            if (headerRight != null)
            {
                Grid.SetColumn(headerRight, 1);
                header.Children.Add(headerRight);
            }

            rows.Children.Add(header);
        }

        rows.Children.Add(content);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 10),
            Child = rows
        };
    }

    static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = Brushes.DarkGray
    };

    static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = Brushes.WhiteSmoke,
        Margin = new Thickness(0, 8, 0, 0)
    };

    static NumericUpDown Num(decimal value) => new()
    {
        Value = value,
        Minimum = 0,
        Maximum = 16384,
        Increment = 1,
        // Wide enough for "16384" plus the spinner buttons -- at 90 the value text was
        // clipped to a single glyph.
        Width = 120
    };

    static Grid CompactRow(string label, Control field, string secondLabel = null, Control secondField = null)
    {
        // Narrow label columns: the image tab's control column is only ~340px, so Row's
        // 120px labels would squeeze the numeric boxes until their text is clipped.
        return Row(label, field, secondLabel, secondField, labelWidth: 30);
    }

    static Grid Row(string label, Control field, string secondLabel = null, Control secondField = null, double labelWidth = 120)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*,{labelWidth},*") };

        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.WhiteSmoke
        });

        Grid.SetColumn(field, 1);
        grid.Children.Add(field);

        if (secondField != null)
        {
            grid.Children.Add(new TextBlock
            {
                Text = secondLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.WhiteSmoke
            });
            Grid.SetColumn(secondField, 3);
            grid.Children.Add(secondField);
        }

        return grid;
    }
}