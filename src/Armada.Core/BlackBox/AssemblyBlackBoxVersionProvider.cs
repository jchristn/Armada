namespace Armada.Core.BlackBox
{
    using System;
    using System.Reflection;

    /// <summary>
    /// Reads the build identity from an assembly's informational version (the part after '+' is the commit). Defaults to
    /// the entry assembly, then Armada.Core. Thread-safe.
    /// </summary>
    public class AssemblyBlackBoxVersionProvider : IBlackBoxVersionProvider
    {
        private readonly string _Version;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="assembly">Assembly to read, or null for the entry assembly (then Armada.Core).</param>
        public AssemblyBlackBoxVersionProvider(Assembly? assembly = null)
        {
            Assembly source = assembly ?? Assembly.GetEntryAssembly() ?? typeof(AssemblyBlackBoxVersionProvider).Assembly;
            _Version = source.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? source.GetName().Version?.ToString()
                ?? "0.0.0";
        }

        /// <inheritdoc />
        public string Version => _Version;

        /// <inheritdoc />
        public string? Commit => _Version.Contains('+') ? _Version.Split('+', 2)[1] : null;
    }
}
