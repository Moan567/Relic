using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Relic.Models;
using Relic.Models.Morph;
using Relic.Utils.Animation;
using Microsoft.Win32;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Toolbar;
using Rockwall2.Editor.Model;
using Rockwall2.Editor.Model.Utils;
using Rockwall2.ViewModels.Editor;
using Rockwall2.Views;
using SharpHook.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using static Relic.Models.CModel;

namespace Rockwall2;

public partial class ModelEditor : UserControl
{
    public Control GameView => gameControl;

    private BodygroupViewModel activeBodygroup;
    private AnimDefViewModel activeAnimDef;
    private readonly AnimationTimelineRenderer timelineRenderer = new();

    private SequenceViewModel activeBlendSequence;
    private int activeBlend1DEntryIndex = -1;
    private readonly Blend2DCanvas blend2DCanvas = new();

    private readonly PropertyEditorPanel animPropsPanel = new();
    private readonly PropertyEditorPanel animRootMotionPanel = new();
    private readonly PropertyEditorPanel eventPropsPanel = new();

    public MorphAnimator MorphAnimator;

    private bool choreoActive = false;
    private bool eventTypeHooked = false;

    private bool eyeFieldsUpdating = false;
    private bool eyeTexFieldsUpdating = false;

    private readonly ToolIconAtlas iconAtlas;
    public (int col, int row) BoneIconCoord = (7, 2);
    public (int col, int row) EyeIconCoord = (6, 2);
    public (int col, int row) AddEyeIconCoord = (5, 2);

    private int selectedLodLevelIndex = -1;
    private bool lodFieldsUpdating = false;

    private readonly PropertyEditorPanel ikEventPropsPanel = new();
    private int selectedIkChainIndex = -1;
    private int selectedIkEventIndex = -1;
    private bool ikFieldsUpdating = false;
    private bool suppressIkChainSelectionChanged = false;
    private bool suppressIkEventSelectionChanged = false;

    public ModelEditor()
    {
        if (!Design.IsDesignMode)
            DataContext = new ModelEditorViewModel();

        iconAtlas = new ToolIconAtlas(
            new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://Rockwall2/Assets/Icons/rw2icons.png"))),
            cellSize: 32);

        InitializeComponent();

        RefreshIcons();

        if (Design.IsDesignMode) return;

        gameControl.Host = App.Host;

        GameView.PointerEntered += (a, b) => GameView.Focus();

        choreoDirector.CloseRequested += () => SetChoreoActive(false);

        timelineRenderer.EventSelected += OnTimelineEventSelected;
        timelineRenderer.IKEventSelected += OnTimelineIKEventSelected;
        timelineRenderer.TimelineChanged += RedrawTimeline;

        timelineRenderer.AttachInput(
            viewAnimationTimeline,
            viewAnimationRuler,
            () => GetSelectedAnimDef(),
            () => (int)(ModelEditorData.SequencePlayer?.CurrentWholeTick ?? 0),
            ScrubTo,
            RedrawTimeline
        );

        animPropsPanel.Orientation = Orientation.Horizontal;
        animPropsPanel.Spacing = 8;
        animPropsPanel.Changed += () => { RefreshAnimationList(); RedrawTimeline(); };
        animPropsHost.Children.Add(animPropsPanel);

        animRootMotionPanel.Orientation = Orientation.Horizontal;
        animRootMotionPanel.Spacing = 6;
        animRootMotionHost.Children.Add(animRootMotionPanel);

        eventPropsPanel.Changed += RedrawTimeline;
        eventPropsHost.Children.Add(eventPropsPanel);

        ikEventPropsPanel.Changed += RefreshIkEventLabelOnly;
        ikEventPropsHost.Children.Add(ikEventPropsPanel);

        blend2DCanvas.EntrySelected += OnBlend2DEntrySelected;
        blend2DCanvas.Changed += UpdateSequenceEditPreview;
        blend2DCanvas.AttachInput(
            blend2DView,
            () => GetSelectedBlendSequence()?.Root,
            RedrawBlend2D
        );

        RefreshAll();

        boneViewer.SelectionChanged += (sender, e) =>
        {
            if (boneViewer.SelectedItem is not TreeViewItem { Tag: CBone boneRef })
                return;

            ModelEditorData.ViewingBone = -1;
            ModelEditorData.ViewingEye = -1;
            eyeViewer.SelectedItem = null;
            bonePhysicsParent.SelectedIndex = boneRef.PhysicsParentOverride == null
                ? 0
                : boneRef.PhysicsParentOverride.Index + 1;
            boneBoundsCenter.Text = $"{boneRef.BoundsCenter.X}, {boneRef.BoundsCenter.Y}, {boneRef.BoundsCenter.Z}";
            boneBoundsSize.Text = $"{boneRef.BoundsSize.X}, {boneRef.BoundsSize.Y}, {boneRef.BoundsSize.Z}";
            boneJointNormal.Text = boneRef.JointNormalHalfCone.ToString("0.####");
            boneJointPlane.Text = boneRef.JointPlaneHalfCone.ToString("0.####");
            boneJointTwistMin.Text = boneRef.JointTwistMin.ToString("0.####");
            boneJointTwistMax.Text = boneRef.JointTwistMax.ToString("0.####");
            bonePanel.IsEnabled = true;
            ModelEditorData.ViewingBone = boneRef.Index;
            RefreshEyeSettings();
        };
        attachmentParent.SelectionChanged += (sender, e) =>
        {
            if (attachFieldsUpdating || ModelEditorData.ViewingAttachment == -1) return;
            ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment].BoneID = attachmentParent.SelectedIndex;
        };
    }
    public void RefreshIcons()
    {
        boneSectionIcon.Source = iconAtlas.Get(BoneIconCoord.col, BoneIconCoord.row);
        eyeSectionIcon.Source = iconAtlas.Get(EyeIconCoord.col, EyeIconCoord.row);
        addEyeIcon.Source = iconAtlas.Get(AddEyeIconCoord.col, AddEyeIconCoord.row);
    }

    public void RefreshLodLevelsList()
    {
        if (ModelEditorData.ActiveModel == null) return;
        lodLevelsBox.Items.Clear();
        for (int i = 0; i < ModelEditorData.ActiveModel.LODLevels.Count; i++)
            lodLevelsBox.Items.Add($"LOD {i + 1}");

        selectedLodLevelIndex = -1;
        lodLevelDistanceBox.IsEnabled = false;
        lodLevelDistanceBox.Text = "";
    }

    public void RefreshEyes()
    {
        if (ModelEditorData.ActiveModel?.EyeDefs == null) return;
        eyeViewer.Items.Clear();

        for (int i = 0; i < ModelEditorData.ActiveModel.EyeDefs.Count; i++)
        {
            var eyeDef = ModelEditorData.ActiveModel.EyeDefs[i];
            int captured = i;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            header.Children.Add(new Image { Width = 14, Height = 14, Source = eyeSectionIcon.Source });
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(eyeDef.Name) ? $"eye_{i}" : eyeDef.Name,
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new TreeViewItem { Header = header, FontSize = 12 };
            item.Tapped += (a, b) =>
            {
                ModelEditorData.ViewingEye = captured;
                ModelEditorData.ViewingBone = -1;
                boneViewer.SelectedItem = null;
                RefreshEyeSettings();
            };
            eyeViewer.Items.Add(item);
        }
    }

    public void RefreshEyeSettings()
    {
        bonePanelBorder.IsVisible = ModelEditorData.ViewingEye < 0;
        eyePanelBorder.IsVisible = ModelEditorData.ViewingEye >= 0;

        if (ModelEditorData.ViewingEye < 0) return;
        var eyeDef = ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye];

        eyeFieldsUpdating = true;

        eyeName.Text = eyeDef.Name ?? "";
        eyeRadius.Text = eyeDef.EyeRadius.ToString("0.####");

        eyeHeadBone.Items.Clear();
        foreach (var bone in ModelEditorData.ActiveModel.Bones)
            eyeHeadBone.Items.Add(new ComboBoxItem { Content = bone.Name });
        eyeHeadBone.SelectedIndex = eyeDef.HeadBoneID;

        eyeFieldsUpdating = false;
    }

    public void OnEyeSetupComplete()
    {
        lblEyeSetupHint.Text = "";
        RefreshEyeSettings();
    }

    private void EyeName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeFieldsUpdating || ModelEditorData.ViewingEye < 0) return;
        ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye].Name = eyeName.Text;
        int sel = ModelEditorData.ViewingEye;
        RefreshEyes();
        ModelEditorData.ViewingEye = sel;
    }

    private void EyeHeadBone_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (eyeFieldsUpdating || ModelEditorData.ViewingEye < 0) return;
        ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye].HeadBoneID = eyeHeadBone.SelectedIndex;
    }

    private void EyeRadius_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeFieldsUpdating || ModelEditorData.ViewingEye < 0) return;
        if (!float.TryParse(eyeRadius.Text, out float r)) return;
        ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye].EyeRadius = r;
        ModelView.Instance.RecomputeEyeOffset(ModelEditorData.ViewingEye);
    }

    private async void AddEye_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new BoneParentPicker(ModelEditorData.ActiveModel.Bones);
        int? boneID = await picker.ShowDialog<int?>(MainWindow.Instance);
        if (boneID == null) return;

        ModelEditorData.ActiveModel.EyeDefs ??= new List<CModel.CEyeDef>();
        var eyeDef = new CModel.CEyeDef
        {
            Name = $"eye_{ModelEditorData.ActiveModel.EyeDefs.Count}",
            HeadBoneID = boneID.Value,
            HeadOffsetMatrix = Microsoft.Xna.Framework.Matrix.Identity,
            EyeMatrix = Microsoft.Xna.Framework.Matrix.Identity,
            EyeRadius = 0.05f
        };
        ModelEditorData.ActiveModel.EyeDefs.Add(eyeDef);
        ModelEditorData.ViewingEye = ModelEditorData.ActiveModel.EyeDefs.Count - 1;
        ModelEditorData.ViewingBone = -1;
        RefreshEyes();
        RefreshEyeSettings();
        ModelView.Instance.EnterEyeSetupMode();
        lblEyeSetupHint.Text = "Click the center vertex, then the edge vertex.";
    }

    private async void BtnEyeSetup_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingEye < 0) return;
        var eyeDef = ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye];

        var picker = new BoneParentPicker(ModelEditorData.ActiveModel.Bones, eyeDef.HeadBoneID);
        int? boneID = await picker.ShowDialog<int?>(MainWindow.Instance);
        if (boneID == null) return;

        eyeDef.HeadBoneID = boneID.Value;
        RefreshEyeSettings();

        ModelView.Instance.EnterEyeSetupMode();
        lblEyeSetupHint.Text = "Click the center vertex, then the edge vertex.";
    }

    private void BtnEyeMirror_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingEye < 0) return;
        ModelView.Instance.MirrorEye(ModelEditorData.ViewingEye);
        RefreshEyes();
    }

    private void BtnEyeDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingEye < 0) return;
        ModelEditorData.ActiveModel.EyeDefs.RemoveAt(ModelEditorData.ViewingEye);
        ModelEditorData.ViewingEye = -1;
        RefreshEyes();
        RefreshEyeSettings();
    }

    private static Avalonia.Media.Color ToAvaloniaColor(Microsoft.Xna.Framework.Color c) => new Avalonia.Media.Color(c.A, c.R, c.G, c.B);
    private static Microsoft.Xna.Framework.Color ToXnaColor(Avalonia.Media.Color c) => new Microsoft.Xna.Framework.Color(c.R, c.G, c.B, c.A);

    public void RefreshEyeTexturePanel()
    {
        if (ModelEditorData.ActiveModel == null) return;
        var recipe = ModelEditorData.ActiveModel.EyeTexture;

        eyeTexFieldsUpdating = true;

        eyeTexSeed.Text = recipe.Seed.ToString();
        eyeTexScleraColor.Color = ToAvaloniaColor(recipe.ScleraColor);
        eyeTexVesselColor.Color = ToAvaloniaColor(recipe.VesselColor);
        eyeTexIrisColor.Color = ToAvaloniaColor(recipe.IrisColor);
        eyeTexIrisRimColor.Color = ToAvaloniaColor(recipe.IrisRimColor);
        eyeTexIrisSize.Text = recipe.IrisSize.ToString("0.###");
        eyeTexPupilColor.Color = ToAvaloniaColor(recipe.PupilColor);
        eyeTexPupilBaseSize.Text = recipe.PupilBaseSize.ToString("0.###");
        eyeTexPupilFeather.Text = recipe.PupilFeather.ToString("0.###");
        eyeTexFiberDensity.Text = recipe.FiberDensity.ToString("0.###");
        eyeTexVeinDensity.Text = recipe.VeinDensity.ToString("0.###");
        eyeTexVeinDistortion.Text = recipe.VeinDistortion.ToString("0.###");
        eyeTexIrisFleckColor.Color = ToAvaloniaColor(recipe.IrisFleckColor);
        eyeTexIrisFleckAmount.Text = recipe.IrisFleckAmount.ToString("0.###");
        eyeTexNormalStrength.Text = recipe.NormalStrength.ToString("0.###");
        eyeTexCorneaBulge.Text = recipe.CorneaBulgeStrength.ToString("0.###");
        eyeTexScleraSpecular.Text = recipe.ScleraSpecular.ToString("0.###");
        eyeTexIrisSpecular.Text = recipe.IrisSpecular.ToString("0.###");

        eyeTexFieldsUpdating = false;
    }

    private void EyeTexSeed_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!int.TryParse(eyeTexSeed.Text, out int v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.Seed = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexScleraColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.ScleraColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexVesselColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.VesselColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisRimColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisRimColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisSize_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexIrisSize.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisSize = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexPupilColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.PupilColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexPupilBaseSize_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexPupilBaseSize.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.PupilBaseSize = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexPupilFeather_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexPupilFeather.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.PupilFeather = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexFiberDensity_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexFiberDensity.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.FiberDensity = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexVeinDensity_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexVeinDensity.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.VeinDensity = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexVeinDistortion_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexVeinDistortion.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.VeinDistortion = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisFleckColor_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisFleckColor = ToXnaColor(e.NewColor);
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisFleckAmount_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexIrisFleckAmount.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisFleckAmount = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexNormalStrength_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexNormalStrength.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.NormalStrength = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexCorneaBulge_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexCorneaBulge.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.CorneaBulgeStrength = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexScleraSpecular_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexScleraSpecular.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.ScleraSpecular = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void EyeTexIrisSpecular_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (eyeTexFieldsUpdating || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(eyeTexIrisSpecular.Text, out float v)) return;
        var r = ModelEditorData.ActiveModel.EyeTexture;
        r.IrisSpecular = v;
        ModelEditorData.ActiveModel.EyeTexture = r;
        ModelView.Instance?.RegenerateEyeTexture();
    }

    private void BtnRegenerateEyeTexture_Click(object? sender, RoutedEventArgs e)
    {
        ModelView.Instance?.RegenerateEyeTexture();
    }
    private void FaceMe_Click(object? sender, RoutedEventArgs e)
    {
        ModelView.Instance?.EyesFaceCamera(((ToggleButton)btnFaceCam).IsChecked ?? false);
    }

    private void LodLevelsBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        selectedLodLevelIndex = lodLevelsBox.SelectedIndex;
        if (selectedLodLevelIndex < 0 || ModelEditorData.ActiveModel == null)
        {
            lodLevelDistanceBox.IsEnabled = false;
            return;
        }

        lodFieldsUpdating = true;
        lodLevelDistanceBox.IsEnabled = true;
        lodLevelDistanceBox.Text = ModelEditorData.ActiveModel.LODLevels[selectedLodLevelIndex].Distance.ToString("0.#");
        lodFieldsUpdating = false;
    }

    private void AddLodLevel_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null) return;
        var levels = ModelEditorData.ActiveModel.LODLevels;
        float dist = levels.Count > 0 ? levels[^1].Distance + 25f : 25f;
        levels.Add(new CLODLevel { Distance = dist });

        RefreshLodLevelsList();
        RefreshBodygroupLodPanel();
    }

    private void RemoveLodLevel_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null) return;
        var levels = ModelEditorData.ActiveModel.LODLevels;
        if (levels.Count == 0) return;

        // Only the last level can be removed
        int lastIndex = levels.Count - 1;
        levels.RemoveAt(lastIndex);

        foreach (var bg in ModelEditorData.ActiveModel.Bodygroups)
        {
            if (bg.LODMeshes.Count > lastIndex)
                bg.LODMeshes.RemoveAt(lastIndex);
        }

        RefreshLodLevelsList();
        RefreshBodygroupLodPanel();
    }

    private void LodLevelDistance_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (lodFieldsUpdating || selectedLodLevelIndex < 0 || ModelEditorData.ActiveModel == null) return;
        if (!float.TryParse(lodLevelDistanceBox.Text, out float v)) return;
        ModelEditorData.ActiveModel.LODLevels[selectedLodLevelIndex].Distance = v;
    }

    public void RefreshBodygroupLodPanel()
    {
        bodygroupLodStack.Children.Clear();

        var model = ModelEditorData.ActiveModel;
        if (model == null || activeBodygroup == null) return;

        var bg = model.Bodygroups[activeBodygroup.ID];

        if (model.LODLevels.Count == 0)
        {
            bodygroupLodStack.Children.Add(new TextBlock
            {
                Text = "(no LOD levels defined for this model)",
                FontSize = 10,
                Opacity = 0.6,
                Foreground = Brushes.White
            });
            return;
        }

        for (int level = 0; level < model.LODLevels.Count; level++)
        {
            int capturedLevel = level;
            var entry = level < bg.LODMeshes.Count ? bg.LODMeshes[level] : null;

            var row = new Grid { Margin = new Thickness(0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition(70, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            var label = new TextBlock
            {
                Text = $"LOD {level + 1}:",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };

            var hiddenCheck = new CheckBox
            {
                Content = "Hide",
                FontSize = 10,
                IsChecked = entry?.Hidden ?? false,
                VerticalAlignment = VerticalAlignment.Center
            };
            hiddenCheck.IsCheckedChanged += (s, e) =>
            {
                var curBg = model.Bodygroups[activeBodygroup.ID];
                while (curBg.LODMeshes.Count <= capturedLevel) curBg.LODMeshes.Add(null);
                curBg.LODMeshes[capturedLevel] ??= new CLODMeshEntry();
                curBg.LODMeshes[capturedLevel].Hidden = hiddenCheck.IsChecked ?? false;
            };

            string statusText = entry == null
                ? "(inherits previous LOD)"
                : entry.Hidden
                    ? "(hidden)"
                    : entry.Mesh != null
                        ? entry.SourceMeshName ?? "(mesh assigned, no data)"
                        : "(no mesh, inherits previous)";

            var status = new TextBlock
            {
                Text = statusText,
                FontSize = 10,
                Opacity = 0.7,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(hiddenCheck, 1);
            Grid.SetColumn(status, 2);
            row.Children.Add(label);
            row.Children.Add(hiddenCheck);
            row.Children.Add(status);

            bodygroupLodStack.Children.Add(row);
        }
    }

    public void RefreshIkChainList()
    {
        if (ModelEditorData.ActiveModel == null) return;
        ikChainListPanel.IsEnabled = true;
        ikChainList.Items.Clear();
        foreach (var chain in ModelEditorData.ActiveModel.IKChains)
        {
            ikChainList.Items.Add(chain.ChainName);
        }

        selectedIkChainIndex = -1;
        ikChainEditPanel.IsEnabled = false;
    }

    private void IkChainList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressIkChainSelectionChanged) return;

        selectedIkChainIndex = ikChainList.SelectedIndex;
        if (selectedIkChainIndex < 0)
        {
            ikChainEditPanel.IsEnabled = false;
            return;
        }

        var chain = ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex];
        ikFieldsUpdating = true;
        ikChainEditPanel.IsEnabled = true;
        ikChainName.Text = chain.ChainName;
        ikChainEndBoneBtn.Content = string.IsNullOrEmpty(chain.EndBoneName) ? "(none)" : chain.EndBoneName;
        ikChainRoleBox.SelectedIndex = (int)chain.Role;

        ikChainOffsetX.Text = chain.EndEffectorOffset.X.ToString("0.###");
        ikChainOffsetY.Text = chain.EndEffectorOffset.Y.ToString("0.###");
        ikChainOffsetZ.Text = chain.EndEffectorOffset.Z.ToString("0.###");

        ikChainOrientPitch.Text = chain.LocalOrientationOffsetEuler.X.ToString("0.##");
        ikChainOrientYaw.Text = chain.LocalOrientationOffsetEuler.Y.ToString("0.##");
        ikChainOrientRoll.Text = chain.LocalOrientationOffsetEuler.Z.ToString("0.##");

        ikFieldsUpdating = false;
    }

    private void IkChainOrientation_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ikFieldsUpdating || selectedIkChainIndex < 0) return;
        if (!float.TryParse(ikChainOrientPitch.Text, out float pitch)) pitch = 0f;
        if (!float.TryParse(ikChainOrientYaw.Text, out float yaw)) yaw = 0f;
        if (!float.TryParse(ikChainOrientRoll.Text, out float roll)) roll = 0f;

        var chain = ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex];
        chain.LocalOrientationOffsetEuler = new Vector3(pitch, yaw, roll);
        chain.LocalOrientationOffset = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(yaw),
            MathHelper.ToRadians(pitch),
            MathHelper.ToRadians(roll));
    }

    private void IkChainOffset_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ikFieldsUpdating || selectedIkChainIndex < 0) return;
        if (!float.TryParse(ikChainOffsetX.Text, out float x)) x = 0f;
        if (!float.TryParse(ikChainOffsetY.Text, out float y)) y = 0f;
        if (!float.TryParse(ikChainOffsetZ.Text, out float z)) z = 0f;
        ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex].EndEffectorOffset = new Vector3(x, y, z);
    }
    private void AddIkChain_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null) return;
        ModelEditorData.ActiveModel.IKChains.Add(new CModel.CIKChain { ChainName = "new_chain" });
        RefreshIkChainList();
        RefreshIkEventChainOptions();
    }

    private void DeleteIkChain_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedIkChainIndex < 0) return;
        ModelEditorData.ActiveModel.IKChains.RemoveAt(selectedIkChainIndex);
        RefreshIkChainList();
        RefreshIkEventChainOptions();
    }

    private void IkChainName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ikFieldsUpdating || selectedIkChainIndex < 0) return;
        ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex].ChainName = ikChainName.Text;

        suppressIkChainSelectionChanged = true;
        ikChainList.Items[selectedIkChainIndex] = ikChainName.Text;
        ikChainList.SelectedIndex = selectedIkChainIndex;
        suppressIkChainSelectionChanged = false;

        RefreshIkEventChainOptions();
    }

    private async void IkChainEndBone_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedIkChainIndex < 0) return;
        var picker = new BoneParentPicker(ModelEditorData.ActiveModel.Bones);
        int? boneID = await picker.ShowDialog<int?>(MainWindow.Instance);
        if (boneID == null) return;

        var chain = ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex];
        chain.EndBoneName = ModelEditorData.ActiveModel.Bones[boneID.Value].Name;
        ikChainEndBoneBtn.Content = chain.EndBoneName;
        ModelEditorData.ActiveModel.ResolveIKChains();
    }
    private void IkChainRole_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ikFieldsUpdating || selectedIkChainIndex < 0) return;
        ModelEditorData.ActiveModel.IKChains[selectedIkChainIndex].Role = (CModel.CIKChainRole)ikChainRoleBox.SelectedIndex;
    }

    public void RefreshIkEventList()
    {
        var animDef = GetSelectedAnimDef();
        ikEventList.Items.Clear();

        if (animDef?.IKEvents == null) return;

        foreach (var ev in animDef.IKEvents)
        {
            string free = ev.FreeFrame < 0 ? "end" : ev.FreeFrame.ToString();
            ikEventList.Items.Add($"{ev.ChainName}  [{ev.LockFrame} \u2192 {free}]");
        }

        RefreshIkEventChainOptions();

        int preserve = selectedIkEventIndex;
        selectedIkEventIndex = -1;
        if (preserve >= 0 && preserve < ikEventList.Items.Count)
        {
            ikEventList.SelectedIndex = preserve;
        }
        else
        {
            RefreshIkEventProps();
        }
    }

    private void RefreshIkEventChainOptions()
    {
        ikEventChainBox.Items.Clear();
        if (ModelEditorData.ActiveModel?.IKChains == null) return;
        foreach (var chain in ModelEditorData.ActiveModel.IKChains)
        {
            ikEventChainBox.Items.Add(chain.ChainName);
        }
    }
    private void RefreshIkEventLabelOnly()
    {
        var animDef = GetSelectedAnimDef();
        if (animDef?.IKEvents == null || selectedIkEventIndex < 0 || selectedIkEventIndex >= animDef.IKEvents.Count) return;

        var ev = animDef.IKEvents[selectedIkEventIndex];
        string free = ev.FreeFrame < 0 ? "end" : ev.FreeFrame.ToString();

        suppressIkEventSelectionChanged = true;
        ikEventList.Items[selectedIkEventIndex] = $"{ev.ChainName}  [{ev.LockFrame} \u2192 {free}]";
        ikEventList.SelectedIndex = selectedIkEventIndex;
        suppressIkEventSelectionChanged = false;

        RedrawTimeline();
    }

    private CIKAnimEvent GetSelectedIkEvent()
    {
        var animDef = GetSelectedAnimDef();
        if (animDef?.IKEvents == null || selectedIkEventIndex < 0 || selectedIkEventIndex >= animDef.IKEvents.Count) return null;
        return animDef.IKEvents[selectedIkEventIndex];
    }

    private void IkEventList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressIkEventSelectionChanged) return;
        selectedIkEventIndex = ikEventList.SelectedIndex;
        timelineRenderer.SelectIKEvent(selectedIkEventIndex);
        RefreshIkEventProps();
        RedrawTimeline();
    }

    private void RefreshIkEventProps()
    {
        var ev = GetSelectedIkEvent();
        ikEventPropsPanel.Bind(ev);

        ikFieldsUpdating = true;
        ikEventChainBox.SelectedItem = ev?.ChainName;

        ikEventTargetRef.Items.Clear();
        if (ModelEditorData.ActiveModel != null)
        {
            foreach (var att in ModelEditorData.ActiveModel.AttachmentPoints)
            {
                ikEventTargetRef.Items.Add(att.Name);
            }
            foreach (var bone in ModelEditorData.ActiveModel.Bones)
            {
                ikEventTargetRef.Items.Add(bone.Name);
            }
        }
        ikEventTargetRef.Text = ev?.TargetReference ?? "";
        ikFieldsUpdating = false;
    }

    private void AddIkEvent_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null) return;

        animDef.IKEvents ??= new();
        animDef.IKEvents.Add(new CIKAnimEvent
        {
            ChainName = ModelEditorData.ActiveModel.IKChains.FirstOrDefault()?.ChainName ?? "",
            LockFrame = ModelEditorData.SequencePlayer?.CurrentWholeTick ?? 0
        });

        selectedIkEventIndex = animDef.IKEvents.Count - 1;
        RefreshIkEventList();
        ikEventList.SelectedIndex = selectedIkEventIndex;
    }

    private void DeleteIkEvent_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef?.IKEvents == null || selectedIkEventIndex < 0) return;
        animDef.IKEvents.RemoveAt(selectedIkEventIndex);
        selectedIkEventIndex = -1;
        timelineRenderer.SelectIKEvent(-1);
        RefreshIkEventList();
        RedrawTimeline();
    }

    private void IkEventChain_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ikFieldsUpdating) return;
        var ev = GetSelectedIkEvent();
        if (ev == null || ikEventChainBox.SelectedItem is not string name) return;
        ev.ChainName = name;
        RefreshIkEventLabelOnly();
    }

    private void IkEventTargetRef_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ikFieldsUpdating) return;
        var ev = GetSelectedIkEvent();
        if (ev == null) return;
        ev.TargetReference = ikEventTargetRef.SelectedItem as string ?? ikEventTargetRef.Text;
    }

    private void IkEventTargetRef_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (ikFieldsUpdating) return;
        var ev = GetSelectedIkEvent();
        if (ev == null) return;
        ev.TargetReference = ikEventTargetRef.Text;
    }

    public void ToggleChoreoDirector() => SetChoreoActive(!choreoActive);

    private void SetChoreoActive(bool active)
    {
        choreoActive = active;

        gameControl.IsVisible = !active;
        choreoDirector.IsVisible = active;

        boneTreeBorder.IsEnabled = !active;
        rightPanelBorder.IsEnabled = !active;
        bottomTabsBorder.IsEnabled = !active;

        if (active)
            choreoDirector.NotifyShown();
    }

    public void Clear()
    {
        activeBodygroup = null;
        activeAnimDef = null;
        activeBlendSequence = null;
        RefreshBlendSequenceEditor();
        ModelEditorData.MorphState = new CMorphState();

        layerPreviewAnimator = null;
        sequenceEditAnimator = null;
        ModelEditorData.PreviewAnimator = null;
    }

    public void RefreshAll()
    {
        if (ModelEditorData.ActiveModel == null) return;

        viewTabs.IsEnabled = true;
        boneViewer.IsEnabled = true;

        RefreshIcons();
        RefreshBones();
        RefreshEyes();
        RefreshEyeTexturePanel();
        RefreshBodygroups();
        RefreshAnimationList();
        RefreshBlendSequenceList();
        RefreshBlendSequenceEditor();
        RefreshAttachmentPanel();
        RefreshLodLevelsList(); 
        RefreshBodygroupLodPanel();
        RefreshIkChainList();

        SetupMorphSliders();

        ModelView.Instance?.RegenerateEyeTexture();
    }

    public void RefreshJointFields()
    {
        if (ModelEditorData.ViewingBone == -1) return;
        var bone = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        boneJointNormal.Text = bone.JointNormalHalfCone.ToString("0.####");
        boneJointPlane.Text = bone.JointPlaneHalfCone.ToString("0.####");
        boneJointTwistMin.Text = bone.JointTwistMin.ToString("0.####");
        boneJointTwistMax.Text = bone.JointTwistMax.ToString("0.####");
    }

    public void RefreshAttachmentFields()
    {
        if (ModelEditorData.ViewingAttachment == -1) return;
        attachFieldsUpdating = true;
        var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
        att.Offset.Decompose(out _, out Quaternion rot, out Vector3 pos);
        attachPosX.Text = pos.X.ToString("0.####");
        attachPosY.Text = pos.Y.ToString("0.####");
        attachPosZ.Text = pos.Z.ToString("0.####");
        var euler = QuaternionToEulerDegrees(rot);
        attachRotX.Text = euler.X.ToString("0.##");
        attachRotY.Text = euler.Y.ToString("0.##");
        attachRotZ.Text = euler.Z.ToString("0.##");
        attachFieldsUpdating = false;
    }

    public void RedrawTimeline()
    {
        if (viewTabs.SelectedItem is not TabItem tab || tab.Header?.ToString() != "Animations") return;

        var animDef = GetSelectedAnimDef();
        int frame = (int)(ModelEditorData.SequencePlayer?.CurrentWholeTick ?? 0);
        timelineRenderer.Redraw(viewAnimationTimeline, viewAnimationRuler, animDef, frame);

        if (animDef != null)
            lblAnimFrame.Text = $"{frame} / {animDef.Animation?.DurationInTicks ?? 0}";
    }

    public void RefreshBones()
    {
        if (ModelEditorData.ActiveModel?.Bones == null) return;

        Dictionary<string, TreeViewItem> bones = new();
        boneViewer.Items.Clear();

        foreach (var bone in ModelEditorData.ActiveModel.Bones)
        {
            var item = new TreeViewItem
            {
                Header = bone.Name,
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.Light,
                Tag = bone
            };

            if (!bone.HasParent || !bones.TryGetValue(bone.Parent.Name, out var parentItem))
            {
                boneViewer.Items.Add(item);
            }
            else
            {
                parentItem.Items.Add(item);
            }
            bones[bone.Name] = item;
        }

        bonePhysicsParent.Items.Clear();
        bonePhysicsParent.Items.Add(new ComboBoxItem { Content = "Default" });
        foreach (var bone in ModelEditorData.ActiveModel.Bones)
            bonePhysicsParent.Items.Add(new ComboBoxItem { Content = bone.Name });

        bonePhysicsParent.SelectionChanged += (s, e) =>
        {
            if (ModelEditorData.ViewingBone == -1) return;
            int sel = bonePhysicsParent.SelectedIndex;
            var bdata = ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone];
            bdata.PhysicsParentOverrideIndex = sel <= 0 ? null : sel - 1;
            ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone] = bdata;
            ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone].PhysicsParentOverride =
                sel <= 0 ? null : ModelEditorData.ActiveModel.Bones[sel - 1];
        };
    }

    private void BoneBoundsCenter_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        var split = boneBoundsCenter.Text?.Split(',');
        if (split?.Length != 3) return;
        if (!float.TryParse(split[0], out float x) ||
            !float.TryParse(split[1], out float y) ||
            !float.TryParse(split[2], out float z)) return;
        var bd = ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone];
        bd.BoundsCenter = new Vector3(x, y, z);
        ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone] = bd;
        ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone].BoundsCenter = bd.BoundsCenter;
    }

    private void BoneBoundsSize_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        var split = boneBoundsSize.Text?.Split(',');
        if (split?.Length != 3) return;
        if (!float.TryParse(split[0], out float x) ||
            !float.TryParse(split[1], out float y) ||
            !float.TryParse(split[2], out float z)) return;
        var bd = ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone];
        bd.BoundsSize = new Vector3(x, y, z);
        ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone] = bd;
        ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone].BoundsSize = bd.BoundsSize;
    }

    private void BoneJointNormal_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        if (!float.TryParse(boneJointNormal.Text, out float v)) return;
        ApplyJointValue(ModelEditorData.ViewingBone, normal: v);
    }

    private void BoneJointPlane_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        if (!float.TryParse(boneJointPlane.Text, out float v)) return;
        ApplyJointValue(ModelEditorData.ViewingBone, plane: v);
    }

    private void BoneJointTwistMin_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        if (!float.TryParse(boneJointTwistMin.Text, out float v)) return;
        ApplyJointValue(ModelEditorData.ViewingBone, twistMin: v);
    }

    private void BoneJointTwistMax_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ViewingBone == -1) return;
        if (!float.TryParse(boneJointTwistMax.Text, out float v)) return;
        ApplyJointValue(ModelEditorData.ViewingBone, twistMax: v);
    }

    private float? clipNormal, clipPlane, clipTwistMin, clipTwistMax;

    private void BtnCopyJoint_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingBone < 0) return;
        var bone = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        clipNormal = bone.JointNormalHalfCone;
        clipPlane = bone.JointPlaneHalfCone;
        clipTwistMin = bone.JointTwistMin;
        clipTwistMax = bone.JointTwistMax;
        btnPasteJoint.IsEnabled = true;
    }

    private void BtnPasteJoint_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingBone < 0 || clipNormal == null) return;
        ApplyJointValues(ModelEditorData.ViewingBone, clipNormal.Value, clipPlane!.Value, clipTwistMin!.Value, clipTwistMax!.Value);
        RefreshJointFields();
    }

    private void BtnMirrorJoint_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingBone < 0) { lblMirrorStatus.Text = "No bone selected."; return; }
        var source = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        int target = FindSymmetricBone(source.Name);
        if (target < 0) { lblMirrorStatus.Text = $"No symmetric bone found for '{source.Name}'."; return; }
        ApplyJointValues(target, source.JointNormalHalfCone, source.JointPlaneHalfCone, source.JointTwistMin, source.JointTwistMax);
        lblMirrorStatus.Text = $"Mirrored → {ModelEditorData.ActiveModel.Bones[target].Name}";
    }

    private void ApplyJointValues(int idx, float normal, float plane, float twistMin, float twistMax)
    {
        var bd = ModelEditorData.ActiveModel.BoneData[idx];
        bd.JointNormalHalfCone = normal;
        bd.JointPlaneHalfCone = plane;
        bd.JointTwistMin = twistMin;
        bd.JointTwistMax = twistMax;
        ModelEditorData.ActiveModel.BoneData[idx] = bd;
        var b = ModelEditorData.ActiveModel.Bones[idx];
        b.JointNormalHalfCone = normal;
        b.JointPlaneHalfCone = plane;
        b.JointTwistMin = twistMin;
        b.JointTwistMax = twistMax;
    }

    private void ApplyJointValue(int idx,
        float? normal = null, float? plane = null,
        float? twistMin = null, float? twistMax = null)
    {
        var bd = ModelEditorData.ActiveModel.BoneData[idx];
        var b = ModelEditorData.ActiveModel.Bones[idx];
        if (normal != null) { bd.JointNormalHalfCone = normal.Value; b.JointNormalHalfCone = normal.Value; }
        if (plane != null) { bd.JointPlaneHalfCone = plane.Value; b.JointPlaneHalfCone = plane.Value; }
        if (twistMin != null) { bd.JointTwistMin = twistMin.Value; b.JointTwistMin = twistMin.Value; }
        if (twistMax != null) { bd.JointTwistMax = twistMax.Value; b.JointTwistMax = twistMax.Value; }
        ModelEditorData.ActiveModel.BoneData[idx] = bd;
    }

    private static int FindSymmetricBone(string name)
    {
        var pairs = new (string a, string b)[]
        {
            ("_Left", "_Right"), ("_left", "_right"),
            ("_L", "_R"), ("_l", "_r"),
            (".L", ".R"), (".l", ".r"),
            ("-L", "-R"), ("-l", "-r"),
        };

        foreach (var (a, b) in pairs)
        {
            if (name.EndsWith(a, StringComparison.Ordinal))
            {
                var candidate = name[..^a.Length] + b;
                var match = ModelEditorData.ActiveModel.Bones
                    .FirstOrDefault(x => x.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Index;
            }
            else if (name.EndsWith(b, StringComparison.Ordinal))
            {
                var candidate = name[..^b.Length] + a;
                var match = ModelEditorData.ActiveModel.Bones
                    .FirstOrDefault(x => x.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Index;
            }
        }
        return -1;
    }

    private bool attachFieldsUpdating = false;

    public void RefreshAttachmentPanel()
    {
        if (ModelEditorData.ActiveModel == null) return;
        attachmentAddPanel.IsEnabled = true;
        attachmentEditPanel.IsEnabled = true;
        attachmentList.Items.Clear();

        int id = 0;
        foreach (var att in ModelEditorData.ActiveModel.AttachmentPoints)
        {
            var captured = att;
            var item = new TreeViewItem
            {
                Header = string.IsNullOrWhiteSpace(att.Name) ? $"attachment_{id}" : att.Name,
                FontSize = 12,
            };
            item.Tapped += (a, b) =>
            {
                ModelEditorData.ViewingAttachment = ModelEditorData.ActiveModel.AttachmentPoints.FindIndex(t => t == captured);
                RefreshAttachmentSettings();
            };
            attachmentList.Items.Add(item);
            id++;
        }
    }

    private void RefreshAttachmentSettings()
    {
        if (ModelEditorData.ViewingAttachment == -1) return;
        var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];

        attachFieldsUpdating = true;

        attachmentName.Text = att.Name ?? "";
        att.Offset.Decompose(out _, out Quaternion rot, out Vector3 pos);
        attachPosX.Text = pos.X.ToString("0.####");
        attachPosY.Text = pos.Y.ToString("0.####");
        attachPosZ.Text = pos.Z.ToString("0.####");
        var euler = QuaternionToEulerDegrees(rot);
        attachRotX.Text = euler.X.ToString("0.##");
        attachRotY.Text = euler.Y.ToString("0.##");
        attachRotZ.Text = euler.Z.ToString("0.##");

        attachmentParent.Items.Clear();
        foreach (var bone in ModelEditorData.ActiveModel.Bones)
        {
            attachmentParent.Items.Add(new ComboBoxItem { Content = bone.Name });
        }
        attachmentParent.SelectedIndex = att.BoneID;

        attachFieldsUpdating = false;
    }

    private void AttachmentName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (attachFieldsUpdating || ModelEditorData.ViewingAttachment == -1) return;
        ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment].Name = attachmentName.Text;
        int sel = ModelEditorData.ViewingAttachment;
        RefreshAttachmentPanel();
        ModelEditorData.ViewingAttachment = sel;
    }

    private void AttachPos_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (attachFieldsUpdating || ModelEditorData.ViewingAttachment == -1) return;
        if (!float.TryParse(attachPosX.Text, out float x) ||
            !float.TryParse(attachPosY.Text, out float y) ||
            !float.TryParse(attachPosZ.Text, out float z)) return;
        RebuildAttachmentOffset(new Vector3(x, y, z), GetCurrentAttachRotation());
    }

    private void AttachRot_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (attachFieldsUpdating || ModelEditorData.ViewingAttachment == -1) return;
        if (!float.TryParse(attachPosX.Text, out float x) ||
            !float.TryParse(attachPosY.Text, out float y) ||
            !float.TryParse(attachPosZ.Text, out float z)) return;
        RebuildAttachmentOffset(new Vector3(x, y, z), GetCurrentAttachRotation());
    }

    private Quaternion GetCurrentAttachRotation()
    {
        if (!float.TryParse(attachRotX.Text, out float pitch)) pitch = 0;
        if (!float.TryParse(attachRotY.Text, out float yaw)) yaw = 0;
        if (!float.TryParse(attachRotZ.Text, out float roll)) roll = 0;
        return Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(yaw),
            MathHelper.ToRadians(pitch),
            MathHelper.ToRadians(roll));
    }

    private void RebuildAttachmentOffset(Vector3 position, Quaternion rotation)
    {
        var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
        att.Offset = Microsoft.Xna.Framework.Matrix.CreateFromQuaternion(rotation)
                   * Microsoft.Xna.Framework.Matrix.CreateTranslation(position);
    }

    private void AddAttachment_Click(object? sender, RoutedEventArgs e)
    {
        ModelEditorData.ActiveModel.AttachmentPoints.Add(new CAttachmentPoint());
        RefreshAttachmentPanel();
    }

    private void DeleteAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ViewingAttachment == -1) return;
        ModelEditorData.ActiveModel.AttachmentPoints.RemoveAt(ModelEditorData.ViewingAttachment);
        RefreshAttachmentPanel();
    }

    private async void BtnLoadPreview_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filters = { new FileDialogFilter { Name = "CCMDL", Extensions = { "ccmdl" } } } };
        var result = await dlg.ShowAsync(MainWindow.Instance);
        if (result?.Length > 0)
        {
            ModelView.Instance.LoadPreviewModel(result[0]);
            RebuildPreviewAttachmentPicker();
        }
    }

    private void BtnClearPreview_Click(object? sender, RoutedEventArgs e)
    {
        ModelView.Instance.ClearPreviewModel();
        previewAttachmentPicker.Items.Clear();
    }

    private void RebuildPreviewAttachmentPicker()
    {
        previewAttachmentPicker.Items.Clear();
        var preview = ModelView.Instance.PreviewModel;
        if (preview == null) return;

        previewAttachmentPicker.Items.Add(new ComboBoxItem { Content = "(model origin)" });
        foreach (var att in preview.AttachmentPoints)
            previewAttachmentPicker.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(att.Name) ? "(unnamed)" : att.Name
            });

        previewAttachmentPicker.SelectedIndex = 0;
        ModelView.Instance.PreviewAttachmentIndex = -1;
    }

    private void PreviewAttachmentPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ModelView.Instance.PreviewAttachmentIndex = previewAttachmentPicker.SelectedIndex - 1;
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion q)
    {
        float sinp = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinp) >= 1f
            ? MathF.CopySign(90f, sinp)
            : MathF.Asin(sinp) * (180f / MathF.PI);
        float siny = 2f * (q.W * q.Y + q.Z * q.X);
        float cosy = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float yaw = MathF.Atan2(siny, cosy) * (180f / MathF.PI);
        float sinr = 2f * (q.W * q.Z + q.X * q.Y);
        float cosr = 1f - 2f * (q.Z * q.Z + q.X * q.X);
        float roll = MathF.Atan2(sinr, cosr) * (180f / MathF.PI);
        return new Vector3(pitch, yaw, roll);
    }

    public void RefreshBodygroups()
    {
        if (ModelEditorData.ActiveModel == null) return;
        viewBodygroupBox.Items.Clear();
        int i = 0;
        foreach (var group in ModelEditorData.ActiveModel.Bodygroups)
            viewBodygroupBox.Items.Add(new BodygroupViewModel(i++));
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null || activeBodygroup == null) return;
        int matID = await MaterialPicker.PickAsync(MainWindow.Instance);
        if (matID > 0)
            ModelEditorData.ActiveModel.Bodygroups[activeBodygroup.ID].MaterialID = matID;
    }

    private void IsEye_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null || activeBodygroup == null) return;

        ModelEditorData.ActiveModel.Bodygroups[activeBodygroup.ID].IsEye = eyetoggle.IsChecked ?? false;

        if (!ModelEditorData.ActiveModel.Bodygroups[activeBodygroup.ID].IsSkinned)
        {
            ModelEditorData.ActiveModel.Bodygroups[activeBodygroup.ID].IsEye = false;
            eyetoggle.IsChecked = false;
        }
    }

    private void Bodygroup_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null || activeBodygroup == null) return;
        ModelEditorData.ActiveModel.Bodygroups[activeBodygroup.ID].Name = bodygroupName.Text ?? "empty";
        RefreshBodygroups();
    }

    private void BodygroupList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null ||
            viewBodygroupBox.SelectedItem is not BodygroupViewModel bg) return;
        bodygroupName.Text = bg.name;
        eyetoggle.IsChecked = bg.isEye;
        activeBodygroup = bg;
        RefreshBodygroupLodPanel();
    }

    private bool evtFieldsUpdating = false;
    private int selectedEventIndex = -1;

    public void RefreshAnimationList()
    {
        if (ModelEditorData.ActiveModel?.Animations == null) return;
        viewAnimationBox.Items.Clear();
        int i = 0;
        foreach (var anim in ModelEditorData.ActiveModel.Animations)
            viewAnimationBox.Items.Add(new AnimDefViewModel(i++));
    }

    private CAnimDef GetSelectedAnimDef()
    {
        if (activeAnimDef == null || ModelEditorData.ActiveModel?.Animations == null) return null;
        return activeAnimDef.ID < ModelEditorData.ActiveModel.Animations.Count
            ? ModelEditorData.ActiveModel.Animations[activeAnimDef.ID] : null;
    }

    private void AnimationList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null ||
            viewAnimationBox.SelectedItem is not AnimDefViewModel anim) return;

        activeAnimDef = anim;

        var s = GetSelectedAnimDef();
        bool hasAnim = s != null;
        btnAddEvent.IsEnabled = hasAnim;
        btnEditBoneMask.IsEnabled = hasAnim;

        if (hasAnim) ModelEditorData.SequencePlayer.AnimDef = s;

        animPropsPanel.Bind(s);
        animRootMotionPanel.Bind(s?.RootMotion);

        selectedEventIndex = -1;
        timelineRenderer.SelectEvent(-1);
        RefreshEventPanel();

        selectedIkEventIndex = -1;
        timelineRenderer.SelectIKEvent(-1);
        RefreshIkEventList();

        RedrawTimeline();

        RedrawTimeline();
    }

    private void BtnAnimPlay_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null || ModelEditorData.SequencePlayer == null) return;
        ModelEditorData.SequencePlayer.AnimDef = animDef;
        ModelEditorData.SequencePlayer.IsPlaying = true;
    }

    private void BtnAnimPause_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.SequencePlayer != null)
            ModelEditorData.SequencePlayer.IsPlaying = false;
    }

    private void BtnAnimStop_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.SequencePlayer == null) return;
        ModelEditorData.SequencePlayer.IsPlaying = false;
        ModelEditorData.SequencePlayer.CurrentTime = 0;
        RedrawTimeline();
    }

    private void BtnDeleteAnimation_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null) return;
        ModelEditorData.ActiveModel.Animations.Remove(animDef);
        if (ModelEditorData.SequencePlayer?.AnimDef == animDef)
            ModelEditorData.SequencePlayer = null;
        RefreshAnimationList();
        selectedEventIndex = -1;
        timelineRenderer.SelectEvent(-1);
        RefreshEventPanel();
        RedrawTimeline();
    }

    private async void BtnEditBoneMask_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null || ModelEditorData.ActiveModel?.Bones == null) return;
        animDef.BoneMask ??= new();

        var dialog = new BoneMaskEditor(animDef.BoneMask, ModelEditorData.ActiveModel.Bones);
        var result = await dialog.ShowDialog<bool>(MainWindow.Instance);
        if (result)
        {
            animDef.BoneMask = dialog.ResultMask;
        }
    }

    private void ScrubTo(int frame)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null || ModelEditorData.SequencePlayer == null) return;
        ModelEditorData.SequencePlayer.IsPlaying = false;
        ModelEditorData.SequencePlayer.CurrentTick = frame;
        ModelEditorData.SequencePlayer.ForceUpdateTransforms();

        if (animDef.Events != null)
        {
            int hit = animDef.Events.FindIndex(ev => ev.Frame == frame);
            if (hit >= 0 && hit != selectedEventIndex)
            {
                selectedEventIndex = hit;
                timelineRenderer.SelectEvent(hit);
                RefreshEventPanel();
            }
        }
        RedrawTimeline();
    }

    private void OnTimelineEventSelected(int index)
    {
        selectedEventIndex = index;
        RefreshEventPanel();
    }
    private void OnTimelineIKEventSelected(int index)
    {
        ikEventList.SelectedIndex = index;
    }

    private void BtnAddEvent_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef == null) return;
        animDef.Events ??= new();
        animDef.Events.Add(new CAnimEvent { Frame = ModelEditorData.SequencePlayer?.CurrentWholeTick ?? 0, Type = AnimEventType.Attach, Slot = "" });
        selectedEventIndex = animDef.Events.Count - 1;
        timelineRenderer.SelectEvent(selectedEventIndex);
        RefreshEventPanel();
        RedrawTimeline();
    }

    private void BtnDeleteEvent_Click(object? sender, RoutedEventArgs e)
    {
        var animDef = GetSelectedAnimDef();
        if (animDef?.Events == null || selectedEventIndex < 0) return;
        animDef.Events.RemoveAt(selectedEventIndex);
        selectedEventIndex = -1;
        timelineRenderer.SelectEvent(-1);
        RefreshEventPanel();
        RedrawTimeline();
    }

    private void RefreshEventPanel()
    {
        var animDef = GetSelectedAnimDef();
        var ev = animDef?.Events != null && selectedEventIndex >= 0 && selectedEventIndex < animDef.Events.Count
            ? animDef.Events[selectedEventIndex] : null;

        eventPropertiesPanel.IsEnabled = ev != null;
        eventPropsPanel.Bind(ev);

        if (!eventTypeHooked)
        {
            HookEventTypeChange();
            eventTypeHooked = true;
        }

        evtAttachFields.IsVisible = false;
        if (ev == null) return;

        evtFieldsUpdating = true;
        evtAttachment.Text = ev.Attachment ?? "";

        evtAttachment.Items.Clear();
        if (ModelEditorData.ActiveModel?.AttachmentPoints != null)
            foreach (var att in ModelEditorData.ActiveModel.AttachmentPoints)
                evtAttachment.Items.Add(att.Name ?? "(unnamed)");
        if (!string.IsNullOrEmpty(ev.Attachment))
            evtAttachment.Text = ev.Attachment;

        evtFieldsUpdating = false;

        RefreshEventFieldVisibility(ev.Type);
    }

    private void RefreshEventFieldVisibility(AnimEventType type)
    {
        evtAttachFields.IsVisible = type == AnimEventType.Attach;

        var targetSlotRow = eventPropsPanel.GetRow("TargetSlot");
        if (targetSlotRow != null) targetSlotRow.IsVisible = type == AnimEventType.Attach;

        var bodygroupRow = eventPropsPanel.GetRow("Bodygroup");
        if (bodygroupRow != null) bodygroupRow.IsVisible = type is AnimEventType.SetBodygroupVisible or AnimEventType.SetBodygroupHidden;

        var optionsRow = eventPropsPanel.GetRow("Options");
        if (optionsRow != null) optionsRow.IsVisible = type is AnimEventType.Custom or AnimEventType.PlaySequence;

        var optionsLabel = eventPropsPanel.GetLabel("Options");
        if (optionsLabel != null) optionsLabel.Content = (type == AnimEventType.PlaySequence ? "Sequence Name" : "Options") + ":";

        var slotLabel = eventPropsPanel.GetLabel("Slot");
        if (slotLabel != null) slotLabel.Content = (type == AnimEventType.PlaySequence ? "Target Slot (blank = self)" : "Slot") + ":";
    }

    private CAnimEvent GetSelectedEvent()
    {
        var animDef = GetSelectedAnimDef();
        if (animDef?.Events == null || selectedEventIndex < 0 || selectedEventIndex >= animDef.Events.Count) return null;
        return animDef.Events[selectedEventIndex];
    }

    private void EvtAttachment_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (evtFieldsUpdating) return;
        var ev = GetSelectedEvent(); if (ev == null) return;
        ev.Attachment = evtAttachment.SelectedItem as string ?? evtAttachment.Text;
    }

    private void EvtAttachment_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (evtFieldsUpdating) return;
        var ev = GetSelectedEvent(); if (ev == null) return;
        ev.Attachment = evtAttachment.Text;
    }

    // "Type" changing needs to re-run visibility logic
    private void HookEventTypeChange()
    {
        var typeControl = eventPropsPanel.GetControl("Type");
        if (typeControl is ComboBox combo)
        {
            combo.SelectionChanged += (s, e) =>
            {
                var ev = GetSelectedEvent();
                if (ev != null) RefreshEventFieldVisibility(ev.Type);
            };
        }
    }

    public void RefreshBlendSequenceList()
    {
        if (ModelEditorData.ActiveModel?.Sequences == null) return;
        viewBlendSequenceBox.Items.Clear();

        int i = 0;
        foreach (var seq in ModelEditorData.ActiveModel.Sequences)
        {
            viewBlendSequenceBox.Items.Add(new SequenceViewModel(i++));
        }

        UpdateSequenceEditPreview();
    }

    private CSequence GetSelectedBlendSequence()
    {
        if (activeBlendSequence == null || ModelEditorData.ActiveModel?.Sequences == null) return null;
        return activeBlendSequence.ID < ModelEditorData.ActiveModel.Sequences.Count
            ? ModelEditorData.ActiveModel.Sequences[activeBlendSequence.ID] : null;
    }

    private void BlendSequenceList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewBlendSequenceBox.SelectedItem is not SequenceViewModel seq) return;

        activeBlendSequence = seq;
        RefreshBlendSequenceEditor();
    }

    private void BtnNewSequence_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel == null) return;

        ModelEditorData.ActiveModel.Sequences ??= new List<CSequence>();
        ModelEditorData.ActiveModel.Sequences.Add(new CSequence
        {
            Name = "new_sequence",
            Root = new BlendSource { Type = BlendSourceType.Clip }
        });

        RefreshBlendSequenceList();
    }

    private void BtnDeleteSequence2_Click(object? sender, RoutedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;

        ModelEditorData.ActiveModel.Sequences.Remove(seq);
        activeBlendSequence = null;
        RefreshBlendSequenceList();
        RefreshBlendSequenceEditor();
    }

    private void BlendSequenceName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;

        seq.Name = blendSequenceNameBox.Text;
        RefreshBlendSequenceList();
    }

    private void BlendRootType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || blendRootTypeBox.SelectedItem is not ComboBoxItem item) return;

        var type = Enum.Parse<BlendSourceType>(item.Content.ToString());
        if (seq.Root.Type == type) return;

        seq.Root = new BlendSource { Type = type };
        RefreshBlendSequenceEditor();
    }

    private void RefreshBlendSequenceEditor()
    {
        var seq = GetSelectedBlendSequence();

        blendSequenceNameBox.Text = seq?.Name ?? "";
        blendRootTypeBox.IsEnabled = seq != null;

        if (seq == null)
        {
            blendClipPanel.IsVisible = false;
            blend1DPanel.IsVisible = false;
            blend2DPanel.IsVisible = false;
            return;
        }

        SelectComboItem(blendRootTypeBox, seq.Root.Type.ToString());

        blendClipPanel.IsVisible = seq.Root.Type == BlendSourceType.Clip;
        blend1DPanel.IsVisible = seq.Root.Type == BlendSourceType.Blend1D;
        blend2DPanel.IsVisible = seq.Root.Type == BlendSourceType.Blend2D;

        if (seq.Root.Type == BlendSourceType.Clip)
        {
            RefreshClipPanel(seq.Root);
        }
        else if (seq.Root.Type == BlendSourceType.Blend1D)
        {
            RefreshBlend1DList(seq.Root);
        }
        else if (seq.Root.Type == BlendSourceType.Blend2D)
        {
            blend2DCanvas.Select(-1);
            lastBlend2DSelectedIndex = -1;
            blend2DEntryXBox.IsEnabled = false;
            blend2DEntryYBox.IsEnabled = false;
            blend2DEntryAnimBox.IsEnabled = false;
            blend2DEntryAnimBox.Items.Clear();

            blend2DFieldsUpdating = true;
            blend2DParamXBox.Text = seq.Root.ParamNameX ?? "";
            blend2DParamYBox.Text = seq.Root.ParamNameY ?? "";
            blend2DMinXBox.Text = seq.Root.MinX.ToString("0.#");
            blend2DMaxXBox.Text = seq.Root.MaxX.ToString("0.#");
            blend2DMinYBox.Text = seq.Root.MinY.ToString("0.#");
            blend2DMaxYBox.Text = seq.Root.MaxY.ToString("0.#");
            blend2DFieldsUpdating = false;
            RedrawBlend2D();
        }
    }

    private void RefreshClipPanel(BlendSource root)
    {
        blendClipAnimBox.Items.Clear();
        foreach (var anim in ModelEditorData.ActiveModel.Animations)
        {
            blendClipAnimBox.Items.Add(anim.Name);
        }
        blendClipAnimBox.SelectedItem = root.AnimationName;
    }

    private void BlendClipAnim_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || blendClipAnimBox.SelectedItem is not string name) return;
        seq.Root.AnimationName = name;
        UpdateSequenceEditPreview();
    }

    private void RefreshBlend1DList(BlendSource root)
    {
        blend1DParamBox.Text = root.ParamName ?? "";

        int preserve = activeBlend1DEntryIndex;
        blend1DEntryBox.Items.Clear();

        for (int i = 0; i < root.Entries1D.Count; i++)
        {
            var entry = root.Entries1D[i];
            string label = $"{entry.Value:0.##}  →  {entry.Source?.AnimationName ?? "(none)"}";
            blend1DEntryBox.Items.Add(new BlendEntryViewModel(i, label));
        }

        activeBlend1DEntryIndex = -1;
        if (preserve >= 0 && preserve < blend1DEntryBox.Items.Count)
        {
            blend1DEntryBox.SelectedIndex = preserve;
        }
        else
        {
            blend1DEntryValueBox.IsEnabled = false;
            blend1DEntryAnimBox.IsEnabled = false;
        }
    }


    private void Blend1DParam_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;
        seq.Root.ParamName = blend1DParamBox.Text;
        UpdateSequenceEditPreview();
    }

    private void BtnAddBlend1DEntry_Click(object? sender, RoutedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || ModelEditorData.ActiveModel.Animations.Count == 0) return;

        seq.Root.Entries1D.Add(new Blend1DEntry
        {
            Value = 0f,
            Source = new BlendSource
            {
                Type = BlendSourceType.Clip,
                AnimationName = ModelEditorData.ActiveModel.Animations[0].Name
            }
        });

        RefreshBlend1DList(seq.Root);
    }
    private void BtnRemoveBlend1DEntry_Click(object? sender, RoutedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || activeBlend1DEntryIndex < 0) return;

        seq.Root.Entries1D.RemoveAt(activeBlend1DEntryIndex);
        RefreshBlend1DList(seq.Root);
    }

    private void Blend1DEntryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || blend1DEntryBox.SelectedItem is not BlendEntryViewModel item)
        {
            activeBlend1DEntryIndex = -1;
            blend1DEntryValueBox.IsEnabled = false;
            blend1DEntryAnimBox.IsEnabled = false;
            return;
        }

        activeBlend1DEntryIndex = item.Index;
        var entry = seq.Root.Entries1D[activeBlend1DEntryIndex];

        blend1DFieldsUpdating = true;
        blend1DEntryValueBox.IsEnabled = true;
        blend1DEntryAnimBox.IsEnabled = true;
        blend1DEntryValueBox.Text = entry.Value.ToString();
        blend1DEntryAnimBox.Items.Clear();
        foreach (var anim in ModelEditorData.ActiveModel.Animations)
        {
            blend1DEntryAnimBox.Items.Add(anim.Name);
        }
        blend1DEntryAnimBox.SelectedItem = entry.Source?.AnimationName;
        blend1DFieldsUpdating = false;
    }
    private void Blend1DEntryValue_LostFocus(object? sender, RoutedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;
        RefreshBlend1DList(seq.Root);
        UpdateSequenceEditPreview();
    }

    private void Blend1DEntryAnim_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (blend1DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null || activeBlend1DEntryIndex < 0) return;
        if (blend1DEntryAnimBox.SelectedItem is not string name) return;

        seq.Root.Entries1D[activeBlend1DEntryIndex].Source.AnimationName = name;
        UpdateSequenceEditPreview();

        Dispatcher.UIThread.Post(() =>
        {
            var s = GetSelectedBlendSequence();
            if (s != null) RefreshBlend1DList(s.Root);
        });
    }
    private void Blend1DEntryValue_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (blend1DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null || activeBlend1DEntryIndex < 0) return;
        if (!float.TryParse(blend1DEntryValueBox.Text, out float value)) return;

        seq.Root.Entries1D[activeBlend1DEntryIndex].Value = value;
        UpdateSequenceEditPreview();
    }

    private void RedrawBlend2D()
    {
        var seq = GetSelectedBlendSequence();
        blend2DCanvas.Redraw(blend2DView, seq?.Root);
    }

    private bool blend2DFieldsUpdating = false;

    private void Blend2DRange_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (blend2DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;

        if (float.TryParse(blend2DMinXBox.Text, out float minX)) seq.Root.MinX = minX;
        if (float.TryParse(blend2DMaxXBox.Text, out float maxX)) seq.Root.MaxX = maxX;
        if (float.TryParse(blend2DMinYBox.Text, out float minY)) seq.Root.MinY = minY;
        if (float.TryParse(blend2DMaxYBox.Text, out float maxY)) seq.Root.MaxY = maxY;

        RedrawBlend2D();
    }
    private void OnBlend2DEntrySelected(int index)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || index < 0 || index >= seq.Root.Entries2D.Count) return;

        lastBlend2DSelectedIndex = index;
        var entry = seq.Root.Entries2D[index];

        blend2DFieldsUpdating = true;
        blend2DEntryXBox.IsEnabled = true;
        blend2DEntryYBox.IsEnabled = true;
        blend2DEntryAnimBox.IsEnabled = true;
        blend2DEntryXBox.Text = entry.X.ToString("0.##");
        blend2DEntryYBox.Text = entry.Y.ToString("0.##");
        blend2DEntryAnimBox.Items.Clear();
        foreach (var anim in ModelEditorData.ActiveModel.Animations)
        {
            blend2DEntryAnimBox.Items.Add(anim.Name);
        }
        blend2DEntryAnimBox.SelectedItem = entry.Source?.AnimationName;
        blend2DFieldsUpdating = false;
    }

    private void Blend2DEntryAnim_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (blend2DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null || blend2DEntryAnimBox.SelectedItem is not string name) return;
        if (lastBlend2DSelectedIndex < 0 || lastBlend2DSelectedIndex >= seq.Root.Entries2D.Count) return;

        seq.Root.Entries2D[lastBlend2DSelectedIndex].Source.AnimationName = name;
        RedrawBlend2D();
        UpdateSequenceEditPreview();
    }

    private void Blend2DEntryX_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (blend2DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null || lastBlend2DSelectedIndex < 0 || lastBlend2DSelectedIndex >= seq.Root.Entries2D.Count) return;
        if (!float.TryParse(blend2DEntryXBox.Text, out float x)) return;

        seq.Root.Entries2D[lastBlend2DSelectedIndex].X = x;
        RedrawBlend2D();
    }

    private void Blend2DEntryY_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (blend2DFieldsUpdating) return;
        var seq = GetSelectedBlendSequence();
        if (seq == null || lastBlend2DSelectedIndex < 0 || lastBlend2DSelectedIndex >= seq.Root.Entries2D.Count) return;
        if (!float.TryParse(blend2DEntryYBox.Text, out float y)) return;

        seq.Root.Entries2D[lastBlend2DSelectedIndex].Y = y;
        RedrawBlend2D();
    }
    private void Blend2DParamX_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;
        seq.Root.ParamNameX = blend2DParamXBox.Text;
        UpdateSequenceEditPreview();
    }
    private void Blend2DParamY_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;
        seq.Root.ParamNameY = blend2DParamYBox.Text;
        UpdateSequenceEditPreview();
    }

    private void Blend2DEntryCoord_LostFocus(object? sender, RoutedEventArgs e) => UpdateSequenceEditPreview();

    private void Blend2DSnap_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        blend2DCanvas.SnapEnabled = blend2DSnapCheck.IsChecked == true;
    }

    private void BtnRemoveBlend2DEntry_Click(object? sender, RoutedEventArgs e)
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null) return;
        blend2DCanvas.RemoveSelected(seq.Root);
        lastBlend2DSelectedIndex = -1;
        blend2DEntryXBox.IsEnabled = false;
        blend2DEntryYBox.IsEnabled = false;
        blend2DEntryAnimBox.IsEnabled = false;
        blend2DEntryAnimBox.Items.Clear();
        RedrawBlend2D();
        UpdateSequenceEditPreview();
    }

    private int lastBlend2DSelectedIndex = -1;

    private static void SelectComboItem(ComboBox box, string content)
    {
        foreach (var obj in box.Items)
        {
            if (obj is ComboBoxItem item && item.Content?.ToString() == content)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private readonly List<PreviewLayerConfig> previewLayers = new();
    private bool previewPlaying = false;
    private CModelAnimator layerPreviewAnimator;
    private CModelAnimator sequenceEditAnimator;
    private const string SequenceEditLayerName = "_sequenceEditPreview";
    private bool blend1DFieldsUpdating = false;

    private void EnsurePreviewAnimator()
    {
        var model = ModelEditorData.ActiveModel;
        if (model == null) { layerPreviewAnimator = null; ModelEditorData.PreviewAnimator = null; return; }
        if (layerPreviewAnimator == null)
        {
            layerPreviewAnimator = new CModelAnimator(model);
        }
        ModelEditorData.PreviewAnimator = layerPreviewAnimator;
    }

    private void SyncPreviewLayer(PreviewLayerConfig config)
    {
        var model = ModelEditorData.ActiveModel;
        if (model?.Sequences == null) return;
        if (config.SequenceIndex < 0 || config.SequenceIndex >= model.Sequences.Count) return;

        EnsurePreviewAnimator();
        var layer = layerPreviewAnimator.GetOrAddLayer(config.LayerName, config.BlendType);
        layer.Influence = config.Influence;
        layer.BlendType = config.BlendType;
        layer.Play(model.Sequences[config.SequenceIndex]);

        foreach (var (paramName, value) in config.ParamValues)
            layer.SetParam(paramName, value);
    }

    private void BtnPreviewPlay_Click(object? sender, RoutedEventArgs e)
    {
        EnsurePreviewAnimator();
        if (layerPreviewAnimator != null) layerPreviewAnimator.Paused = false;
    }

    private void BtnPreviewStop_Click(object? sender, RoutedEventArgs e)
    {
        if (layerPreviewAnimator != null) layerPreviewAnimator.Paused = true;
        ModelEditorData.PreviewRootMotionAccum = Vector3.Zero;
    }

    private void BtnClearPreviewLayers_Click(object? sender, RoutedEventArgs e)
    {
        previewLayers.Clear();
        previewLayerStack.Children.Clear();
        layerPreviewAnimator = null;
        ModelEditorData.PreviewAnimator = null;
        ModelEditorData.PreviewRootMotionAccum = Vector3.Zero;
    }
    private void BtnAddPreviewLayer_Click(object? sender, RoutedEventArgs e)
    {
        if (ModelEditorData.ActiveModel?.Sequences is not { Count: > 0 }) return;
        var config = new PreviewLayerConfig { LayerName = $"layer{previewLayers.Count}" };
        previewLayers.Add(config);
        AddPreviewLayerRow(config);
        SyncPreviewLayer(config);
    }

    private void EnsureSequenceEditAnimator()
    {
        var model = ModelEditorData.ActiveModel;
        if (model == null) { sequenceEditAnimator = null; return; }
        sequenceEditAnimator ??= new CModelAnimator(model);
    }

    private void UpdateSequenceEditPreview()
    {
        var seq = GetSelectedBlendSequence();
        if (seq == null || ModelEditorData.ActiveModel == null)
        {
            sequenceEditAnimator = null;
            blend1DPreviewParamHost.Children.Clear();
            blend2DPreviewParamHost.Children.Clear();
            if (viewTabs.SelectedItem is TabItem tab && tab.Header?.ToString() == "Sequences")
                ModelEditorData.PreviewAnimator = null;
            return;
        }

        EnsureSequenceEditAnimator();
        var layer = sequenceEditAnimator.GetOrAddLayer(SequenceEditLayerName, LayerBlendType.Replace);
        layer.Influence = 1f;
        layer.ForceReplay(seq);
        sequenceEditAnimator.Paused = false;

        if (viewTabs.SelectedItem is TabItem activeTab && activeTab.Header?.ToString() == "Sequences")
            ModelEditorData.PreviewAnimator = sequenceEditAnimator;

        blend1DPreviewParamHost.Children.Clear();
        blend2DPreviewParamHost.Children.Clear();

        if (seq.Root.Type == BlendSourceType.Blend1D)
        {
            RebuildSequencePreviewParamSliders(seq, layer, blend1DPreviewParamHost);
        }
        else if (seq.Root.Type == BlendSourceType.Blend2D)
        {
            RebuildSequencePreviewParamSliders(seq, layer, blend2DPreviewParamHost);
        }
    }

    private void RebuildSequencePreviewParamSliders(CSequence seq, LayerConfig layer, StackPanel host)
    {
        host.Children.Clear();

        var sliders = new List<(string name, float min, float max)>();
        if (seq.Root.Type == BlendSourceType.Blend1D && !string.IsNullOrEmpty(seq.Root.ParamName))
        {
            sliders.Add((seq.Root.ParamName, -180f, 180f));
        }
        else if (seq.Root.Type == BlendSourceType.Blend2D)
        {
            if (!string.IsNullOrEmpty(seq.Root.ParamNameX)) sliders.Add((seq.Root.ParamNameX, seq.Root.MinX, seq.Root.MaxX));
            if (!string.IsNullOrEmpty(seq.Root.ParamNameY)) sliders.Add((seq.Root.ParamNameY, seq.Root.MinY, seq.Root.MaxY));
        }

        if (sliders.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "(name a param above to preview it)",
                FontSize = 10,
                Opacity = 0.6,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var (paramName, min, max) in sliders)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            block.Children.Add(new TextBlock
            {
                Text = paramName,
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 2),
            });

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var slider = new Slider { Minimum = min, Maximum = max, Value = 0, VerticalAlignment = VerticalAlignment.Center };
            var valueLabel = new TextBlock
            {
                Text = "0",
                Foreground = Brushes.White,
                FontSize = 10,
                Width = 32,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };

            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name != nameof(Slider.Value)) return;
                float v = (float)slider.Value;
                layer.SetParam(paramName, v);
                valueLabel.Text = v.ToString("0.#");
            };

            Grid.SetColumn(slider, 0);
            Grid.SetColumn(valueLabel, 1);
            row.Children.Add(slider);
            row.Children.Add(valueLabel);

            block.Children.Add(row);
            host.Children.Add(block);
        }
    }

    private void AddPreviewLayerRow(PreviewLayerConfig config)
    {
        var seqs = ModelEditorData.ActiveModel.Sequences;

        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition(130, GridUnitType.Pixel));
        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(36, GridUnitType.Pixel));
        row.ColumnDefinitions.Add(new ColumnDefinition(72, GridUnitType.Pixel));
        row.ColumnDefinitions.Add(new ColumnDefinition(24, GridUnitType.Pixel));

        var paramPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

        var seqPicker = new ComboBox { Margin = new Thickness(2) };
        foreach (var s in seqs) seqPicker.Items.Add(s.Name);
        seqPicker.SelectedIndex = Math.Clamp(config.SequenceIndex, 0, seqs.Count - 1);
        seqPicker.SelectionChanged += (s, e) =>
        {
            config.SequenceIndex = seqPicker.SelectedIndex;
            RebuildParamSliders(config, paramPanel);
            SyncPreviewLayer(config);
        };
        Grid.SetColumn(seqPicker, 0);

        var slider = new Slider { Minimum = 0, Maximum = 1, Value = config.Influence, Margin = new Thickness(4, 0), MinWidth = 60, VerticalAlignment = VerticalAlignment.Center };
        var influenceLabel = new TextBlock { Text = config.Influence.ToString("0.00"), Foreground = Brushes.White, Width = 32, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
        slider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name != nameof(Slider.Value)) return;
            config.Influence = (float)slider.Value;
            influenceLabel.Text = config.Influence.ToString("0.00");
            SyncPreviewLayer(config);
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(influenceLabel, 2);

        var blendPicker = new ComboBox { Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        blendPicker.Items.Add("Replace");
        blendPicker.Items.Add("Additive");
        blendPicker.SelectedIndex = (int)config.BlendType;
        blendPicker.SelectionChanged += (s, e) => { config.BlendType = (LayerBlendType)blendPicker.SelectedIndex; SyncPreviewLayer(config); };
        Grid.SetColumn(blendPicker, 3);

        var deleteBtn = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            Margin = new Thickness(2),
            Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 96, 96)),
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteBtn.Click += (s, e) =>
        {
            previewLayers.Remove(config);
            previewLayerStack.Children.Remove(container);
        };
        Grid.SetColumn(deleteBtn, 4);

        row.Children.AddRange(new Control[] { seqPicker, slider, influenceLabel, blendPicker, deleteBtn });

        container.Children.Add(row);
        container.Children.Add(paramPanel);
        previewLayerStack.Children.Add(container);

        RebuildParamSliders(config, paramPanel);
    }

    private void RebuildParamSliders(PreviewLayerConfig config, WrapPanel paramPanel)
    {
        paramPanel.Children.Clear();
        config.ParamValues.Clear();

        var seqs = ModelEditorData.ActiveModel?.Sequences;
        if (seqs == null || config.SequenceIndex < 0 || config.SequenceIndex >= seqs.Count) return;

        var root = seqs[config.SequenceIndex].Root;
        List<string> paramNames = root.Type switch
        {
            BlendSourceType.Blend1D => new List<string> { root.ParamName },
            BlendSourceType.Blend2D => new List<string> { root.ParamNameX, root.ParamNameY },
            _ => new List<string>()
        };

        foreach (var paramName in paramNames.Where(n => !string.IsNullOrEmpty(n)))
        {
            config.ParamValues[paramName] = 0f;

            var label = new TextBlock { Text = paramName, Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var paramSlider = new Slider { Minimum = -180, Maximum = 180, Value = 0, Width = 100, Margin = new Thickness(2, 0) };
            paramSlider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name != nameof(Slider.Value)) return;
                config.ParamValues[paramName] = (float)paramSlider.Value;
                SyncPreviewLayer(config);
            };

            paramPanel.Children.Add(label);
            paramPanel.Children.Add(paramSlider);
        }
    }

    private void ViewTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Design.IsDesignMode || viewTabs == null || ModelEditorData.ActiveModel == null) return;

        string header = (viewTabs.SelectedItem as TabItem)?.Header?.ToString();

        if (header == "Layer Preview")
        {
            ModelEditorData.PreviewAnimator = layerPreviewAnimator;
        }
        else if (header == "Sequences")
        {
            RedrawBlend2D();
            UpdateSequenceEditPreview();
        }
        else
        {
            ModelEditorData.PreviewAnimator = null;
        }
    }

    private void SetupMorphSliders()
    {
        morphStack.Children.Clear();

        if (ModelEditorData.ActiveModel?.Bodygroups == null) return;

        var morphNames = ModelEditorData.ActiveModel.Bodygroups
            .Where(bg => bg.MorphTargets?.Count > 0)
            .SelectMany(bg => bg.MorphTargets.Select(m => m.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (morphNames.Count == 0)
        {
            morphStack.Children.Add(new TextBlock
            {
                Text = "No morph targets.",
                Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(100, 100, 100)),
                FontStyle = FontStyle.Italic,
                FontSize = 11,
                Margin = new Thickness(6, 4),
            });
            return;
        }

        ModelEditorData.MorphState ??= new CMorphState();

        foreach (var name in morphNames)
        {
            var capturedName = name;
            float initialWeight = ModelEditorData.MorphState.GetWeight(name);

            var row = new Grid
            {
                Margin = new Thickness(2, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(28, GridUnitType.Pixel));

            var label = new TextBlock
            {
                Text = name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(180, 180, 180)),
                Margin = new Thickness(2, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var valueLabel = new TextBlock
            {
                Text = initialWeight.ToString("0.00"),
                FontSize = 10,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
            };

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = initialWeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
            };

            slider.ValueChanged += (s, e) =>
            {
                float w = (float)slider.Value;
                valueLabel.Text = w.ToString("0.00");
                ModelEditorData.MorphState?.SetWeight(capturedName, w);
            };

            var resetBtn = new Button
            {
                Content = "↶",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTip.SetTip(resetBtn, "Reset to 0");
            resetBtn.Click += (s, e) => slider.Value = 0;

            var inner = new StackPanel { Spacing = 0 };

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            topRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(label, 0);
            Grid.SetColumn(valueLabel, 1);
            topRow.Children.Add(label);
            topRow.Children.Add(valueLabel);

            inner.Children.Add(topRow);
            inner.Children.Add(slider);

            Grid.SetColumn(inner, 0);
            Grid.SetColumn(resetBtn, 1);
            row.Children.Add(inner);
            row.Children.Add(resetBtn);

            morphStack.Children.Add(row);
        }
    }
}

public class PreviewLayerConfig
{
    public string LayerName = "layer0";
    public int SequenceIndex = 0;
    public float Influence = 1f;
    public LayerBlendType BlendType = LayerBlendType.Replace;
    public Dictionary<string, float> ParamValues = new();
}