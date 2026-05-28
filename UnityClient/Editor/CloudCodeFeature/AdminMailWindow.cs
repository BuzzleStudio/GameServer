using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Editor
{
    /// <summary>
    /// Editor window for admin mail operations.
    /// MenuItem: Tool/CloudCodeFeature/Admin Mail
    ///
    /// Tabs:
    ///   - Send Global: broadcast mail to all players (admin-gated)
    ///   - Send User:   targeted user mail (admin-gated)
    ///   - Manage:      delete / expire / purge expired (admin-gated)
    ///
    /// IMPORTANT: Admin endpoints require your playerId to be present in the
    /// mailbox_admin_allowlist Cloud Save custom key. Add it via the UGS Dashboard
    /// (Cloud Save > Custom Data > mailbox_admin_allowlist > playerIds array).
    /// Until bootstrapped, all admin calls return 401 Unauthorized (fail-closed by design).
    /// </summary>
    public class AdminMailWindow : EditorWindow
    {
        // -----------------------------------------------------------------------
        // Tabs
        // -----------------------------------------------------------------------

        private enum Tab { SendGlobal, SendUser, Manage }
        private Tab _activeTab = Tab.SendGlobal;

        // -----------------------------------------------------------------------
        // Shared state
        // -----------------------------------------------------------------------

        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _rawJson = string.Empty;
        private bool _showRawJson;
        private Vector2 _scroll;

        // -----------------------------------------------------------------------
        // Send Global fields
        // -----------------------------------------------------------------------

        private string _globalSubject = string.Empty;
        private string _globalBody = string.Empty;
        private string _globalExpiresAt = string.Empty;   // ISO 8601 UTC, e.g. 2026-06-28T00:00:00Z
        private string _globalDedupKey = string.Empty;
        private string _globalSenderName = string.Empty;
        private int _globalCategoryIndex;
        private string _globalAttachmentsJson = "[]"; // JSON array of {itemId, type, quantity}

        // -----------------------------------------------------------------------
        // Send User fields
        // -----------------------------------------------------------------------

        private string _userTargetId = string.Empty;
        private string _userSubject = string.Empty;
        private string _userBody = string.Empty;
        private string _userExpiresAt = string.Empty;
        private string _userDedupKey = string.Empty;
        private string _userSenderName = string.Empty;
        private int _userCategoryIndex;
        private string _userAttachmentsJson = "[]";

        // -----------------------------------------------------------------------
        // Manage fields
        // -----------------------------------------------------------------------

        private string _manageMailId = string.Empty;

        // -----------------------------------------------------------------------
        // Dropdown options
        // -----------------------------------------------------------------------

        private static readonly string[] CategoryOptions =
        {
            "System", "Event", "Compensation", "Gift", "Support", "PatchNote"
        };

        private static readonly string[] SenderTypeOptions =
        {
            "System", "Admin", "Player"
        };

        // -----------------------------------------------------------------------
        // MenuItem
        // -----------------------------------------------------------------------

        [MenuItem("Tool/CloudCodeFeature/Admin Mail")]
        public static void Open()
        {
            var window = GetWindow<AdminMailWindow>("Admin Mail");
            window.minSize = new Vector2(500, 480);
            window.Show();
        }

        // -----------------------------------------------------------------------
        // GUI
        // -----------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();
            DrawAdminWarning();
            DrawSignInStatus();
            EditorGUILayout.Space(4);
            DrawTabs();
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            switch (_activeTab)
            {
                case Tab.SendGlobal: DrawSendGlobalTab(); break;
                case Tab.SendUser:   DrawSendUserTab();   break;
                case Tab.Manage:     DrawManageTab();     break;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            DrawRawJsonFoldout();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Admin Mail Tool", EditorStyles.boldLabel);
        }

        private void DrawAdminWarning()
        {
            EditorGUILayout.HelpBox(
                "Admin endpoints require your playerId in the mailbox_admin_allowlist Cloud Save custom key.\n" +
                "Bootstrap via: UGS Dashboard > Cloud Save > Custom Data > mailbox_admin_allowlist.\n" +
                "Until bootstrapped all admin calls return 401 Unauthorized (fail-closed by design).",
                MessageType.Warning);
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

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_activeTab == Tab.SendGlobal, "Send Global", EditorStyles.toolbarButton))
                _activeTab = Tab.SendGlobal;
            if (GUILayout.Toggle(_activeTab == Tab.SendUser, "Send User", EditorStyles.toolbarButton))
                _activeTab = Tab.SendUser;
            if (GUILayout.Toggle(_activeTab == Tab.Manage, "Manage", EditorStyles.toolbarButton))
                _activeTab = Tab.Manage;
            EditorGUILayout.EndHorizontal();
        }

        // -----------------------------------------------------------------------
        // Send Global tab
        // -----------------------------------------------------------------------

        private void DrawSendGlobalTab()
        {
            EditorGUILayout.LabelField("Send Global Mail (admin-gated)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _globalSubject    = EditorGUILayout.TextField("Subject (1-128 chars)", _globalSubject);
            EditorGUILayout.LabelField("Body (1-1024 chars)");
            _globalBody       = EditorGUILayout.TextArea(_globalBody, GUILayout.MinHeight(60));
            _globalExpiresAt  = EditorGUILayout.TextField("Expires At (ISO UTC, blank=never)", _globalExpiresAt);
            _globalCategoryIndex = EditorGUILayout.Popup("Category", _globalCategoryIndex, CategoryOptions);
            _globalSenderName = EditorGUILayout.TextField("Sender Name (e.g. GM_Ninh)", _globalSenderName);
            _globalDedupKey   = EditorGUILayout.TextField("Dedup Key (optional)", _globalDedupKey);

            EditorGUILayout.LabelField("Attachments JSON (array of {itemId,type,quantity})");
            _globalAttachmentsJson = EditorGUILayout.TextArea(_globalAttachmentsJson, GUILayout.MinHeight(50));

            EditorGUILayout.Space(4);
            GUI.enabled = !_isBusy && IsSignedIn();
            if (GUILayout.Button("Send Global Mail"))
                RunAsync(SendGlobalMailAsync);
            GUI.enabled = true;
        }

        // -----------------------------------------------------------------------
        // Send User tab
        // -----------------------------------------------------------------------

        private void DrawSendUserTab()
        {
            EditorGUILayout.LabelField("Send User Mail (admin-gated)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _userTargetId  = EditorGUILayout.TextField("Target Player ID", _userTargetId);
            _userSubject   = EditorGUILayout.TextField("Subject (1-128 chars)", _userSubject);
            EditorGUILayout.LabelField("Body (1-1024 chars)");
            _userBody      = EditorGUILayout.TextArea(_userBody, GUILayout.MinHeight(60));
            _userExpiresAt = EditorGUILayout.TextField("Expires At (ISO UTC, blank=never)", _userExpiresAt);
            _userCategoryIndex = EditorGUILayout.Popup("Category", _userCategoryIndex, CategoryOptions);
            _userSenderName = EditorGUILayout.TextField("Sender Name (e.g. GM_Ninh)", _userSenderName);
            _userDedupKey  = EditorGUILayout.TextField("Dedup Key (optional)", _userDedupKey);

            EditorGUILayout.LabelField("Attachments JSON (array of {itemId,type,quantity})");
            _userAttachmentsJson = EditorGUILayout.TextArea(_userAttachmentsJson, GUILayout.MinHeight(50));

            EditorGUILayout.Space(4);
            GUI.enabled = !_isBusy && IsSignedIn() && !string.IsNullOrWhiteSpace(_userTargetId);
            if (GUILayout.Button("Send User Mail"))
                RunAsync(SendUserMailAsync);
            GUI.enabled = true;
        }

        // -----------------------------------------------------------------------
        // Manage tab
        // -----------------------------------------------------------------------

        private void DrawManageTab()
        {
            EditorGUILayout.LabelField("Manage Mail (admin-gated)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _manageMailId = EditorGUILayout.TextField("Mail ID", _manageMailId);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isBusy && IsSignedIn() && !string.IsNullOrWhiteSpace(_manageMailId);
            if (GUILayout.Button("Delete Mail", GUILayout.Width(110)))
                RunAsync(DeleteMailAsync);
            if (GUILayout.Button("Expire Mail", GUILayout.Width(110)))
                RunAsync(ExpireMailAsync);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Purge All Expired Global Mails (admin)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "PurgeExpired removes all expired refs from global_mail_index_v2 and deletes their mail_global_{id} keys. " +
                "Run periodically for housekeeping.",
                MessageType.Info);
            GUI.enabled = !_isBusy && IsSignedIn();
            if (GUILayout.Button("Purge Expired", GUILayout.Width(120)))
                RunAsync(PurgeExpiredAsync);
            GUI.enabled = true;
        }

        // -----------------------------------------------------------------------
        // Async operations
        // -----------------------------------------------------------------------

        private async Task SendGlobalMailAsync()
        {
            ValidateSubjectBody(_globalSubject, _globalBody);
            await EnsureInitializedAsync();

            var attachments = ParseAttachmentsJson(_globalAttachmentsJson);
            string expiresAt = string.IsNullOrWhiteSpace(_globalExpiresAt) ? null : _globalExpiresAt.Trim();
            string category  = CategoryOptions[_globalCategoryIndex];
            string sender    = string.IsNullOrWhiteSpace(_globalSenderName) ? null : _globalSenderName.Trim();
            string dedupKey  = string.IsNullOrWhiteSpace(_globalDedupKey) ? null : _globalDedupKey.Trim();

            var result = await BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                _globalSubject.Trim(), _globalBody.Trim(),
                expiresAt, category, sender, dedupKey, attachments);

            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            string mailId = string.IsNullOrEmpty(result.mailId) ? result.globalMailId : result.mailId;
            _statusMessage = $"SendGlobalMail: success={result.success} mailId={mailId} sentAt={result.sentAt}";
        }

        private async Task SendUserMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_userTargetId))
                throw new ArgumentException("Target Player ID is required.");
            ValidateSubjectBody(_userSubject, _userBody);
            await EnsureInitializedAsync();

            var attachments = ParseAttachmentsJson(_userAttachmentsJson);
            string expiresAt = string.IsNullOrWhiteSpace(_userExpiresAt) ? null : _userExpiresAt.Trim();
            string category  = CategoryOptions[_userCategoryIndex];
            string sender    = string.IsNullOrWhiteSpace(_userSenderName) ? null : _userSenderName.Trim();
            string dedupKey  = string.IsNullOrWhiteSpace(_userDedupKey) ? null : _userDedupKey.Trim();

            var result = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                _userTargetId.Trim(), _userSubject.Trim(), _userBody.Trim(),
                expiresAt, category, sender, dedupKey, attachments);

            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"SendUserMail: success={result.success} mailId={result.mailId} sentAt={result.sentAt}";
        }

        private async Task DeleteMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Delete.");
            await EnsureInitializedAsync();
            var result = await BackpackCloudCodeService.CallDeleteMailAsync(_manageMailId.Trim());
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"DeleteMail: success={result.success} mailId={result.mailId}";
        }

        private async Task ExpireMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Expire.");
            await EnsureInitializedAsync();
            var result = await BackpackCloudCodeService.CallExpireMailAsync(_manageMailId.Trim());
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"ExpireMail: success={result.success} mailId={result.mailId}";
        }

        private async Task PurgeExpiredAsync()
        {
            await EnsureInitializedAsync();
            var result = await BackpackCloudCodeService.CallPurgeExpiredAsync();
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"PurgeExpired: success={result.success} purgedCount={result.purgedCount}";
        }

        // -----------------------------------------------------------------------
        // UI helpers
        // -----------------------------------------------------------------------

        private void DrawRawJsonFoldout()
        {
            _showRawJson = EditorGUILayout.Foldout(_showRawJson, "Server Response JSON (debug)", true);
            if (_showRawJson && !string.IsNullOrEmpty(_rawJson))
            {
                EditorGUILayout.TextArea(_rawJson, EditorStyles.textArea, GUILayout.MinHeight(80));
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
        // Utility
        // -----------------------------------------------------------------------

        private static void ValidateSubjectBody(string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 128)
                throw new ArgumentException("Subject must be 1-128 characters.");
            if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 1024)
                throw new ArgumentException("Body must be 1-1024 characters.");
        }

        /// <summary>
        /// Parses a JSON array string into a List of MailAttachment.
        /// Expected format: [{"itemId":"gold","type":"currency","quantity":100}, ...]
        /// Returns null (not empty list) when the JSON is "[]" or blank so the backend treats it as absent.
        /// </summary>
        private static List<MailAttachment> ParseAttachmentsJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            string trimmed = json.Trim();
            if (trimmed == "[]" || trimmed == "null") return null;

            // Unity's JsonUtility does not deserialize top-level arrays directly.
            // Wrap in an object to parse, then unwrap.
            try
            {
                string wrapped = "{\"items\":" + trimmed + "}";
                var wrapper = UnityEngine.JsonUtility.FromJson<AttachmentListWrapper>(wrapped);
                return (wrapper?.items?.Count > 0) ? wrapper.items : null;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Attachments JSON parse error: {ex.Message}. " +
                    "Expected format: [{\"itemId\":\"gold\",\"type\":\"currency\",\"quantity\":100}]");
            }
        }

        private static async Task EnsureInitializedAsync()
        {
            await BackpackCloudCodeService.InitializeAsync();
        }

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
                _statusMessage = $"Error: {ex.Message}";
                _rawJson = ex.ToString();
                Debug.LogError("[AdminMailWindow] " + ex.Message);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }

        // -----------------------------------------------------------------------
        // Private helper types
        // -----------------------------------------------------------------------

        [Serializable]
        private class AttachmentListWrapper
        {
            public List<MailAttachment> items;
        }
    }
}
