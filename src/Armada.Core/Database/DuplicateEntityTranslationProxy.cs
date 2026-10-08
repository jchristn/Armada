namespace Armada.Core.Database
{
    using System;
    using System.Collections.Concurrent;
    using System.Reflection;
    using System.Runtime.ExceptionServices;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Decorates a database method interface (ICaptainMethods, IFleetMethods, ...) so that a unique-constraint or
    /// primary-key violation raised by any provider during any call surfaces as a typed
    /// <see cref="DuplicateEntityException"/> instead of the raw provider exception (whose message names tables and
    /// columns). This is the race-safe net behind the user-facing checks in <see cref="DuplicateEntityGuard"/>: two
    /// concurrent creates both pass the check, and the loser's constraint violation is translated here.
    /// <see cref="DatabaseDriver"/> wraps every entity accessor with it, so each provider and every caller gets it.
    /// </summary>
    public class DuplicateEntityTranslationProxy : DispatchProxy
    {
        #region Private-Members

        private static readonly MethodInfo _WrapTypedTaskMethod = typeof(DuplicateEntityTranslationProxy)
            .GetMethod(nameof(WrapTypedTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly ConcurrentDictionary<Type, MethodInfo> _WrapTypedTaskByResult = new ConcurrentDictionary<Type, MethodInfo>();

        private object _Target = null!;
        private string _EntityType = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. Used by <see cref="DispatchProxy"/>; call <see cref="Wrap{T}(T)"/> instead.
        /// </summary>
        public DuplicateEntityTranslationProxy()
        {
        }

        /// <summary>
        /// Wrap a database method implementation. Null is returned as null, and an instance that is already wrapped is
        /// returned unchanged.
        /// </summary>
        /// <typeparam name="T">Database method interface.</typeparam>
        /// <param name="target">Implementation.</param>
        /// <returns>The wrapped implementation.</returns>
        public static T Wrap<T>(T target) where T : class
        {
            if (target == null) return null!;
            if (target is DuplicateEntityTranslationProxy) return target;
            if (!typeof(T).IsInterface) return target;

            T proxy = Create<T, DuplicateEntityTranslationProxy>();
            DuplicateEntityTranslationProxy state = (DuplicateEntityTranslationProxy)(object)proxy;
            state._Target = target;
            state._EntityType = UniqueConstraintViolation.EntityTypeFor(typeof(T));
            return proxy;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The wrapped implementation, for tests and diagnostics.
        /// </summary>
        /// <returns>Implementation.</returns>
        public object GetTarget()
        {
            return _Target;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Forward a call to the wrapped implementation and translate a unique-constraint violation, whether thrown
        /// synchronously or by the returned task.
        /// </summary>
        /// <param name="targetMethod">Interface method.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>The implementation's result (a translating task for Task-returning methods).</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));

            object? result;
            try
            {
                result = targetMethod.Invoke(_Target, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                DuplicateEntityException? duplicate = UniqueConstraintViolation.Translate(tie.InnerException, _EntityType);
                if (duplicate != null) throw duplicate;
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }

            if (result is Task task)
            {
                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(Task)) return WrapTaskAsync(task, _EntityType);
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    Type resultType = returnType.GetGenericArguments()[0];
                    MethodInfo wrap = _WrapTypedTaskByResult.GetOrAdd(resultType, t => _WrapTypedTaskMethod.MakeGenericMethod(t));
                    return wrap.Invoke(null, new object[] { task, _EntityType });
                }
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task WrapTaskAsync(Task task, string entityType)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DuplicateEntityException? duplicate = UniqueConstraintViolation.Translate(ex, entityType);
                if (duplicate != null && !ReferenceEquals(duplicate, ex)) throw duplicate;
                throw;
            }
        }

        private static async Task<TResult> WrapTypedTaskAsync<TResult>(Task<TResult> task, string entityType)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DuplicateEntityException? duplicate = UniqueConstraintViolation.Translate(ex, entityType);
                if (duplicate != null && !ReferenceEquals(duplicate, ex)) throw duplicate;
                throw;
            }
        }

        #endregion
    }
}
