namespace Armada.Helm.Infrastructure
{
    using Spectre.Console.Cli;

    /// <summary>
    /// Spectre.Console type registrar backed by a simple service collection.
    /// Enables constructor injection of services into CLI commands.
    /// </summary>
    public sealed class TypeRegistrar : ITypeRegistrar
    {
        #region Private-Members

        private readonly Dictionary<Type, Type> _Registrations = new Dictionary<Type, Type>();
        private readonly Dictionary<Type, object> _Instances = new Dictionary<Type, object>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a service type with its implementation.
        /// </summary>
        public void Register(Type service, Type implementation)
        {
            _Registrations[service] = implementation;
        }

        /// <summary>
        /// Register a service type with a specific instance.
        /// </summary>
        public void RegisterInstance(Type service, object implementation)
        {
            _Instances[service] = implementation;
        }

        /// <summary>
        /// Register a lazy-initialized service.
        /// </summary>
        public void RegisterLazy(Type service, Func<object> factory)
        {
            _Instances[service] = new Lazy<object>(factory);
        }

        /// <summary>
        /// Build the type resolver.
        /// </summary>
        public ITypeResolver Build()
        {
            return new TypeResolver(_Registrations, _Instances);
        }

        #endregion
    }
}
