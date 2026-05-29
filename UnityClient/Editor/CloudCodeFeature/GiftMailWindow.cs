using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Editor
{
    /// <summary>
    /// Editor window for sending player-to-player gift mail.
    /// MenuItem: Tool/CloudCodeFeature/Gift Mail
    ///
    /// GiftMail restrictions (§5.3):
    ///   - Sender must not equal target player (no self-gift)
    ///   - senderType is forced to Player; mailCategory is forced to Gift (server-side)
    ///   - No attachment items in this iteration (notification only)
    ///   - Max 5 gifts per sender per 24-hour window (server-enforced)
    /// </summary>
    public class GiftMailWindow : EditorWindow
    {
        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private string _targetPlayerId = string.Empty;
        private string _subject = string.Empty;
        private string _body = string.Empty;

        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _rawJson = string.Empty;
        private bool _showRawJson;

        // -----------------------------------------------------------------------
        // MenuItem
        // -----------------------------------------------------------------------

        [MenuItem("Tool/CloudCodeFeature/Gift Mail")]
        public static void Open()
        {
            var window = GetWindow<GiftMailWindow>("Gift Mail");
            window.minSize = new Vector2(420, 320);
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
            DrawGiftMailForm();
            EditorGUILayout.Space(4);
            DrawRawJsonFoldout();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Gift Mail — Player to Player", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Sends a notification gift mail. No item attachment (notification only in this iteration).",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(
                "Restrictions: sender != target | max 5 gifts per 24 h (server-enforced) | " +
                "mailCategory=Gift and senderType=Player are set server-side.",
                MessageType.Info);
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

        private void DrawGiftMailForm()
        {
            _targetPlayerId = EditorGUILayout.TextField("Target Player ID", _targetPlayerId);
            _subject        = EditorGUILayout.TextField("Subject (1-128 chars)", _subject);
            EditorGUILayout.LabelField("Body (1-1024 chars)");
            _body           = EditorGUILayout.TextArea(_body, GUILayout.MinHeight(60));

            EditorGUILayout.Space(6);

            bool formValid = !string.IsNullOrWhiteSpace(_targetPlayerId)
                && !string.IsNullOrWhiteSpace(_subject)
                && !string.IsNullOrWhiteSpace(_body);

            GUI.enabled = !_isBusy && IsSignedIn() && formValid;
            if (GUILayout.Button("Send Gift Mail", GUILayout.Height(28)))
                RunAsync(SendGiftMailAsync);
            GUI.enabled = true;

            if (!formValid && IsSignedIn())
                EditorGUILayout.HelpBox("Target Player ID, Subject, and Body are all required.", MessageType.Warning);
        }

        private void DrawRawJsonFoldout()
        {
            _showRawJson = EditorGUILayout.Foldout(_showRawJson, "Server Response JSON (debug)", true);
            if (_showRawJson && !string.IsNullOrEmpty(_rawJson))
            {
                EditorGUILayout.TextArea(_rawJson, EditorStyles.textArea, GUILayout.MinHeight(60));
            }
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            if (_isBusy)
                EditorGUILayout.LabelField("Working...", EditorStyles.miniLabel);
        }

        // -----------------------------------------------------------------------
        // Async operations
        // -----------------------------------------------------------------------

        private async Task SendGiftMailAsync()
        {
            string targetId = _targetPlayerId.Trim();
            string subject  = _subject.Trim();
            string body     = _body.Trim();

            if (string.IsNullOrEmpty(targetId))
                throw new ArgumentException("Target Player ID is required.");
            if (subject.Length < 1 || subject.Length > 128)
                throw new ArgumentException("Subject must be 1-128 characters.");
            if (body.Length < 1 || body.Length > 1024)
                throw new ArgumentException("Body must be 1-1024 characters.");

            await BackpackCloudCodeService.InitializeAsync();

            // Self-gift guard (client-side early rejection mirrors server rule)
            if (IsSignedIn() && targetId == AuthenticationService.Instance.PlayerId)
                throw new ArgumentException("Cannot send a gift to yourself (server will also reject this).");

            var result = await BackpackCloudCodeService.CallUserSendGiftMailAsync(targetId, subject, body);
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"GiftMail: mailId={result.mailId} sentAt={result.sentAt}";
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool IsSignedIn()
        {
            try { return AuthenticationService.Instance.IsSignedIn; }
            catch { return false; }
        }

        private void RunAsync(Func<Task> action)
        {
            _isBusy = true;
            _statusMessage = "Working...";
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
                _statusMessage = ex is CloudCodeApiException apiEx
                    ? $"Error: HTTP {apiEx.StatusCode} {apiEx.ErrorCode}"
                    : $"Error: {ex.Message}";
                _rawJson = ex.ToString();
                Debug.LogError("[GiftMailWindow] " + ex.Message);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }
    }
}

