using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed class GlslIncludeResolver
    {
        private struct EmbeddedSource
        {
            public Assembly Assembly;
            public string ResourcePrefix;
        }

        private readonly List<string> _directories = new List<string>();
        private readonly List<EmbeddedSource> _embedded = new List<EmbeddedSource>();

        public void AddDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Directory must not be empty.", nameof(directory));
            _directories.Add(directory);
        }

        public void AddEmbeddedResources(Assembly assembly, string resourcePrefix)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (string.IsNullOrEmpty(resourcePrefix))
                throw new ArgumentException("Resource prefix must not be empty.", nameof(resourcePrefix));
            _embedded.Add(new EmbeddedSource { Assembly = assembly, ResourcePrefix = resourcePrefix });
        }

        internal bool TryResolve(string includePath, bool quoteForm, string requestingIdentity,
            out string identity, out string source)
        {
            if (quoteForm && requestingIdentity != null && requestingIdentity.StartsWith("file:", StringComparison.Ordinal))
            {
                var requestingDir = Path.GetDirectoryName(requestingIdentity.Substring("file:".Length));
                if (!string.IsNullOrEmpty(requestingDir))
                {
                    var candidate = Path.GetFullPath(Path.Combine(requestingDir, includePath));
                    if (File.Exists(candidate))
                    {
                        identity = "file:" + candidate;
                        source = File.ReadAllText(candidate);
                        return true;
                    }
                }
            }

            foreach (var directory in _directories)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, includePath));
                if (File.Exists(candidate))
                {
                    identity = "file:" + candidate;
                    source = File.ReadAllText(candidate);
                    return true;
                }
            }

            var normalizedTarget = "." + includePath.Replace('/', '.').Replace('\\', '.');

            foreach (var source_ in _embedded)
            {
                foreach (var resourceName in source_.Assembly.GetManifestResourceNames())
                {
                    if (!resourceName.StartsWith(source_.ResourcePrefix, StringComparison.Ordinal))
                        continue;

                    if (resourceName.EndsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        identity = "res:" + source_.Assembly.GetName().Name + ":" + resourceName;
                        using (var stream = source_.Assembly.GetManifestResourceStream(resourceName))
                        using (var reader = new StreamReader(stream))
                            source = reader.ReadToEnd();
                        return true;
                    }
                }
            }

            identity = null;
            source = null;
            return false;
        }
    }

    internal static class GlslPreprocessor
    {
        private static readonly Regex IncludeRegex = new Regex(
            "^[ \\t]*#include\\s+(?:\"(?<quoted>[^\"]+)\"|<(?<angled>[^>]+)>)[ \\t]*\\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public static string Expand(string source, string identity, GlslIncludeResolver resolver)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return Expand(source, identity, resolver, seen);
        }

        private static string Expand(string source, string identity, GlslIncludeResolver resolver, HashSet<string> seen)
        {
            seen.Add(identity);

            return IncludeRegex.Replace(source, match =>
            {
                var quoted = match.Groups["quoted"].Success;
                var path = quoted ? match.Groups["quoted"].Value : match.Groups["angled"].Value;

                if (!resolver.TryResolve(path, quoted, identity, out var includedIdentity, out var includedSource))
                {
                    throw new InvalidOperationException(
                        $"GLSLEffect: could not resolve #include \"{path}\" (requested from {identity}). " +
                        "Register the containing directory with GLSLEffect.Includes.AddDirectory(...), or the " +
                        "assembly it's embedded in with GLSLEffect.Includes.AddEmbeddedResources(...).");
                }

                if (seen.Contains(includedIdentity))
                    return "// (GLSLEffect: skipped already-included " + path + ")";

                return Expand(includedSource, includedIdentity, resolver, seen);
            });
        }
    }
}
