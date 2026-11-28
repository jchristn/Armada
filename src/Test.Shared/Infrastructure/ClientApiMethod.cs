namespace Test.Shared.Infrastructure
{
    using System;
    using System.Reflection;
    using Armada.Client;

    /// <summary>
    /// One public API method of an <see cref="ArmadaClient"/> sub-client (for example <c>Fleets.ListAsync</c>).
    /// </summary>
    public sealed class ClientApiMethod
    {
        #region Public-Members

        /// <summary>
        /// The <see cref="ArmadaClient"/> property that holds the sub-client.
        /// </summary>
        public PropertyInfo Area { get; }

        /// <summary>
        /// The sub-client method.
        /// </summary>
        public MethodInfo Method { get; }

        /// <summary>
        /// Qualified name, <c>Area.Method</c>, as callers write it after <c>client.</c>.
        /// </summary>
        public string Name
        {
            get { return Area.Name + "." + Method.Name; }
        }

        /// <summary>
        /// The <c>api/client.ts</c> export this method mirrors, or null.
        /// </summary>
        public string? DashboardExport
        {
            get
            {
                DashboardExportAttribute? attr = Method.GetCustomAttribute<DashboardExportAttribute>();
                return attr?.Name;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="area">Sub-client property.</param>
        /// <param name="method">Sub-client method.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ClientApiMethod(PropertyInfo area, MethodInfo method)
        {
            Area = area ?? throw new ArgumentNullException(nameof(area));
            Method = method ?? throw new ArgumentNullException(nameof(method));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Invoke the method on a client's sub-client.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>The method's return value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
        public object? Invoke(ArmadaClient client, object?[] args)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            object? target = Area.GetValue(client);
            return Method.Invoke(target, args);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }

        #endregion
    }
}
