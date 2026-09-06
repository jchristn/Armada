namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="LocalHostProcessExecutor"/>: the Local-mode process seam must be a
    /// behaviour-preserving pass-through to <see cref="AgentRuntimeFactory"/>, returning the same runtime the
    /// factory would, so standalone captain launches are unchanged.
    /// </summary>
    public sealed class LocalHostProcessExecutorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.LocalHostProcessExecutor";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the LocalHostProcessExecutor suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("passes_through_by_enum", "CreateRuntime by enum matches the factory", TestTags.Positive, () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                LocalHostProcessExecutor executor = new LocalHostProcessExecutor(factory);

                IAgentRuntime viaFactory = factory.Create(AgentRuntimeEnum.ClaudeCode);
                IAgentRuntime viaExecutor = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                AssertNotNull(viaExecutor, "Expected the executor to return a runtime.");
                AssertEqual(viaFactory.GetType().FullName, viaExecutor.GetType().FullName);
            }));

            cases.Add(Case("passes_through_by_name_error", "CreateRuntime by name mirrors the factory's behavior", TestTags.Negative, () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                LocalHostProcessExecutor executor = new LocalHostProcessExecutor(factory);

                // The string overload resolves custom-registered runtimes; an unknown name throws. The
                // executor must throw the same way the factory does, proving a faithful pass-through.
                bool factoryThrew = Threw(() => factory.Create("no-such-runtime"));
                bool executorThrew = Threw(() => executor.CreateRuntime("no-such-runtime"));
                AssertTrue(factoryThrew, "Expected the factory to throw for an unknown runtime name.");
                AssertEqual(factoryThrew, executorThrew);
            }));

            cases.Add(Case("null_factory_throws", "Constructor rejects a null factory", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new LocalHostProcessExecutor(null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Local Host Process Executor",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static bool Threw(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static AgentRuntimeFactory CreateFactory()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new AgentRuntimeFactory(logging);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
