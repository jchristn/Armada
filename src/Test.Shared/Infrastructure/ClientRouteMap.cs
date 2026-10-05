namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;

    /// <summary>
    /// Maps every public API method of <see cref="ArmadaClient"/> to the HTTP requests it sends, by invoking it with
    /// synthesized arguments against a <see cref="StubHttpHandler"/> that records each request. Used to compare the client
    /// with the server's route surface (docs/api-surface-1.0.json) and to list the methods the contract suites must cover.
    /// </summary>
    public static class ClientRouteMap
    {
        #region Public-Methods

        /// <summary>
        /// Public API methods of the client (every public instance method returning a Task, except
        /// <see cref="ArmadaClient.SendRawAsync"/>, which takes any path), ordered by name.
        /// </summary>
        /// <returns>Methods.</returns>
        public static List<MethodInfo> ApiMethods()
        {
            return typeof(ArmadaClient)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType) && m.Name != nameof(ArmadaClient.SendRawAsync))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Invoke one method with synthesized arguments and return the requests it sent ("METHOD /path", no query).
        /// </summary>
        /// <param name="method">Client method.</param>
        /// <returns>Requests, in order.</returns>
        public static async Task<List<string>> RequestsOfAsync(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            StubHttpHandler stub = new StubHttpHandler();
            using (ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub))
            {
                object?[] args = method.GetParameters().Select(Synthesize).ToArray();
                try
                {
                    Task task = (Task)method.Invoke(client, args)!;
                    await task.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The stub answers 404 for everything; only the requests matter here.
                }
            }

            List<string> requests = new List<string>();
            foreach (string request in stub.Requests)
            {
                int q = request.IndexOf('?');
                requests.Add(q >= 0 ? request.Substring(0, q) : request);
            }

            return requests;
        }

        /// <summary>
        /// Find the surface route ("METHOD /template") a request matches; the template with the most literal segments wins.
        /// </summary>
        /// <param name="request">Request ("METHOD /path").</param>
        /// <param name="routes">Surface routes ("METHOD /template").</param>
        /// <returns>The route, or null.</returns>
        public static string? Match(string request, IEnumerable<string> routes)
        {
            string[] parts = request.Split(' ', 2);
            if (parts.Length != 2) return null;
            string[] segments = parts[1].Trim('/').Split('/');
            string? best = null;
            int bestLiterals = -1;
            foreach (string route in routes)
            {
                string[] routeParts = route.Split(' ', 2);
                if (routeParts.Length != 2 || !String.Equals(routeParts[0], parts[0], StringComparison.OrdinalIgnoreCase)) continue;
                string[] template = routeParts[1].Trim('/').Split('/');
                if (template.Length != segments.Length) continue;
                int literals = 0;
                bool ok = true;
                for (int i = 0; i < template.Length; i++)
                {
                    if (template[i].StartsWith("{", StringComparison.Ordinal) && template[i].EndsWith("}", StringComparison.Ordinal)) continue;
                    if (!String.Equals(template[i], segments[i], StringComparison.Ordinal))
                    {
                        ok = false;
                        break;
                    }

                    literals++;
                }

                if (ok && literals > bestLiterals)
                {
                    best = route;
                    bestLiterals = literals;
                }
            }

            return best;
        }

        #endregion

        #region Private-Methods

        private static object? Synthesize(ParameterInfo parameter)
        {
            Type type = parameter.ParameterType;
            if (type == typeof(CancellationToken)) return CancellationToken.None;
            if (type == typeof(string))
            {
                if (parameter.Name == "type") return "fleets";
                if (parameter.HasDefaultValue) return parameter.DefaultValue;
                return "p_" + parameter.Name;
            }

            if (parameter.HasDefaultValue && !type.IsValueType) return parameter.DefaultValue;
            if (parameter.HasDefaultValue) return parameter.DefaultValue ?? Activator.CreateInstance(type);
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;
            if (type == typeof(int)) return 1;
            if (type == typeof(long)) return 1L;
            if (type == typeof(bool)) return false;
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            if (type == typeof(byte[])) return new byte[] { 1 };
            if (type == typeof(ArmadaRawJson)) return new ArmadaRawJson("{}");
            if (type == typeof(List<string>)) return new List<string> { "p_item" };
            if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType) return Activator.CreateInstance(type);
            if (type.GetConstructor(Type.EmptyTypes) != null) return Activator.CreateInstance(type);
            return null;
        }

        #endregion
    }
}
