using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Rockwall2.Editor.Common;
using Rockwall2.Views;
using System.IO;
using System.Threading.Tasks;

namespace Rockwall2;

public partial class ConfigEditorWindow : Window
{
    public bool Result { get; private set; } = false;
    public ConfigFile WorkingConfig;

    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);
    public ConfigEditorWindow()
    {
        InitializeComponent();
    }
    public ConfigEditorWindow(ConfigFile file)
    {
        InitializeComponent();
        WorkingConfig = file;
        Reload();

        gameNameBox.TextChanged += (s, e) =>
        {
            WorkingConfig.GameName = gameNameBox.Text ?? "";
        };
    }
    void Reload()
    {
        gameNameBox.Text = WorkingConfig.GameName;
        gameEXEPath.Text = WorkingConfig.GamePath;
        gameDataPath.Text = WorkingConfig.GameEDF;
        compToolPath.Text = WorkingConfig.CompileTool;
        editConfPath.Text = WorkingConfig.EditorAssetsPath;
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

    private async Task<string> BrowseFile(string fileType, string fileExtension)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel.StorageProvider;

        var customType = new FilePickerFileType(fileType)
        {
            Patterns = new[] { fileExtension },
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            FileTypeFilter = new[] { customType },
            AllowMultiple = false,
        });

        if (files == null || files.Count == 0) return null;

        return files[0].TryGetLocalPath();
    }
    private async Task<string> BrowseFolder()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel.StorageProvider;

        var files = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false,
        });

        if (files == null || files.Count == 0) return null;

        return files[0].TryGetLocalPath();
    }

    private async void BrowseData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await BrowseFile("Relic Game Data", "*.eds");
        if (path == null) return;

        WorkingConfig.GameEDF = path;
        Reload();
    }
    private async void BrowseCompiler(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await BrowseFile("Executable", "*");
        if (path == null) return;

        WorkingConfig.CompileTool = path;
        Reload();
    }
    private async void BrowseConfig(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await BrowseFolder();
        if (path == null) return;

        WorkingConfig.EditorAssetsPath = path;
        Reload();
    }
    private async void BrowseExecutable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await BrowseFile("Executable", "*");
        if (path == null) return;

        WorkingConfig.GamePath = path;
        Reload();
    }

    private async void Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel.StorageProvider;

        var customType = new FilePickerFileType("Rockwall 2 Editor Config")
        {
            Patterns = new[] { "*.cfg" },
        };

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Open File",
            FileTypeChoices = new[] {customType},
            SuggestedFileType = customType,
            SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(Path.GetFullPath(ConfigManager.confSavePath))
        });

        if (file == null)
        {
            Close(); 
            return; 
        }

        var path = file.TryGetLocalPath();

        if (path != null)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(WorkingConfig));
            Result = true;
        }

        Close();
    }
    private async void Cancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}