using System;
using System.Text;
using System.Threading;

namespace MapCompiler
{
    /// <summary>
    /// Handles all styled console output for the compiler pipeline.
    /// </summary>
    public static class CompilerConsole
    {
        private static readonly object _lock = new object();

        public static void Header(string title)
        {
            lock (_lock)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"══ {title.ToUpper()} ");
                Console.ResetColor();
            }
        }

        public static void Step(string message)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  ► ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static void Info(string message)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("    ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static void Stat(string label, object value)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"    {label}: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(value);
                Console.ResetColor();
            }
        }

        public static void Success(string message)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  ✓ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static void Warn(string message)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  ⚠ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static void Error(string message)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  ✗ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static ProgressBar StartProgress(string label)
        {
            lock (_lock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"    {label} ");
                Console.ResetColor();
            }
            return new ProgressBar();
        }
    }

    // Based on https://gist.github.com/DanielSWolf/0ab6a96899cc5377bf54
    public class ProgressBar : IDisposable, IProgress<float>
    {
        private const int BlockCount = 12;

        private float _currentProgress = 0;
        private volatile string _currentText = string.Empty;

        public void Report(float value)
        {
            value = Math.Max(0, Math.Min(1, value));
            Interlocked.Exchange(ref _currentProgress, value);

            int filled = (int)(_currentProgress * BlockCount);
            int percent = (int)(_currentProgress * 100);

            string text = $"[{new string('█', filled)}{new string('░', BlockCount - filled)}] {percent,3}%";
            UpdateText(text);
        }

        private void UpdateText(string text)
        {
            int commonLen = 0;
            int maxCommon = Math.Min(_currentText.Length, text.Length);
            while (commonLen < maxCommon && text[commonLen] == _currentText[commonLen])
                commonLen++;

            var sb = new StringBuilder();
            sb.Append('\b', _currentText.Length - commonLen);
            sb.Append(text[commonLen..]);

            int overlap = _currentText.Length - text.Length;
            if (overlap > 0)
            {
                sb.Append(' ', overlap);
                sb.Append('\b', overlap);
            }

            Console.Write(sb);
            _currentText = text;
        }

        public void Dispose() => UpdateText(string.Empty);
    }
}
