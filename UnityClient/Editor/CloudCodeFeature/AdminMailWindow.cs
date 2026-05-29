using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// IMPORTANT: This editor tool calls Cloud Code through the UGS REST API using a
    /// project-scoped Unity service account as transport. Admin authorization is still
    /// enforced server-side by ADMIN_SERVICE_TOKEN from Unity Secret Manager.
    /// </summary>
    public class AdminMailWindow : EditorWindow
    {
        private const string ModuleName = "BackpackAdventuresModule";
        private const string ProjectIdPrefKey = "BackpackAdventures.AdminMail.ProjectId";
        private const string EnvironmentIdPrefKey = "BackpackAdventures.AdminMail.EnvironmentId";
        private const string ServiceKeyIdPrefKey = "BackpackAdventures.AdminMail.ServiceKeyId";

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
        // REST/project-scoped service-account credentials
        // -----------------------------------------------------------------------

        private string _projectId = string.Empty;
        private string _environmentId = string.Empty;
        private string _serviceKeyId = string.Empty;
        private string _serviceSecret = string.Empty;

        // -----------------------------------------------------------------------
        // Admin metadata
        // -----------------------------------------------------------------------

        private string _adminToken  = string.Empty;
        private string _operatorId  = string.Empty;

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

        private void OnEnable()
        {
            _projectId = EditorPrefs.GetString(ProjectIdPrefKey, _projectId);
            _environmentId = EditorPrefs.GetString(EnvironmentIdPrefKey, _environmentId);
            _serviceKeyId = EditorPrefs.GetString(ServiceKeyIdPrefKey, _serviceKeyId);
        }

        // -----------------------------------------------------------------------
        // GUI
        // -----------------------------------------------------------------------

        private void OnGUI()
        {
            DrawHeader();
            DrawAdminWarning();
            DrawServiceAccountCredentials();
            DrawAdminCredentials();
            DrawRestStatus();
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
                "Admin calls use UGS REST with a project-scoped Unity service account for transport. " +
                "Authorization uses the ADMIN_SERVICE_TOKEN environment secret on the deployed Cloud Code module. " +
                "Project/Environment/Key ID can be saved locally; secrets are session-only.",
                MessageType.Warning);
        }

        private void DrawServiceAccountCredentials()
        {
            EditorGUILayout.LabelField("Project-Scoped Service Account REST", EditorStyles.boldLabel);
            _projectId = EditorGUILayout.TextField("Project ID", _projectId);
            _environmentId = EditorGUILayout.TextField("Environment ID", _environmentId);
            _serviceKeyId = EditorGUILayout.TextField("Project Service Key ID", _serviceKeyId);
            _serviceSecret = EditorGUILayout.PasswordField("Project Service Secret", _serviceSecret);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Project/Env/Key ID", GUILayout.Width(190)))
                SaveRestPrefs();
            if (GUILayout.Button("Clear Saved", GUILayout.Width(100)))
                ClearRestPrefs();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAdminCredentials()
        {
            EditorGUILayout.LabelField("Admin Metadata", EditorStyles.boldLabel);
            _adminToken = EditorGUILayout.PasswordField("Admin Token (ADMIN_SERVICE_TOKEN)", _adminToken);
            _operatorId = EditorGUILayout.TextField("Operator ID (email)", _operatorId);
        }

        private void DrawRestStatus()
        {
            bool ready = HasRestCredentials();
            string label = ready
                ? "Project-scoped REST transport ready. Play Mode is not required."
                : "Project-scoped REST transport config is incomplete.";
            Color prev = GUI.color;
            GUI.color = ready ? Color.green : Color.yellow;
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
            GUI.enabled = !_isBusy && HasRestCredentials() && HasAdminCredentials();
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
            GUI.enabled = !_isBusy && HasRestCredentials() && !string.IsNullOrWhiteSpace(_userTargetId) && HasAdminCredentials();
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
            GUI.enabled = false;
            if (GUILayout.Button("Delete Mail", GUILayout.Width(110)))
                RunAsync(DeleteMailAsync);
            GUI.enabled = !_isBusy && HasRestCredentials() && !string.IsNullOrWhiteSpace(_manageMailId) && HasAdminCredentials();
            if (GUILayout.Button("Expire Global", GUILayout.Width(110)))
                RunAsync(ExpireMailAsync);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Delete Mail is player-scoped and still requires a player-authenticated runtime call. " +
                "Service-account editor flow supports admin global operations only.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Purge All Expired Global Mails (admin)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "PurgeExpired removes all expired refs from global_mail_index_v2 and deletes their mail_global_{id} keys. " +
                "Run periodically for housekeeping.",
                MessageType.Info);
            GUI.enabled = !_isBusy && HasRestCredentials() && HasAdminCredentials();
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
            using var backendScope = UseRestBackend();

            var attachments = ParseAttachmentsJson(_globalAttachmentsJson);
            string expiresAt = string.IsNullOrWhiteSpace(_globalExpiresAt) ? null : _globalExpiresAt.Trim();
            string category  = CategoryOptions[_globalCategoryIndex];
            string sender    = string.IsNullOrWhiteSpace(_globalSenderName) ? null : _globalSenderName.Trim();
            string dedupKey  = string.IsNullOrWhiteSpace(_globalDedupKey) ? null : _globalDedupKey.Trim();

            var result = await BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                _globalSubject.Trim(), _globalBody.Trim(),
                expiresAt, category, sender, dedupKey, attachments,
                _adminToken, _operatorId);

            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            string mailId = string.IsNullOrEmpty(result.mailId) ? result.globalMailId : result.mailId;
            _statusMessage = $"SendGlobalMail: success={result.success} mailId={mailId} sentAt={result.sentAt}";
        }

        private async Task SendUserMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_userTargetId))
                throw new ArgumentException("Target Player ID is required.");
            ValidateSubjectBody(_userSubject, _userBody);
            using var backendScope = UseRestBackend();

            var attachments = ParseAttachmentsJson(_userAttachmentsJson);
            string expiresAt = string.IsNullOrWhiteSpace(_userExpiresAt) ? null : _userExpiresAt.Trim();
            string category  = CategoryOptions[_userCategoryIndex];
            string sender    = string.IsNullOrWhiteSpace(_userSenderName) ? null : _userSenderName.Trim();
            string dedupKey  = string.IsNullOrWhiteSpace(_userDedupKey) ? null : _userDedupKey.Trim();

            var result = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                _userTargetId.Trim(), _userSubject.Trim(), _userBody.Trim(),
                expiresAt, category, sender, dedupKey, attachments,
                _adminToken, _operatorId);

            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"SendUserMail: success={result.success} mailId={result.mailId} sentAt={result.sentAt}";
        }

        private async Task DeleteMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Delete.");
            using var backendScope = UseRestBackend();
            var result = await BackpackCloudCodeService.CallDeleteMailAsync(_manageMailId.Trim());
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"DeleteMail: success={result.success} mailId={result.mailId}";
        }

        private async Task ExpireMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Expire.");
            using var backendScope = UseRestBackend();
            var result = await BackpackCloudCodeService.CallExpireMailAsync(_manageMailId.Trim(), _adminToken, _operatorId);
            _rawJson = UnityEngine.JsonUtility.ToJson(result, true);
            _statusMessage = $"ExpireMail: success={result.success} mailId={result.mailId}";
        }

        private async Task PurgeExpiredAsync()
        {
            using var backendScope = UseRestBackend();
            var result = await BackpackCloudCodeService.CallPurgeExpiredAsync(_adminToken, _operatorId);
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

        private void SaveRestPrefs()
        {
            EditorPrefs.SetString(ProjectIdPrefKey, _projectId?.Trim() ?? string.Empty);
            EditorPrefs.SetString(EnvironmentIdPrefKey, _environmentId?.Trim() ?? string.Empty);
            EditorPrefs.SetString(ServiceKeyIdPrefKey, _serviceKeyId?.Trim() ?? string.Empty);
            _statusMessage = "Saved Project ID, Environment ID, and Service Key ID.";
        }

        private void ClearRestPrefs()
        {
            EditorPrefs.DeleteKey(ProjectIdPrefKey);
            EditorPrefs.DeleteKey(EnvironmentIdPrefKey);
            EditorPrefs.DeleteKey(ServiceKeyIdPrefKey);
            _projectId = string.Empty;
            _environmentId = string.Empty;
            _serviceKeyId = string.Empty;
            _statusMessage = "Cleared saved REST config.";
        }

        private bool HasRestCredentials()
        {
            return !string.IsNullOrWhiteSpace(_projectId) &&
                   !string.IsNullOrWhiteSpace(_environmentId) &&
                   !string.IsNullOrWhiteSpace(_serviceKeyId) &&
                   !string.IsNullOrWhiteSpace(_serviceSecret);
        }

        private bool HasAdminCredentials()
        {
            return !string.IsNullOrWhiteSpace(_operatorId) &&
                   !string.IsNullOrWhiteSpace(_adminToken);
        }

        private IDisposable UseRestBackend()
        {
            if (!HasRestCredentials())
                throw new InvalidOperationException("Project ID, Environment ID, Service Key ID, and Service Secret are required.");

            SaveRestPrefs();
            return new BackendScope(new EditorRestCloudCodeBackend(
                _projectId.Trim(),
                _environmentId.Trim(),
                _serviceKeyId.Trim(),
                _serviceSecret));
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

        private sealed class BackendScope : IDisposable
        {
            private readonly ICloudCodeBackend _previousBackend;

            public BackendScope(ICloudCodeBackend backend)
            {
                _previousBackend = BackpackCloudCodeService.Backend;
                BackpackCloudCodeService.Backend = backend;
            }

            public void Dispose()
            {
                BackpackCloudCodeService.Backend = _previousBackend;
            }
        }

        private sealed class EditorRestCloudCodeBackend : ICloudCodeBackend
        {
            private const string TokenExchangeUrl = "https://services.api.unity.com/auth/v1/token-exchange";
            private const string CloudCodeBaseUrl = "https://cloud-code.services.api.unity.com";
            private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            private readonly string _projectId;
            private readonly string _environmentId;
            private readonly string _serviceKeyId;
            private readonly string _serviceSecret;
            private string _accessToken;
            private DateTime _accessTokenExpiresAtUtc;

            public EditorRestCloudCodeBackend(string projectId, string environmentId, string serviceKeyId, string serviceSecret)
            {
                _projectId = projectId;
                _environmentId = environmentId;
                _serviceKeyId = serviceKeyId;
                _serviceSecret = serviceSecret;
            }

            public async Task<T> CallEndpointAsync<T>(string endpoint, object request)
            {
                string accessToken = await GetAccessTokenAsync();
                string url = $"{CloudCodeBaseUrl}/v1/projects/{Uri.EscapeDataString(_projectId)}/modules/{ModuleName}/{Uri.EscapeDataString(endpoint)}";
                var payload = new JObject
                {
                    ["params"] = request == null
                        ? new JObject()
                        : new JObject { ["request"] = JToken.FromObject(request) }
                };

                string responseJson = await PostJsonAsync(url, payload.ToString(Formatting.None), accessToken, useBearer: true);
                var response = JObject.Parse(responseJson);
                JToken output = response["output"] ?? response;
                return output.ToObject<T>();
            }

            private async Task<string> GetAccessTokenAsync()
            {
                if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiresAtUtc)
                    return _accessToken;

                string url = $"{TokenExchangeUrl}?projectId={Uri.EscapeDataString(_projectId)}&environmentId={Uri.EscapeDataString(_environmentId)}";
                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serviceKeyId}:{_serviceSecret}"));

                using var message = new HttpRequestMessage(HttpMethod.Post, url);
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                message.Content = new StringContent("{\"scopes\":[]}", Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await HttpClient.SendAsync(message);
                string responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Token exchange failed ({(int)response.StatusCode}): {responseJson}");

                var tokenResponse = JObject.Parse(responseJson);
                _accessToken = tokenResponse.Value<string>("accessToken");
                if (string.IsNullOrEmpty(_accessToken))
                    throw new InvalidOperationException("Token exchange response did not contain accessToken.");

                _accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                return _accessToken;
            }

            private static async Task<string> PostJsonAsync(string url, string json, string authToken, bool useBearer)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, url);
                message.Headers.Authorization = new AuthenticationHeaderValue(useBearer ? "Bearer" : "Basic", authToken);
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await HttpClient.SendAsync(message);
                string responseJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Cloud Code REST call failed ({(int)response.StatusCode}): {responseJson}");

                return responseJson;
            }
        }
    }
}
