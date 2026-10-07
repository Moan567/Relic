using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Console;

public class CommandBinding
{
    public string commandName;
    public Action<string[]> command;
    public Func<string[], int, IEnumerable<string>> completer;

    public CommandBinding(string commandName, Action<string[]> command, Func<string[], int, IEnumerable<string>> completer = null)
    {
        this.commandName = commandName;
        this.command = command;
        this.completer = completer;
        Create();
    }

    public void Create()
    {
        MainEngine.commands.Enqueue(this);
        MainEngine.commandsDirty = true;
    }
}