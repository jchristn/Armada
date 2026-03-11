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

    /// <summary>
    /// Spectre.Console type resolver backed by the registrar's service collection.
    /// </summary>
    public sealed class TypeResolver : ITypeResolver
    {
        #region Private-Members

        private readonly Dictionary<Type, Type> _Registrations;
        private readonly Dictionary<Type, object> _Instances;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a type resolver from the registrar's service collection.
        /// </summary>
        internal TypeResolver(Dictionary<Type, Type> registrations, Dictionary<Type, object> instances)
        {
            _Registrations = registrations;
            _Instances = instances;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a type instance. Checks instances first, then registrations, then falls back to Activator.
        /// </summary>
        public object? Resolve(Type? type)
        {
            if (type == null) return null;

            // Check for registered instances
            if (_Instances.TryGetValue(type, out object? instance))
            {
                if (instance is Lazy<object> lazy) return lazy.Value;
                return instance;
            }

            // Check for type registrations
            Type? implementationType = type;
            if (_Registrations.TryGetValue(type, out Type? registered))
            {
                implementationType = registered;
            }

            // Try to create with constructor injection
            System.Reflection.ConstructorInfo[] constructors = implementationType.GetConstructors();
            if (constructors.Length > 0)
            {
                // Use the constructor with the most parameters we can satisfy
                foreach (System.Reflection.ConstructorInfo ctor in constructors.OrderByDescending(c => c.GetParameters().Length))
                {
                    System.Reflection.ParameterInfo[] parameters = ctor.GetParameters();
                    object?[] args = new object?[parameters.Length];
                    bool canResolve = true;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        object? arg = Resolve(parameters[i].ParameterType);
                        if (arg == null && !parameters[i].HasDefaultValue)
                        {
                            canResolve = false;
                            break;
                        }
                        args[i] = arg ?? parameters[i].DefaultValue;
                    }

                    if (canResolve)
                    {
                        return ctor.Invoke(args);
                    }
                }
            }

            // Handle IEnumerable<T> requests (Spectre.Console.Cli resolves these internally)
            if (implementationType.IsGenericType
                && implementationType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type elementType = implementationType.GetGenericArguments()[0];
                Array empty = Array.CreateInstance(elementType, 0);
                return empty;
            }

            // Fallback to parameterless activation
            return Activator.CreateInstance(implementationType);
        }

        #endregion
    }
}
