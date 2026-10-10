using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Toolbar;
public sealed class ToolIconAtlas
{
    private readonly Bitmap sheet;
    private readonly int cellSize;

    public ToolIconAtlas(Bitmap sheet, int cellSize)
    {
        this.sheet = sheet;
        this.cellSize = cellSize;
    }

    public CroppedBitmap Get(int col, int row) =>
        new(sheet, new PixelRect(col * cellSize, row * cellSize, cellSize, cellSize));
}