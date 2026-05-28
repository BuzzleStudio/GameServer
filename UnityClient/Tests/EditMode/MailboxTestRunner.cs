// MailboxTestRunner.cs
// Programmatic entry point for the mailbox test suite.
// Called by: MailboxAdminToolWindow (Client Tooling agent's Editor window)
//            DevOps post-deploy hook (via Unity Test Runner CLI)
//
// Namespace: BackpackAdventures.CloudCode.Client.Tests
// This exact namespace is required by the Editor window reference.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    /// <summary>
    /// Static programmatic runner for the mailbox NUnit test suite.
    /// Provides RunAllAsync, RunPositiveAsync, RunNegativeAsync, RunConcurrencyAsync,
    /// and RunReliabilityAsync overloads.
    ///
    /// Usage from the Editor window:
    ///   await MailboxTestRunner.RunAllAsync();
    ///
    /// Usage from DevOps CI (via Unity Test Runner CLI):
    ///   See Assets/UnityCloudCode/docs/TEST_RUNNER.md
    /// </summary>
    public static class MailboxTestRunner
    {
        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>Runs all mailbox test suites sequentially and returns the aggregated result.</summary>
        public static async Task<MailboxTestRunResult> RunAllAsync()
        {
            var result = new MailboxTestRunResult("All");
            result.Merge(await RunPositiveAsync());
            result.Merge(await RunNegativeAsync());
            result.Merge(await RunConcurrencyAsync());
            result.Merge(await RunReliabilityAsync());
            return result;
        }

        /// <summary>Runs all [Category("Positive")] tests.</summary>
        public static async Task<MailboxTestRunResult> RunPositiveAsync()
        {
            return await RunSuiteAsync<MailboxApiPositiveTests>("Positive");
        }

        /// <summary>Runs all [Category("Negative")] tests.</summary>
        public static async Task<MailboxTestRunResult> RunNegativeAsync()
        {
            return await RunSuiteAsync<MailboxApiNegativeTests>("Negative");
        }

        /// <summary>Runs all [Category("Concurrency")] tests.</summary>
        public static async Task<MailboxTestRunResult> RunConcurrencyAsync()
        {
            return await RunSuiteAsync<MailboxApiConcurrencyTests>("Concurrency");
        }

        /// <summary>
        /// Runs all [Category("Reliability")] tests.
        /// Tests marked [Explicit] are SKIPPED — they require a dedicated test environment.
        /// </summary>
        public static async Task<MailboxTestRunResult> RunReliabilityAsync()
        {
            return await RunSuiteAsync<MailboxApiReliabilityTests>("Reliability", skipExplicit: true);
        }

        // -----------------------------------------------------------------------
        // Internal runner
        // -----------------------------------------------------------------------

        private static async Task<MailboxTestRunResult> RunSuiteAsync<TSuite>(
            string suiteName,
            bool skipExplicit = false)
            where TSuite : class, new()
        {
            var result = new MailboxTestRunResult(suiteName);
            var suiteType = typeof(TSuite);
            var instance = new TSuite();

            var methods = suiteType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.Name)
                .ToList();

            foreach (var method in methods)
            {
                // Skip [Explicit] tests unless the caller opts in
                if (skipExplicit && method.GetCustomAttribute<ExplicitAttribute>() != null)
                {
                    result.AddSkipped(method.Name, "Explicit test — requires dedicated environment");
                    continue;
                }

                // Run [SetUp]
                var setUp = suiteType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);

                try
                {
                    if (setUp != null)
                    {
                        var setUpResult = setUp.Invoke(instance, null);
                        if (setUpResult is Task setUpTask)
                            await setUpTask;
                    }
                }
                catch (Exception setupEx)
                {
                    result.AddFailed(method.Name, $"[SetUp failed] {setupEx.Message}");
                    continue;
                }

                // Run test
                try
                {
                    var testResult = method.Invoke(instance, null);
                    if (testResult is Task testTask)
                        await testTask;
                    result.AddPassed(method.Name);
                    Debug.Log($"[MailboxTestRunner] PASS — {suiteName}/{method.Name}");
                }
                catch (InconclusiveException incEx)
                {
                    result.AddSkipped(method.Name, incEx.Message);
                    Debug.LogWarning($"[MailboxTestRunner] SKIP — {suiteName}/{method.Name}: {incEx.Message}");
                }
                catch (SuccessException)
                {
                    result.AddPassed(method.Name);
                    Debug.Log($"[MailboxTestRunner] PASS (Assert.Pass) — {suiteName}/{method.Name}");
                }
                catch (AssertionException assertEx)
                {
                    result.AddFailed(method.Name, assertEx.Message);
                    Debug.LogError($"[MailboxTestRunner] FAIL — {suiteName}/{method.Name}: {assertEx.Message}");
                }
                catch (TargetInvocationException tiEx) when (tiEx.InnerException is AssertionException aEx)
                {
                    result.AddFailed(method.Name, aEx.Message);
                    Debug.LogError($"[MailboxTestRunner] FAIL — {suiteName}/{method.Name}: {aEx.Message}");
                }
                catch (TargetInvocationException tiEx) when (tiEx.InnerException is InconclusiveException incEx2)
                {
                    result.AddSkipped(method.Name, incEx2.Message);
                    Debug.LogWarning($"[MailboxTestRunner] SKIP — {suiteName}/{method.Name}: {incEx2.Message}");
                }
                catch (Exception ex)
                {
                    string innerMsg = ex.InnerException?.Message ?? ex.Message;
                    result.AddFailed(method.Name, $"Unhandled exception: {ex.GetType().Name} — {innerMsg}");
                    Debug.LogError(
                        $"[MailboxTestRunner] FAIL (exception) — {suiteName}/{method.Name}: {innerMsg}");
                }
                finally
                {
                    // Run [TearDown] regardless of test outcome
                    var tearDown = suiteType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(m => m.GetCustomAttribute<TearDownAttribute>() != null);
                    try
                    {
                        if (tearDown != null)
                        {
                            var tdResult = tearDown.Invoke(instance, null);
                            if (tdResult is Task tdTask)
                                await tdTask;
                        }
                    }
                    catch (Exception tearDownEx)
                    {
                        Debug.LogWarning(
                            $"[MailboxTestRunner] TearDown warning — {method.Name}: {tearDownEx.Message}");
                    }
                }
            }

            Debug.Log(
                $"[MailboxTestRunner] Suite '{suiteName}' complete — " +
                $"Passed={result.Passed} Failed={result.Failed} Skipped={result.Skipped}");

            return result;
        }
    }

    // -----------------------------------------------------------------------
    // Result model
    // -----------------------------------------------------------------------

    /// <summary>
    /// Aggregated result from one or more test suite runs.
    /// Surfaced by MailboxAdminToolWindow and serialized for CI.
    /// </summary>
    public class MailboxTestRunResult
    {
        public string SuiteName { get; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }
        public bool AllPassed => Failed == 0;

        public List<string> PassedTests { get; } = new List<string>();
        public List<(string Name, string Reason)> FailedTests { get; } = new List<(string, string)>();
        public List<(string Name, string Reason)> SkippedTests { get; } = new List<(string, string)>();

        public MailboxTestRunResult(string suiteName)
        {
            SuiteName = suiteName;
        }

        public void AddPassed(string name) { Passed++; PassedTests.Add(name); }
        public void AddFailed(string name, string reason) { Failed++; FailedTests.Add((name, reason)); }
        public void AddSkipped(string name, string reason) { Skipped++; SkippedTests.Add((name, reason)); }

        public void Merge(MailboxTestRunResult other)
        {
            Passed += other.Passed;
            Failed += other.Failed;
            Skipped += other.Skipped;
            PassedTests.AddRange(other.PassedTests);
            FailedTests.AddRange(other.FailedTests);
            SkippedTests.AddRange(other.SkippedTests);
        }

        public string ToSummary()
        {
            return $"[{SuiteName}] Passed={Passed} Failed={Failed} Skipped={Skipped} " +
                   $"| AllPassed={AllPassed}";
        }

        public string ToDetailedReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Mailbox Test Run: {SuiteName} ===");
            sb.AppendLine($"Passed: {Passed}  Failed: {Failed}  Skipped: {Skipped}");

            if (FailedTests.Count > 0)
            {
                sb.AppendLine("\n--- FAILED ---");
                foreach (var (name, reason) in FailedTests)
                    sb.AppendLine($"  FAIL  {name}: {reason}");
            }

            if (SkippedTests.Count > 0)
            {
                sb.AppendLine("\n--- SKIPPED ---");
                foreach (var (name, reason) in SkippedTests)
                    sb.AppendLine($"  SKIP  {name}: {reason}");
            }

            if (PassedTests.Count > 0)
            {
                sb.AppendLine("\n--- PASSED ---");
                foreach (var name in PassedTests)
                    sb.AppendLine($"  PASS  {name}");
            }

            return sb.ToString();
        }
    }
}
