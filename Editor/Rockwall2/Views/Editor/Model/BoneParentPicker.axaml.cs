using Avalonia.Controls;
using Avalonia.Interactivity;
using Relic.Models;
using System;
using System.Collections.Generic;

namespace Rockwall2;

public partial class BoneParentPicker : Window
{
    public BoneParentPicker()
    {
        InitializeComponent();
    }

    public BoneParentPicker(List<CBone> bones, int preselectIndex = 0) : this()
    {
        foreach (var bone in bones)
            boneCombo.Items.Add(new ComboBoxItem { Content = bone.Name });
        if (boneCombo.Items.Count > 0)
            boneCombo.SelectedIndex = Math.Clamp(preselectIndex, 0, boneCombo.Items.Count - 1);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close((int?)boneCombo.SelectedIndex);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close((int?)null);
}