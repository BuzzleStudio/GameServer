using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Editor
{
    /// <summary>
    /// Editor window for browsing a player's mailbox.
    /// MenuItem: CloudCode/Mailbox
    /// Supports paginated fetch of user/global mails; per-mail Mark Read and Claim Attachment actions.
    /// </summary>
    public class MailboxWindow : EditorWindow
    {
        private enum MailboxScope { User, Global }
        private static readonly string[] MailboxScopeLabels = { "User Mails", "Global Mails" };
        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private int _page = 0;
        private int _pageSize = 20;
        private MailboxScope _mailboxScope = MailboxScope.User;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _rawJson = string.Empty;
        private bool _showRawJson;

        private GetMailboxPageResponse _lastResponse;
        private Vector2 _mailListScroll;

        // Per-mail fold state (keyed by index in _lastResponse.mails)
        private readonly Dictionary<int, bool> _mailFoldouts = new Dictionary<int, bool>();

        // -----------------------------------------------------------------------
        // MenuItem
        // -----------------------------------------------------------------------

        [MenuItem("CloudCode/Mailbox")]
        public static void Open()
        {
            var window = GetWindow<MailboxWindow>("Mailbox");
            window.minSize = new Vector2(480, 400);
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
            DrawPaginationControls();
            EditorGUILayout.Space(4);
            DrawMailList();
            EditorGUILayout.Space(4);
            DrawRawJsonFoldout();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            string scopeLabel = _mailboxScope == MailboxScope.Global ? "Global Mails" : "User Mails";
            string endpointLabel = _mailboxScope == MailboxScope.Global ? "GetGlobalMails" : "GetUserMails";
            EditorGUILayout.LabelField($"Mailbox - {scopeLabel}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Reads {endpointLabel} with pagination. Mark Read / Claim per mail.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(2);
        }

        private void DrawSignInStatus()
        {
            bool signedIn = IsSignedIn();
            string label = signedIn
                ? $"Signed in as: {AuthenticationService.Instance.PlayerId}"
                : "Not signed in. Call InitializeAsync first (HealthCheck window or play mode).";
            Color prev = GUI.color;
            GUI.color = signedIn ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            GUI.color = prev;
        }

        private void DrawPaginationControls()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scope", GUILayout.Width(42));
            EditorGUI.BeginChangeCheck();
            int selectedScope = EditorGUILayout.Popup((int)_mailboxScope, MailboxScopeLabels, GUILayout.Width(110));
            if (EditorGUI.EndChangeCheck())
            {
                _mailboxScope = (MailboxScope)selectedScope;
                _page = 0;
                _lastResponse = null;
                _rawJson = string.Empty;
                _mailFoldouts.Clear();
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Page", GUILayout.Width(36));
            _page = EditorGUILayout.IntField(_page, GUILayout.Width(50));
            EditorGUILayout.LabelField("Page Size", GUILayout.Width(64));
            _pageSize = EditorGUILayout.IntSlider(_pageSize, 1, 50, GUILayout.Width(160));
            EditorGUILayout.Space(8);
            GUI.enabled = !_isBusy && IsSignedIn();
            if (GUILayout.Button("Fetch Mailbox", GUILayout.Width(110)))
                RunAsync(FetchMailboxAsync);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_lastResponse != null)
            {
                EditorGUILayout.LabelField(
                    $"Showing page {_lastResponse.page}  |  {_lastResponse.mails?.Count ?? 0} mails" +
                    $"  |  Total: {_lastResponse.totalCount}  |  Has more: {_lastResponse.hasMore}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawMailList()
        {
            if (_lastResponse?.mails == null || _lastResponse.mails.Count == 0)
            {
                EditorGUILayout.HelpBox("No mails loaded. Select scope and click 'Fetch Mailbox' to retrieve.", MessageType.Info);
                return;
            }

            _mailListScroll = EditorGUILayout.BeginScrollView(_mailListScroll,
                GUILayout.ExpandHeight(true), GUILayout.MinHeight(200));

            for (int i = 0; i < _lastResponse.mails.Count; i++)
            {
                var mail = _lastResponse.mails[i];
                DrawMailEntry(i, mail);
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMailEntry(int index, MailItem mail)
        {
            if (!_mailFoldouts.ContainsKey(index))
                _mailFoldouts[index] = false;

            string foldLabel = $"[{mail.mailType ?? "?"}] {mail.subject ?? "(no subject)"}  |  " +
                               $"Scope: {GetCurrentMailOwnershipType()}  " +
                               $"From: {mail.sender ?? mail.senderType ?? "?"}  " +
                               $"  Sent: {FormatDate(mail.sentAt)}" +
                               $"  Read: {(mail.isRead ? "Yes" : "No")}" +
                               $"  Claimed: {(mail.attachmentClaimed ? "Yes" : "No")}";

            _mailFoldouts[index] = EditorGUILayout.Foldout(_mailFoldouts[index], foldLabel, true);
            if (!_mailFoldouts[index]) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Mail ID", mail.mailId ?? "-", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Body", mail.body ?? "-", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Expires At", mail.expiresAt ?? "never", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Category", mail.mailCategory ?? "-", EditorStyles.miniLabel);

            if (mail.attachments != null && mail.attachments.Count > 0)
            {
                EditorGUILayout.LabelField("Attachments:", EditorStyles.boldLabel);
                foreach (var att in mail.attachments)
                {
                    string itemId = string.IsNullOrEmpty(att.itemId) ? att.id : att.itemId;
                    int qty = att.quantity > 0 ? att.quantity : att.amount;
                    EditorGUILayout.LabelField($"  {att.type}: {itemId} x{qty}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isBusy && !mail.isRead;
            if (GUILayout.Button("Mark Read", GUILayout.Width(90)))
            {
                string mailId = mail.mailId;
                string ownershipType = GetCurrentMailOwnershipType();
                RunAsync(() => MarkReadAsync(mailId, ownershipType));
            }
            GUI.enabled = !_isBusy && !mail.attachmentClaimed
                && mail.attachments != null && mail.attachments.Count > 0;
            if (GUILayout.Button("Claim Attachment", GUILayout.Width(130)))
            {
                string mailId = mail.mailId;
                string ownershipType = GetCurrentMailOwnershipType();
                RunAsync(() => ClaimAttachmentAsync(mailId, ownershipType));
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
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
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            }
            if (_isBusy)
            {
                EditorGUILayout.LabelField("Working...", EditorStyles.miniLabel);
            }
        }

        // -----------------------------------------------------------------------
        // Async operations
        // -----------------------------------------------------------------------

        private async Task FetchMailboxAsync()
        {
            if (_pageSize > 50) { _pageSize = 50; }
            if (_page < 0) { _page = 0; }

            await EnsureInitializedAsync();
            _lastResponse = _mailboxScope == MailboxScope.Global
                ? await BackpackCloudCodeService.CallGetGlobalMailsAsync(_page, _pageSize)
                : await BackpackCloudCodeService.CallGetMailboxAsync(_page, _pageSize);
            _mailFoldouts.Clear();
            _rawJson = UnityEngine.JsonUtility.ToJson(_lastResponse, true);
            _statusMessage = $"Fetched {GetCurrentMailOwnershipType()} page {_lastResponse.page}. Total mails: {_lastResponse.totalCount}";
        }

        private async Task MarkReadAsync(string mailId, string mailType)
        {
            await EnsureInitializedAsync();
            var result = await BackpackCloudCodeService.CallMarkMailReadAsync(mailId, mailType);
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"MarkMailRead: isRead={result.isRead}";
            // Refresh the list after marking read
            await FetchMailboxAsync();
        }

        private async Task ClaimAttachmentAsync(string mailId, string mailType)
        {
            await EnsureInitializedAsync();
            string requestId = System.Guid.NewGuid().ToString();
            var result = await BackpackCloudCodeService.CallClaimAttachmentAsync(mailId, mailType, requestId);
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"ClaimAttachment: alreadyClaimed={result.alreadyClaimed}";
            await FetchMailboxAsync();
        }

        private static async Task EnsureInitializedAsync()
        {
            await BackpackCloudCodeService.InitializeAsync();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool IsSignedIn()
        {
            try { return AuthenticationService.Instance.IsSignedIn; }
            catch { return false; }
        }

        private static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "-";
            if (DateTime.TryParse(iso, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return iso;
        }

        private string GetCurrentMailOwnershipType()
        {
            return _mailboxScope == MailboxScope.Global ? "global" : "user";
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
                Debug.LogError("[MailboxWindow] " + ex.Message);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }
    }
}

