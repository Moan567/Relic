using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
public static class ShaderLoader
{
    public static string Load(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException($"Embedded shader resource not found: {name}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static string LoadWithIncludes(string mainShaderName, params string[] includeNames)
    {
        string main = ShaderLoader.Load(mainShaderName);
        int versionEnd = main.IndexOf('\n') + 1;
        string versionLine = main.Substring(0, versionEnd);
        string rest = main.Substring(versionEnd);

        var sb = new StringBuilder();
        sb.Append(versionLine);
        foreach (var name in includeNames)
        {
            sb.Append(ShaderLoader.Load(name));
            sb.Append('\n');
        }
        sb.Append(rest);
        return sb.ToString();
    }
}