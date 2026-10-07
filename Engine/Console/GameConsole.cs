using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Console
{
    public class GameConsole
    {
        public string OutputLog
        {
            get
            {
                return log.ToString();
            }
        }
        public bool IsInputActive { get; set; }

        private Dictionary<string, Action<string[]>> registeredCommands = new Dictionary<string, Action<string[]>>();
        private Dictionary<string, Func<string[], int, IEnumerable<string>>> completers = new();
        private StringBuilder log = new StringBuilder();

        private List<(string log, float time, LogLevel level)> recentLogs = [];
        public enum LogLevel
        {
            Message,
            Warning,
            Error,
        }

        public void RegisterCommand(string name, Action<string[]> action, Func<string[], int, IEnumerable<string>> completer = null)
        {
            if (registeredCommands.ContainsKey(name)) return;
            registeredCommands.Add(name, action);

            if (completer != null)
                completers.Add(name, completer);
        }

        internal void WriteDirect(string log, LogLevel level = LogLevel.Message, float startTime = 0f)
        {
            this.log.AppendLine(log);
            MainEngine.Instance.ConsoleWindow?.Append(log);

            recentLogs.Add((log, startTime, level));
        }
        public void TickRecentLogs()
        {
            if (recentLogs.Count <= 0) return;

            float scalar = 1 * (int.Min(recentLogs.Count, 32)/2f);

            for (int i = 0; i < int.Min(recentLogs.Count, 4); i++)
            {
                recentLogs[i] = recentLogs[i] with { time = recentLogs[i].time + MainEngine.PreviousFrameDelta * (1 - i / 4f) * scalar };

                if (recentLogs[i].time > 4f) recentLogs.RemoveAt(0);
            }
        }
        public List<(string log, float time, LogLevel level)> GetRecentLogs() => recentLogs;
        public void Execute(string input)
        {
            string[] args = input.Trim().Split(' ');

            string[] passArgs = args == null || args.Length <= 1 ? Array.Empty<string>() : input.Remove(0, args[0].Length + 1).Split(' ');

            WriteDirect($"] {input}");

            if (registeredCommands.TryGetValue(args[0], out var cmd))
            {
#if !DEBUG
                try
                {
#endif
                cmd.Invoke(passArgs);
#if !DEBUG
                }
                catch(Exception ex)
                {
                    WriteDirect(ex.Message, LogLevel.Error);
                }
#endif
            }
            else
            {
                WriteDirect($"Command {args[0]} not recognized.", LogLevel.Warning);
            }
        }
        public void ListCommands()
        {
            foreach(var cmd in registeredCommands.Keys)
            {
                WriteDirect(cmd);
            }
        }
        public List<string> GetCompletions(string input)
        {
            string[] tokens = input.Split(' ');
            string partial = tokens[^1];
            IEnumerable<string> candidates;

            if (tokens.Length == 1)
            {
                candidates = registeredCommands.Keys;
            }
            else if (completers.TryGetValue(tokens[0], out var completer))
            {
                string[] args = tokens[1..];
                candidates = completer(args, args.Length - 1) ?? Enumerable.Empty<string>();
            }
            else
            {
                return [];
            }

            return candidates
                .Where(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string ApplyCompletion(string input, string completion)
        {
            int lastSpace = input.LastIndexOf(' ');
            return input[..(lastSpace + 1)] + completion + " ";
        }
    }
}
