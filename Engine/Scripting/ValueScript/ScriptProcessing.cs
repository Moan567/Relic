using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;
public class ScriptPreprocessor
{
    HashSet<string> visitedFiles = new HashSet<string>();
    
    public string Process(string rootPath)
    {
        var data = File.ReadAllText(rootPath);

        data = Solve(data, Directory.GetParent(rootPath).FullName, rootPath);

        return data;
    }

    static Regex includeExpression = new Regex("^#include\\s+\"([^\"]+)\"");

    string Solve(string file, string rootPath, string currentFile)
    {
        var lines = file.Split('\n');

        StringBuilder sb = new StringBuilder();

        foreach(var line in lines)
        {
            var includeDir = includeExpression.Match(line);

            if (includeDir.Success)
            {
                string includeFilePath = Path.Combine(rootPath,includeDir.Groups[1].Value);

                if(!File.Exists(includeFilePath))
                {
                    throw new FileNotFoundException(includeFilePath);
                }

                if (visitedFiles.Contains(includeFilePath)) continue;
                visitedFiles.Add(includeFilePath);

                sb.Append(Solve(File.ReadAllText(includeFilePath), rootPath, includeFilePath));

                sb.AppendLine();

                continue;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }
}

public class ScriptLoader
{
    public static ScriptBlock LoadScript(string filePath)
    {
        var preprocessor = new ScriptPreprocessor();
        var tokenizer = new ScriptTokenizer();
        var parser = new ScriptParser();
        var data = preprocessor.Process(filePath);

        var parsed = parser.Parse(tokenizer.Tokenize(data));
        parsed = InheritanceResolver.Resolve(parsed);
        parsed = VariableResolver.Resolve(parsed);

        return parsed;
    }
} 