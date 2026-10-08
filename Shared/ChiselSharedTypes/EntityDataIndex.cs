using System.IO;

namespace Chisel;

public sealed class EntityDataIndex
{
    public const string FileName = "def.eds";

    public string ClassnamesPath { get; private init; }
    public string LookupTablePath { get; private init; }
    public string ContentPath { get; private init; }
    public string MaterialsPath { get; private init; }
    public string MetadataPath { get; private init; }

    public static void Write(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);

        File.WriteAllText(Path.Combine(dataDirectory, FileName), string.Join('\n',
            "entnme.edt",
            "entlid.edt",
            "../Content",
            "../Content/Materials",
            "entMETA.gff"));
    }

    public static EntityDataIndex Read(string edsPath)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(edsPath));
        string[] lines = File.ReadAllLines(edsPath);

        string Resolve(int index)
        {
            return Path.GetFullPath(Path.Combine(root, lines[index].Trim()));
        }

        return new EntityDataIndex
        {
            ClassnamesPath = Resolve(0),
            LookupTablePath = Resolve(1),
            ContentPath = Resolve(2),
            MaterialsPath = Resolve(3),
            MetadataPath = Resolve(4)
        };
    }
}