using System;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Editor
{
    /// <summary>
    /// Editor window for triggering QA smoke tests.
    /// MenuItem: Tool/CloudCodeFeature/QA Smoke
    ///
    /// Invokes BackpackAdventures.CloudCode.Client.Tests.MailboxTestRunner.RunAllAsync (and
    /// similar entry points) via reflection. If the class is absent at compile time the buttons
    /// are shown in a disabled state with an explanatory message — the window does not block
    /// compilation.
    ///
    /// The QA agent creates MailboxTestRunner under
    /// Assets/UnityCloudCode/UnityClient/Tests/PlayMode/MailboxIntegrationTests.cs.
    /// </summary>
    public class MailboxQAWindow : EditorWindow
    {
        // -----------------------------------------------------------------------
        // Constants
        // -----------------------------------------------------------------------

        private const string RunnerTypeName =
            "BackpackAdventures.CloudCode.Client.Tests.MailboxTestRunner";

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _outputLog = string.Empty;
        private Vector2 _logScroll;

        // -----------------------------------------------------------------------
        // MenuItem
        // -----------------------------------------------------------------------

        [MenuItem("Tool/CloudCodeFeature/QA Smoke")]
        public static void Open()
        {
            var window = GetWindow<MailboxQAWindow>("QA Smoke");
            window.minSize = new Vector2(440, 360);
            window.Show();
        }

        // -----------------------------------------------------------------------
        // GUI
        // -----------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();
            DrawSignInStatus();
            EditorGUILayout.Space(4);
            DrawRunnerAvailability();
            EditorGUILayout.Space(4);
            DrawButtons();
            EditorGUILayout.Space(4);
            DrawOutputLog();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("QA Smoke Tests", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Drives MailboxTestRunner entry points created by the QA agent.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSignInStatus()
        {
            bool signedIn = IsSignedIn();
            string label = signedIn
                ? $"Signed in as: {AuthenticationService.Instance.PlayerId}"
                : "Not signed in.";
            Color prev = GUI.color;
            GUI.color = signedIn ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            GUI.color = prev;
        }

        private void DrawRunnerAvailability()
        {
            bool available = FindRunnerType() != null;
            if (available)
            {
                EditorGUILayout.HelpBox(
                    $"{RunnerTypeName} found. Buttons enabled.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"{RunnerTypeName} not found in loaded assemblies.\n" +
                    "The QA agent must create MailboxTestRunner before these buttons are active.\n" +
                    "This window compiles cleanly without it.",
                    MessageType.Warning);
            }
        }

        private void DrawButtons()
        {
            bool runnerAvailable = FindRunnerType() != null;
            bool canRun = !_isBusy && IsSignedIn() && runnerAvailable;

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = canRun;
            if (GUILayout.Button("Run All", GUILayout.Height(28)))
                RunAsync(() => InvokeRunnerMethodAsync("RunAllAsync"));
            if (GUILayout.Button("Run Positive", GUILayout.Height(28)))
                RunAsync(() => InvokeRunnerMethodAsync("RunPositiveAsync"));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Negative", GUILayout.Height(28)))
                RunAsync(() => InvokeRunnerMethodAsync("RunNegativeAsync"));
            if (GUILayout.Button("Run Concurrency", GUILayout.Height(28)))
                RunAsync(() => InvokeRunnerMethodAsync("RunConcurrencyAsync"));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOutputLog()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll,
                GUILayout.ExpandHeight(true), GUILayout.MinHeight(120));
            if (!string.IsNullOrEmpty(_outputLog))
                EditorGUILayout.TextArea(_outputLog, EditorStyles.textArea,
                    GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Log", GUILayout.Width(80)))
            {
                _outputLog = string.Empty;
                _statusMessage = string.Empty;
            }
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            if (_isBusy)
                EditorGUILayout.LabelField("Running tests...", EditorStyles.miniLabel);
        }

        // -----------------------------------------------------------------------
        // Reflection-based runner invocation
        // -----------------------------------------------------------------------

        private async Task InvokeRunnerMethodAsync(string methodName)
        {
            await BackpackCloudCodeService.InitializeAsync();

            Type runnerType = FindRunnerType();
            if (runnerType == null)
                throw new InvalidOperationException(
                    $"{RunnerTypeName} not found. QA agent must create it first.");

            MethodInfo method = runnerType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);

            if (method == null)
                throw new InvalidOperationException(
                    $"Method {methodName} not found on {RunnerTypeName}.");

            object returnValue = method.Invoke(null, null);

            // If the method returns a Task (or Task<T>), await it.
            if (returnValue is Task task)
            {
                await task;
                _statusMessage = $"{methodName} completed.";
                _outputLog += $"[{DateTime.Now:HH:mm:ss}] {methodName} finished.\n";
            }
            else
            {
                _statusMessage = $"{methodName} invoked (non-async).";
                _outputLog += $"[{DateTime.Now:HH:mm:ss}] {methodName} invoked.\n";
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Searches all loaded assemblies for the MailboxTestRunner type.
        /// Returns null if not found — this is the safe fallback path.
        /// </summary>
        private static Type FindRunnerType()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(RunnerTypeName, throwOnError: false);
                    if (t != null) return t;
                }
                catch
                {
                    // Skip assemblies that cannot be reflected (e.g. dynamic assemblies).
                }
            }
            return null;
        }

        private static bool IsSignedIn()
        {
            try { return AuthenticationService.Instance.IsSignedIn; }
            catch { return false; }
        }

        private void RunAsync(Func<Task> action)
        {
            _isBusy = true;
            _statusMessage = "Running...";
            _ = ExecuteAsync(action);
        }

        private async Task ExecuteAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                _outputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                Debug.LogError("[MailboxQAWindow] " + ex.Message);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }
    }
}
