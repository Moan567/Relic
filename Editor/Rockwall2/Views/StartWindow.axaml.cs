using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
using Newtonsoft.Json;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rockwall2;

public partial class StartWindow : Window
{
    public bool Result { get; private set; } = false;
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);
    public StartWindow()
    {
        InitializeComponent();
        Load();
    }

    public void Load()
    {
        ConfigManager.LoadAllConfigs();

        if (ConfigManager.configFiles == null) return;

        var items = new List<string>();
        foreach (var config in ConfigManager.configFiles)
        {
            var conf = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(config));

            items.Add(conf.GameName);
        }
        listBox.ItemsSource = items;
        listBox.DoubleTapped += (s, e) =>
        {
            OpenConfig();
        };
    }
    public void OpenConfig()
    {
        if (listBox.SelectedIndex == -1) return;
        var cfg = listBox.SelectedIndex;

        ConfigManager.LoadConfig(ConfigManager.configFiles[cfg]);

        Result = true;
        Close(true);
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isWindowDragInEffect)
        {
            Point currentCursorPosition = e.GetPosition(this);
            Point delta = currentCursorPosition - cursorDragStart;
            Position = this.PointToScreen(delta);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => isWindowDragInEffect = false;

    private async void NewConfig(object sender, RoutedEventArgs e)
    {
        var window = new ConfigEditorWindow(default);
        await window.ShowDialog(this);

        if(window.Result)
        {
            Load();
        }
    }

    private void M_Open(object sender, RoutedEventArgs e)
    {

    }
    private async void M_Edit(object sender, RoutedEventArgs e)
    {
        MenuItem menuItem = sender as MenuItem;
        if (menuItem != null)
        {
            var itemIndex = ConfigManager.configFiles.ToList().FindIndex((t) =>
            {
                var conf = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(t));

                return conf.GameName == (string)(menuItem.DataContext);
            });
            if (itemIndex == -1) return;

            var confEdit = new ConfigEditorWindow(JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigManager.configFiles[itemIndex])));
            await confEdit.ShowDialog(this);

            if (confEdit.Result != true) return;

            Load();
        }
    }
    private async void M_Delete(object sender, RoutedEventArgs e)
    {
        //MessageBoxResult result = MessageBox.Show("Are you sure?", "Delete Confirmation", MessageBoxButton.YesNo);

        var box = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
        {
            ButtonDefinitions = new List<ButtonDefinition>
            {
                new ButtonDefinition
                {
                    Name = "Yes"
                },
                new ButtonDefinition
                {
                    Name = "No"
                },
            },
            ContentTitle="Confirmation",
            ContentMessage="Are you sure?",
            SystemDecorations = SystemDecorations.BorderOnly,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CloseOnClickAway = true
        });
        var result = await box.ShowAsync();
        if (result != "Yes") return;

        if (sender is MenuItem menuItem)
        {
            var itemIndex = ConfigManager.configFiles.ToList().FindIndex((t)=>
            {
                var conf = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(t));

                return conf.GameName == (string)(menuItem.DataContext);
            });
            if (itemIndex == -1) return;

            ConfigManager.DeleteConfig(ConfigManager.configFiles[itemIndex]);
            Load();
        }
    }
}