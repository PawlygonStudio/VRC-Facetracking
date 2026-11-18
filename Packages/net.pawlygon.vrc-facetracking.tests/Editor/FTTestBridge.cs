using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>
    /// Runs this assembly's tests without the Test Runner window, for the dev bridge in the test project. The run
    /// takes a few editor updates, so the summary is appended to the bridge's result file when it finishes, in the
    /// same format the bridge writes its own results, and the failures go to a file of their own.
    /// </summary>
    public static class FTTestBridge
    {
        const string AssemblyName = "Pawlygon.FaceTracking.Editor.Tests";

        public static void Run(string command, string resultFile, string failuresFile)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(api, command, resultFile, failuresFile));
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { AssemblyName } }));
        }

        sealed class Callbacks : ICallbacks
        {
            readonly TestRunnerApi api;
            readonly string command, resultFile, failuresFile;
            readonly List<string> failures = new List<string>();

            public Callbacks(TestRunnerApi api, string command, string resultFile, string failuresFile)
            {
                this.api = api;
                this.command = command;
                this.resultFile = resultFile;
                this.failuresFile = failuresFile;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren || result.TestStatus != TestStatus.Failed) return;
                failures.Add($"{result.Test.FullName}\n{result.Message}\n{result.StackTrace}".TrimEnd());
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                api.UnregisterCallbacks(this);

                File.WriteAllText(failuresFile, failures.Count == 0 ? "" : string.Join("\n\n", failures) + "\n");
                var summary = $"{result.PassCount} passed, {result.FailCount} failed, {result.SkipCount + result.InconclusiveCount} skipped" +
                              (failures.Count > 0 ? $" (see {failuresFile})" : "");
                File.AppendAllText(resultFile, $"{DateTime.Now:O} {command}: {summary}\n");
            }
        }
    }
}
