using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class AdminMailWindow : EditorWindow
    {
        private const string ModuleName = "BackpackAdventuresModule";
        private const string ProjectIdPrefKey = "BackpackAdventures.AdminMail.ProjectId";
        private const string EnvironmentIdPrefKey = "BackpackAdventures.AdminMail.EnvironmentId"; // legacy — kept for migration read only
        private const string EnvironmentNamePrefKey = "BackpackAdventures.AdminMail.EnvironmentName";
        private const string ServiceKeyIdPrefKey = "BackpackAdventures.AdminMail.ServiceKeyId";

        private enum Tab { SendGlobal, SendTargeted, Manage }
        private enum AssetTypeOption { Currency, Item }

        [Serializable]
        private sealed class ItemRow
        {
            public string blueprintId = string.Empty;
            public int currentLevel = 1;
            public Rarity rarity = Rarity.None;
            public int initialLevel = 1;
            public string fromSource = string.Empty;
        }

        [Serializable]
        private sealed class AttachmentDraft
        {
            public string payoutAssetId = string.Empty;
            public AssetTypeOption assetType;
            public int payoutAmount = 1;
            public float chance = 1f;
            public List<ItemRow> itemRows = new List<ItemRow> { new ItemRow() };
        }

        private Tab _activeTab = Tab.SendGlobal;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private string _rawJson = string.Empty;
        private bool _showRawJson = true;
        private bool _showGlobalAttachments;
        private bool _showTargetUserIds;
        private bool _showUserAttachments;
        private Vector2 _scroll;

        private string _projectId = string.Empty;
        private string _environmentName = string.Empty;
        private string _resolvedEnvironmentId = string.Empty;
        private string _resolvedForName = string.Empty;
        private string _serviceKeyId = string.Empty;
        private string _serviceSecret = string.Empty;
        private string _operatorId = string.Empty;

        private string _globalSubject = string.Empty;
        private string _globalBody = string.Empty;
        private bool _globalUseEndTime;
        private string _globalEndDate = string.Empty;
        private string _globalEndTime = string.Empty;
        private string _globalDedupKey = string.Empty;
        private string _globalSenderName = string.Empty;
        private int _globalCategoryIndex;
        private readonly List<AttachmentDraft> _globalAttachments = new List<AttachmentDraft>();

        private readonly List<string> _targetUserIds = new List<string>();
        private string _userSubject = string.Empty;
        private string _userBody = string.Empty;
        private bool _userUseEndTime;
        private string _userEndDate = string.Empty;
        private string _userEndTime = string.Empty;
        private string _userDedupKey = string.Empty;
        private string _userSenderName = string.Empty;
        private int _userCategoryIndex;
        private readonly List<AttachmentDraft> _userAttachments = new List<AttachmentDraft>();

        private string _manageMailId = string.Empty;
        private bool _manageUseEndTime;
        private string _manageEndDate = string.Empty;
        private string _manageEndTime = string.Empty;

        private static readonly string[] CategoryOptions =
        {
            "System", "Event", "Compensation", "Gift", "Support", "PatchNote"
        };

        [MenuItem("CloudCode/Admin Mail")]
        public static void Open()
        {
            var window = GetWindow<AdminMailWindow>("Admin Mail");
            window.minSize = new Vector2(560, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _projectId = EditorPrefs.GetString(ProjectIdPrefKey, _projectId);
            _environmentName = EditorPrefs.GetString(EnvironmentNamePrefKey, string.Empty);
            if (string.IsNullOrEmpty(_environmentName))
                _environmentName = EditorPrefs.GetString(EnvironmentIdPrefKey, string.Empty); // migrate from old GUID key
            _serviceKeyId = EditorPrefs.GetString(ServiceKeyIdPrefKey, _serviceKeyId);
            EnsureAttachmentDefaults();
        }

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
                case Tab.SendTargeted: DrawSendTargetedTab(); break;
                case Tab.Manage: DrawManageTab(); break;
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
                "Admin calls use UGS REST with project-scoped Unity service account. Project ID, Environment Name, and Key ID can be saved locally; service account secret is session-only. The environment name (e.g. \"production\") is resolved to its GUID via UGS Environments API before each call.",
                MessageType.Warning);
        }

        private void DrawServiceAccountCredentials()
        {
            EditorGUILayout.LabelField("Project-Scoped Service Account REST", EditorStyles.boldLabel);
            _projectId = EditorGUILayout.TextField("Project ID", _projectId);
            _environmentName = EditorGUILayout.TextField("Environment Name", _environmentName);
            if (!string.IsNullOrEmpty(_resolvedEnvironmentId) && _resolvedForName == _environmentName.Trim())
                EditorGUILayout.LabelField($"  → Resolved ID: {_resolvedEnvironmentId}", EditorStyles.miniLabel);
            _serviceKeyId = EditorGUILayout.TextField("Project Service Key ID", _serviceKeyId);
            _serviceSecret = EditorGUILayout.PasswordField("Project Service Secret", _serviceSecret);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Project/Env/Key", GUILayout.Width(190)))
                SaveRestPrefs();
            if (GUILayout.Button("Clear Saved", GUILayout.Width(100)))
                ClearRestPrefs();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAdminCredentials()
        {
            EditorGUILayout.LabelField("Admin Metadata", EditorStyles.boldLabel);
            _operatorId = EditorGUILayout.TextField("Operator ID (email)", _operatorId);
        }

        private void DrawRestStatus()
        {
            bool ready = HasRestCredentials();
            string label = ready
                ? "Project-scoped REST transport ready. Play Mode not required."
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
            if (GUILayout.Toggle(_activeTab == Tab.SendTargeted, "Send Targeted", EditorStyles.toolbarButton))
                _activeTab = Tab.SendTargeted;
            if (GUILayout.Toggle(_activeTab == Tab.Manage, "Manage", EditorStyles.toolbarButton))
                _activeTab = Tab.Manage;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSendGlobalTab()
        {
            EditorGUILayout.LabelField("Send Global Mail", EditorStyles.boldLabel);
            _globalSubject = EditorGUILayout.TextField("MailInfo.Title", _globalSubject);
            EditorGUILayout.LabelField("MailInfo.Content");
            _globalBody = EditorGUILayout.TextArea(_globalBody, GUILayout.MinHeight(60));
            DrawEndTimeEditor("MailInfo.EndTime", ref _globalUseEndTime, ref _globalEndDate, ref _globalEndTime);
            _globalCategoryIndex = EditorGUILayout.Popup("Category", _globalCategoryIndex, CategoryOptions);
            _globalSenderName = EditorGUILayout.TextField("Sender Name", _globalSenderName);
            _globalDedupKey = EditorGUILayout.TextField("Dedup Key", _globalDedupKey);
            DrawAttachmentEditor("MailInfo.Attachment", _globalAttachments, ref _showGlobalAttachments);

            EditorGUILayout.Space(4);
            GUI.enabled = !_isBusy && HasRestCredentials() && HasOperatorId();
            if (GUILayout.Button("Send Global Mail"))
                RunAsync(SendGlobalMailAsync);
            GUI.enabled = true;
        }

        private void DrawSendTargetedTab()
        {
            EditorGUILayout.LabelField("Send Targeted Admin Mail", EditorStyles.boldLabel);
            DrawTargetUserIdsEditor();
            _userSubject = EditorGUILayout.TextField("MailInfo.Title", _userSubject);
            EditorGUILayout.LabelField("MailInfo.Content");
            _userBody = EditorGUILayout.TextArea(_userBody, GUILayout.MinHeight(60));
            DrawEndTimeEditor("MailInfo.EndTime", ref _userUseEndTime, ref _userEndDate, ref _userEndTime);
            _userCategoryIndex = EditorGUILayout.Popup("Category", _userCategoryIndex, CategoryOptions);
            _userSenderName = EditorGUILayout.TextField("Sender Name", _userSenderName);
            _userDedupKey = EditorGUILayout.TextField("Dedup Key", _userDedupKey);
            DrawAttachmentEditor("MailInfo.Attachment", _userAttachments, ref _showUserAttachments);

            EditorGUILayout.Space(4);
            GUI.enabled = !_isBusy && HasRestCredentials() && HasTargetUserIds() && HasOperatorId();
            if (GUILayout.Button("Send Targeted Mail"))
                RunAsync(SendUserMailAsync);
            GUI.enabled = true;
        }

        private void DrawTargetUserIdsEditor()
        {
            EditorGUILayout.Space(4);
            _showTargetUserIds = EditorGUILayout.Foldout(_showTargetUserIds, $"TargetUserIds ({CountTargetUserIds()})", true);
            if (!_showTargetUserIds)
                return;

            EditorGUILayout.LabelField("Each row is one player ID. Empty rows are ignored.", EditorStyles.wordWrappedMiniLabel);

            int removeIndex = -1;
            for (int i = 0; i < _targetUserIds.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _targetUserIds[i] = EditorGUILayout.TextField($"User {i + 1}", _targetUserIds[i]);
                GUI.enabled = _targetUserIds.Count > 1;
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeIndex = i;
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
                _targetUserIds.RemoveAt(removeIndex);

            if (GUILayout.Button("Add User ID", GUILayout.Width(120)))
                _targetUserIds.Add(string.Empty);
        }

        private void DrawAttachmentEditor(string label, List<AttachmentDraft> attachments, ref bool isExpanded)
        {
            EditorGUILayout.Space(4);
            isExpanded = EditorGUILayout.Foldout(isExpanded, $"{label} ({CountAttachments(attachments)})", true);
            if (!isExpanded)
                return;

            EditorGUILayout.HelpBox("Currency: plain PayoutAssetId. Item: list of item rows serialized to JSON array in PayoutAssetId.", MessageType.Info);

            int removeIndex = -1;
            for (int i = 0; i < attachments.Count; i++)
            {
                var draft = attachments[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Attachment {i + 1}", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                draft.assetType = (AssetTypeOption)EditorGUILayout.EnumPopup("AssetType", draft.assetType);
                draft.payoutAmount = EditorGUILayout.IntField("PayoutAmount", draft.payoutAmount);
                draft.chance = EditorGUILayout.Slider("Chance", draft.chance, 0f, 1f);

                if (draft.assetType == AssetTypeOption.Currency)
                {
                    draft.payoutAssetId = EditorGUILayout.TextField("PayoutAssetId", draft.payoutAssetId);
                }
                else
                {
                    EditorGUILayout.LabelField("Item Rows (serialized to JSON array in PayoutAssetId)", EditorStyles.miniBoldLabel);
                    if (draft.itemRows == null) draft.itemRows = new List<ItemRow>();

                    int removeRow = -1;
                    for (int r = 0; r < draft.itemRows.Count; r++)
                    {
                        var row = draft.itemRows[r];
                        EditorGUILayout.BeginVertical("helpBox");
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"Item {r + 1}", EditorStyles.miniLabel, GUILayout.Width(50));
                        if (GUILayout.Button("✕", GUILayout.Width(24)))
                            removeRow = r;
                        EditorGUILayout.EndHorizontal();
                        row.blueprintId = EditorGUILayout.TextField("BlueprintId", row.blueprintId);
                        row.currentLevel = EditorGUILayout.IntField("CurrentLevel", row.currentLevel);
                        row.rarity = (Rarity)EditorGUILayout.EnumPopup("Rarity", row.rarity);
                        row.initialLevel = EditorGUILayout.IntField("InitialLevel", row.initialLevel);
                        row.fromSource = EditorGUILayout.TextField("FromSource", row.fromSource);
                        EditorGUILayout.EndVertical();
                    }

                    if (removeRow >= 0)
                        draft.itemRows.RemoveAt(removeRow);

                    if (GUILayout.Button("+ Add Item Row", GUILayout.Width(120)))
                        draft.itemRows.Add(new ItemRow());
                }

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
                attachments.RemoveAt(removeIndex);

            if (GUILayout.Button("Add Attachment", GUILayout.Width(120)))
                attachments.Add(new AttachmentDraft());
        }

        private void DrawManageTab()
        {
            EditorGUILayout.LabelField("Manage Mail", EditorStyles.boldLabel);
            _manageMailId = EditorGUILayout.TextField("Mail ID", _manageMailId);
            DrawEndTimeEditor("Set MailInfo.EndTime", ref _manageUseEndTime, ref _manageEndDate, ref _manageEndTime);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isBusy && HasRestCredentials() && !string.IsNullOrWhiteSpace(_manageMailId) && HasOperatorId();
            if (GUILayout.Button("Set EndTime", GUILayout.Width(110)))
                RunAsync(SetMailEndTimeAsync);
            if (GUILayout.Button("Expire Global", GUILayout.Width(110)))
                RunAsync(ExpireMailAsync);
            if (GUILayout.Button("Delete Global", GUILayout.Width(110)))
                RunAsync(DeleteMailAsync);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Manage actions use project-scoped admin REST. Delete Global removes the global index ref and mail_global_{mailId} payload.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Purge All Expired Global Mails", EditorStyles.boldLabel);
            GUI.enabled = !_isBusy && HasRestCredentials() && HasOperatorId();
            if (GUILayout.Button("Purge Expired", GUILayout.Width(120)))
                RunAsync(PurgeExpiredAsync);
            GUI.enabled = true;
        }

        private async Task SendGlobalMailAsync()
        {
            ValidateSubjectBody(_globalSubject, _globalBody);
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var result = await BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                _globalSubject.Trim(),
                _globalBody.Trim(),
                BuildEndTimeIso(_globalUseEndTime, _globalEndDate, _globalEndTime),
                CategoryOptions[_globalCategoryIndex],
                NormalizeOptional(_globalSenderName),
                NormalizeOptional(_globalDedupKey),
                BuildAttachments(_globalAttachments),
                adminToken: null,
                operatorId: _operatorId,
                targetUserIds: null);

            _rawJson = backendScope.Backend.FormatSuccessResponse("SendGlobalMail", result);
            string mailId = string.IsNullOrEmpty(result.mailId) ? result.globalMailId : result.mailId;
            _statusMessage = $"SendGlobalMail: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} mailId={mailId} sentAt={result.sentAt}";
        }

        private async Task SendUserMailAsync()
        {
            var targetUserIds = BuildTargetUserIds();
            ValidateSubjectBody(_userSubject, _userBody);
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var result = await BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                _userSubject.Trim(),
                _userBody.Trim(),
                BuildEndTimeIso(_userUseEndTime, _userEndDate, _userEndTime),
                CategoryOptions[_userCategoryIndex],
                NormalizeOptional(_userSenderName),
                NormalizeOptional(_userDedupKey),
                BuildAttachments(_userAttachments),
                adminToken: null,
                operatorId: _operatorId,
                targetUserIds: targetUserIds);

            _rawJson = backendScope.Backend.FormatSuccessResponse("SendGlobalMail", result);
            string mailId = string.IsNullOrEmpty(result.mailId) ? result.globalMailId : result.mailId;
            _statusMessage = $"SendTargetedMail: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} mailId={mailId} targets={targetUserIds.Count} sentAt={result.sentAt}";
        }

        private async Task DeleteMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Delete.");
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var result = await BackpackCloudCodeService.CallAdminDeleteGlobalMailAsync(_manageMailId.Trim(), adminToken: null, operatorId: _operatorId);
            _rawJson = backendScope.Backend.FormatSuccessResponse("DeleteGlobalMail", result);
            _statusMessage = $"DeleteGlobalMail: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} mailId={result.mailId}";
        }

        private async Task SetMailEndTimeAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Set EndTime.");
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var endTime = BuildEndTimeIso(_manageUseEndTime, _manageEndDate, _manageEndTime);
            var result = await BackpackCloudCodeService.CallSetMailEndTimeAsync(_manageMailId.Trim(), endTime, adminToken: null, operatorId: _operatorId);
            _rawJson = backendScope.Backend.FormatSuccessResponse("SetMailEndTime", result);
            _statusMessage = $"SetMailEndTime: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} mailId={result.mailId} endTime={result.endTime ?? "null"}";
        }

        private async Task ExpireMailAsync()
        {
            if (string.IsNullOrWhiteSpace(_manageMailId))
                throw new ArgumentException("Mail ID is required for Expire.");
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var result = await BackpackCloudCodeService.CallExpireMailAsync(_manageMailId.Trim(), adminToken: null, operatorId: _operatorId);
            _rawJson = backendScope.Backend.FormatSuccessResponse("ExpireMail", result);
            _statusMessage = $"ExpireMail: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} mailId={result.mailId}";
        }

        private async Task PurgeExpiredAsync()
        {
            string envId = await ResolveEnvironmentIdAsync();
            using var backendScope = UseRestBackend(envId);
            var result = await BackpackCloudCodeService.CallPurgeExpiredAsync(adminToken: null, operatorId: _operatorId);
            _rawJson = backendScope.Backend.FormatSuccessResponse("PurgeExpired", result);
            _statusMessage = $"PurgeExpired: HTTP {backendScope.Backend.LastStatusCode} {backendScope.Backend.LastReasonPhrase} purgedCount={result.purgedCount}";
        }

        private void DrawRawJsonFoldout()
        {
            _showRawJson = EditorGUILayout.Foldout(_showRawJson, "Server Response JSON (status/details)", true);
            if (_showRawJson && !string.IsNullOrEmpty(_rawJson))
                EditorGUILayout.TextArea(_rawJson, EditorStyles.textArea, GUILayout.MinHeight(80));
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            if (_isBusy)
                EditorGUILayout.LabelField("Working...", EditorStyles.miniLabel);
        }

        private static void ValidateSubjectBody(string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 128)
                throw new ArgumentException("MailInfo.Title must be 1-128 characters.");
            if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 1024)
                throw new ArgumentException("MailInfo.Content must be 1-1024 characters.");
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void DrawEndTimeEditor(string label, ref bool useEndTime, ref string endDate, ref string endTime)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            int mode = GUILayout.SelectionGrid(
                useEndTime ? 1 : 0,
                new[] { "Null / no expiration", "Use UTC time" },
                2,
                EditorStyles.miniButton);
            useEndTime = mode == 1;

            if (!useEndTime)
            {
                EditorGUILayout.LabelField("Cloud Save stores EndTime as null.", EditorStyles.miniLabel);
                return;
            }

            EnsureEndTimeDefaults(ref endDate, ref endTime, TimeSpan.FromDays(7));

            EditorGUILayout.BeginHorizontal();
            endDate = EditorGUILayout.TextField("Date UTC", endDate);
            endTime = EditorGUILayout.TextField("Time UTC", endTime);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+1d", GUILayout.Width(55)))
                SetEndTimePreset(ref endDate, ref endTime, TimeSpan.FromDays(1));
            if (GUILayout.Button("+7d", GUILayout.Width(55)))
                SetEndTimePreset(ref endDate, ref endTime, TimeSpan.FromDays(7));
            if (GUILayout.Button("+30d", GUILayout.Width(60)))
                SetEndTimePreset(ref endDate, ref endTime, TimeSpan.FromDays(30));
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                useEndTime = false;
                endDate = string.Empty;
                endTime = string.Empty;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Format: yyyy-MM-dd and HH:mm, interpreted as UTC.", EditorStyles.miniLabel);
        }

        private static void EnsureEndTimeDefaults(ref string endDate, ref string endTime, TimeSpan offset)
        {
            if (!string.IsNullOrWhiteSpace(endDate) && !string.IsNullOrWhiteSpace(endTime))
                return;

            SetEndTimePreset(ref endDate, ref endTime, offset);
        }

        private static void SetEndTimePreset(ref string endDate, ref string endTime, TimeSpan offset)
        {
            DateTime value = DateTime.UtcNow.Add(offset);
            endDate = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            endTime = value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string BuildEndTimeIso(bool useEndTime, string endDate, string endTime)
        {
            if (!useEndTime)
                return null;

            if (string.IsNullOrWhiteSpace(endDate) || string.IsNullOrWhiteSpace(endTime))
                throw new ArgumentException("End Time date and time are required when Use UTC time is selected.");

            string raw = $"{endDate.Trim()} {endTime.Trim()}";
            string[] formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" };
            if (!DateTime.TryParseExact(
                    raw,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                throw new ArgumentException("End Time must use UTC date yyyy-MM-dd and time HH:mm or HH:mm:ss.");
            }

            return parsed.ToUniversalTime().ToString("o");
        }

        private static List<MailAttachment> BuildAttachments(List<AttachmentDraft> drafts)
        {
            if (drafts == null || drafts.Count == 0) return null;

            var result = new List<MailAttachment>();
            foreach (var draft in drafts)
            {
                bool isItem = draft.assetType == AssetTypeOption.Item;

                if (!isItem && string.IsNullOrWhiteSpace(draft.payoutAssetId))
                    continue;

                if (draft.payoutAmount <= 0)
                    throw new ArgumentException($"Attachment must have PayoutAmount > 0.");
                if (draft.chance <= 0f)
                    throw new ArgumentException($"Attachment must have Chance > 0.");

                string payoutAssetId;
                if (isItem)
                {
                    var rows = draft.itemRows ?? new List<ItemRow>();
                    var serialized = new JArray();
                    foreach (var row in rows)
                    {
                        serialized.Add(new JObject
                        {
                            ["BlueprintId"] = row.blueprintId ?? string.Empty,
                            ["CurrentLevel"] = row.currentLevel,
                            ["Rarity"] = (int)row.rarity,
                            ["InitialLevel"] = row.initialLevel,
                            ["FromSource"] = row.fromSource ?? string.Empty,
                        });
                    }
                    payoutAssetId = serialized.ToString(Formatting.None);
                }
                else
                {
                    payoutAssetId = draft.payoutAssetId.Trim();
                }

                result.Add(new MailAttachment
                {
                    itemId = payoutAssetId,
                    id = payoutAssetId,
                    quantity = draft.payoutAmount,
                    amount = draft.payoutAmount,
                    type = isItem ? "item" : "currency",
                });
            }

            return result.Count > 0 ? result : null;
        }

        private List<string> BuildTargetUserIds()
        {
            var result = new List<string>();
            foreach (string targetUserId in _targetUserIds)
            {
                if (string.IsNullOrWhiteSpace(targetUserId))
                    continue;

                string normalized = targetUserId.Trim();
                if (!result.Contains(normalized))
                    result.Add(normalized);
            }

            if (result.Count == 0)
                throw new ArgumentException("At least one Target User ID is required.");

            return result;
        }

        private bool HasTargetUserIds()
        {
            foreach (string targetUserId in _targetUserIds)
            {
                if (!string.IsNullOrWhiteSpace(targetUserId))
                    return true;
            }

            return false;
        }

        private int CountTargetUserIds()
        {
            int count = 0;
            foreach (string targetUserId in _targetUserIds)
            {
                if (!string.IsNullOrWhiteSpace(targetUserId))
                    count++;
            }

            return count;
        }

        private static int CountAttachments(List<AttachmentDraft> attachments)
        {
            if (attachments == null) return 0;
            int count = 0;
            foreach (AttachmentDraft attachment in attachments)
            {
                if (attachment.assetType == AssetTypeOption.Item
                    ? attachment.itemRows != null && attachment.itemRows.Count > 0
                    : !string.IsNullOrWhiteSpace(attachment.payoutAssetId))
                    count++;
            }

            return count;
        }

        private void EnsureAttachmentDefaults()
        {
            if (_targetUserIds.Count == 0)
                _targetUserIds.Add(string.Empty);
            if (_globalAttachments.Count == 0)
                _globalAttachments.Add(new AttachmentDraft());
            if (_userAttachments.Count == 0)
                _userAttachments.Add(new AttachmentDraft());
        }

        private void SaveRestPrefs()
        {
            EditorPrefs.SetString(ProjectIdPrefKey, _projectId?.Trim() ?? string.Empty);
            EditorPrefs.SetString(EnvironmentNamePrefKey, _environmentName?.Trim() ?? string.Empty);
            EditorPrefs.SetString(ServiceKeyIdPrefKey, _serviceKeyId?.Trim() ?? string.Empty);
            _statusMessage = "Saved Project ID, Environment Name, and Service Key ID.";
        }

        private void ClearRestPrefs()
        {
            EditorPrefs.DeleteKey(ProjectIdPrefKey);
            EditorPrefs.DeleteKey(EnvironmentNamePrefKey);
            EditorPrefs.DeleteKey(EnvironmentIdPrefKey);
            EditorPrefs.DeleteKey(ServiceKeyIdPrefKey);
            _projectId = string.Empty;
            _environmentName = string.Empty;
            _serviceKeyId = string.Empty;
            _resolvedEnvironmentId = string.Empty;
            _resolvedForName = string.Empty;
            _statusMessage = "Cleared saved REST config.";
        }

        private bool HasRestCredentials()
        {
            return !string.IsNullOrWhiteSpace(_projectId) &&
                   !string.IsNullOrWhiteSpace(_environmentName) &&
                   !string.IsNullOrWhiteSpace(_serviceKeyId) &&
                   !string.IsNullOrWhiteSpace(_serviceSecret);
        }

        private bool HasOperatorId()
        {
            return !string.IsNullOrWhiteSpace(_operatorId);
        }

        // FILL: replace placeholder values with real UGS environment GUIDs from the dashboard.
        // These are not secrets — they are project-scoped public identifiers.
        // Keys must be lowercase to match case-insensitive lookup below.
        private static readonly Dictionary<string, string> EnvironmentNameMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "production", "<FILL_PRODUCTION_ENV_GUID>" },
                { "testing",    "<FILL_TESTING_ENV_GUID>" },
            };

        private static bool LooksLikeUuid(string value) =>
            value != null && value.Length == 36 &&
            value[8] == '-' && value[13] == '-' && value[18] == '-' && value[23] == '-';

        private async Task<string> ResolveEnvironmentIdAsync()
        {
            if (!HasRestCredentials())
                throw new InvalidOperationException("Project ID, Environment Name, Service Key ID, and Service Secret are required.");

            string name = _environmentName.Trim();
            if (!string.IsNullOrEmpty(_resolvedEnvironmentId) && _resolvedForName == name)
                return _resolvedEnvironmentId;

            string id;

            // (1) Already a GUID — use it directly, no network call needed.
            if (LooksLikeUuid(name))
            {
                id = name;
            }
            // (2) Config-map lookup — use the mapped GUID if it has been filled in.
            else if (EnvironmentNameMap.TryGetValue(name, out string mapped) && !mapped.StartsWith("<FILL"))
            {
                id = mapped;
            }
            // (3) Fallback: live UGS Environments API.
            else
            {
                id = await EditorRestCloudCodeBackend.ResolveEnvironmentIdAsync(
                    _projectId.Trim(), name, _serviceKeyId.Trim(), _serviceSecret);
            }

            _resolvedEnvironmentId = id;
            _resolvedForName = name;
            return id;
        }

        private BackendScope UseRestBackend(string resolvedEnvId)
        {
            SaveRestPrefs();
            return new BackendScope(new EditorRestCloudCodeBackend(
                _projectId.Trim(),
                resolvedEnvId,
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
            catch (CloudCodeRestException ex)
            {
                _statusMessage = $"Error: HTTP {ex.StatusCode} {ex.ReasonPhrase}";
                _rawJson = ex.FormatErrorResponse();
                Debug.LogError("[AdminMailWindow] " + ex.Message);
            }
            catch (ArgumentException ex)
            {
                _statusMessage = "Error: HTTP 400 Bad Request";
                _rawJson = FormatLocalErrorResponse(400, "Bad Request", "LocalValidation", ex.Message);
                Debug.LogError("[AdminMailWindow] " + ex.Message);
            }
            catch (Exception ex)
            {
                _statusMessage = ex is CloudCodeApiException apiEx
                    ? $"Error: HTTP {apiEx.StatusCode} {apiEx.ErrorCode}"
                    : $"Error: {ex.Message}";
                _rawJson = ex is CloudCodeApiException apiException
                    ? FormatLocalErrorResponse(apiException.StatusCode, apiException.ErrorCode, apiException.Endpoint, apiException.Message)
                    : FormatLocalErrorResponse(500, "InternalError", "Editor", ex.Message);
                Debug.LogError("[AdminMailWindow] " + ex.Message);
            }
            finally
            {
                _isBusy = false;
                Repaint();
            }
        }

        private static string FormatLocalErrorResponse(int status, string statusText, string endpoint, string details)
        {
            var wrapper = new JObject
            {
                ["status"] = status,
                ["statusText"] = statusText,
                ["endpoint"] = endpoint,
                ["details"] = details
            };
            return wrapper.ToString(Formatting.Indented);
        }

        private sealed class BackendScope : IDisposable
        {
            private readonly ICloudCodeBackend _previousBackend;
            public EditorRestCloudCodeBackend Backend { get; }

            public BackendScope(EditorRestCloudCodeBackend backend)
            {
                Backend = backend;
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
            public int LastStatusCode { get; private set; }
            public string LastReasonPhrase { get; private set; } = string.Empty;
            private string LastEndpoint { get; set; } = string.Empty;
            private string LastResponseBody { get; set; } = string.Empty;

            public EditorRestCloudCodeBackend(string projectId, string environmentId, string serviceKeyId, string serviceSecret)
            {
                _projectId = projectId;
                _environmentId = environmentId;
                _serviceKeyId = serviceKeyId;
                _serviceSecret = serviceSecret;
            }

            public async Task<T> CallEndpointAsync<T>(string endpoint, object request)
            {
                try
                {
                    string accessToken = await GetAccessTokenAsync();
                    string url = $"{CloudCodeBaseUrl}/v1/projects/{Uri.EscapeDataString(_projectId)}/modules/{ModuleName}/{Uri.EscapeDataString(endpoint)}";
                    var payload = new JObject
                    {
                        ["params"] = request == null ? new JObject() : new JObject { ["request"] = JToken.FromObject(request) }
                    };

                    var httpResponse = await PostJsonAsync(url, payload.ToString(Formatting.None), accessToken, useBearer: true);
                    SetLastResponse(endpoint, httpResponse);
                    if (!httpResponse.IsSuccessStatusCode)
                        throw new CloudCodeRestException(endpoint, httpResponse);

                    var response = JObject.Parse(httpResponse.Body);
                    // The Cloud Code REST envelope wraps the module return value under "output".
                    // The module returns ApiResponse<T> whose Data field holds the actual payload.
                    // Unwrap: envelope["output"]["Data"] → T (matches what the Unity SDK delivers).
                    JToken output = response["output"] ?? response;
                    JToken data = output["Data"] ?? output["data"] ?? output;
                    return data.ToObject<T>();
                }
                catch (CloudCodeRestException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw CloudCodeApiException.From(endpoint, ex);
                }
            }

            public string FormatSuccessResponse<T>(string endpoint, T output)
            {
                var wrapper = new JObject
                {
                    ["status"] = LastStatusCode,
                    ["statusText"] = LastReasonPhrase,
                    ["endpoint"] = string.IsNullOrEmpty(LastEndpoint) ? endpoint : LastEndpoint,
                    ["details"] = "Cloud Code REST call succeeded.",
                    ["output"] = output == null ? JValue.CreateNull() : JToken.FromObject(output)
                };

                if (!string.IsNullOrWhiteSpace(LastResponseBody))
                    wrapper["rawResponse"] = ParseJsonOrString(LastResponseBody);

                return wrapper.ToString(Formatting.Indented);
            }

            private void SetLastResponse(string endpoint, RestHttpResponse response)
            {
                LastEndpoint = endpoint;
                LastStatusCode = response.StatusCode;
                LastReasonPhrase = response.ReasonPhrase;
                LastResponseBody = response.Body;
            }

            // Calls GET https://services.api.unity.com/unity/v1/projects/{projectId}/environments
            // using Basic (service-account) auth. Returns the UUID matching envName (case-insensitive).
            // Endpoint sourced from task spec + UGS CLI IdentityApiV1 (unity/v1 path).
            // Expected response shape: { "results": [ { "id": "<UUID>", "name": "<string>", ... } ] }
            public static async Task<string> ResolveEnvironmentIdAsync(
                string projectId, string envName, string keyId, string secret)
            {
                string url = $"https://services.api.unity.com/unity/v1/projects/{Uri.EscapeDataString(projectId)}/environments";
                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{secret}"));

                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                using HttpResponseMessage httpResponse = await HttpClient.SendAsync(message);
                string body = await httpResponse.Content.ReadAsStringAsync();
                if (!httpResponse.IsSuccessStatusCode)
                    throw new CloudCodeRestException("ResolveEnvironment",
                        new RestHttpResponse((int)httpResponse.StatusCode, httpResponse.ReasonPhrase, body, false));

                var json = JObject.Parse(body);
                // Tolerate both "results" and "environments" root keys for resilience
                var results = (json["results"] ?? json["environments"]) as JArray;
                if (results == null)
                    throw new InvalidOperationException(
                        $"Environments API returned unexpected shape (no 'results'). Response: {body}");

                foreach (JToken env in results)
                {
                    string name = env.Value<string>("name");
                    if (string.Equals(name, envName, StringComparison.OrdinalIgnoreCase))
                    {
                        string id = env.Value<string>("id");
                        if (!string.IsNullOrEmpty(id))
                            return id;
                    }
                }

                throw new InvalidOperationException(
                    $"Environment '{envName}' not found in project '{projectId}'. Verify the name matches the UGS dashboard.");
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
                    throw new CloudCodeRestException("TokenExchange", new RestHttpResponse((int)response.StatusCode, response.ReasonPhrase, responseJson, false));

                var tokenResponse = JObject.Parse(responseJson);
                _accessToken = tokenResponse.Value<string>("accessToken");
                if (string.IsNullOrEmpty(_accessToken))
                    throw new InvalidOperationException("Token exchange response did not contain accessToken.");

                _accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                return _accessToken;
            }

            private static async Task<RestHttpResponse> PostJsonAsync(string url, string json, string authToken, bool useBearer)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, url);
                message.Headers.Authorization = new AuthenticationHeaderValue(useBearer ? "Bearer" : "Basic", authToken);
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await HttpClient.SendAsync(message);
                string responseJson = await response.Content.ReadAsStringAsync();
                return new RestHttpResponse((int)response.StatusCode, response.ReasonPhrase, responseJson, response.IsSuccessStatusCode);
            }
        }

        private sealed class RestHttpResponse
        {
            public RestHttpResponse(int statusCode, string reasonPhrase, string body, bool isSuccessStatusCode)
            {
                StatusCode = statusCode;
                ReasonPhrase = string.IsNullOrWhiteSpace(reasonPhrase) ? StatusCode.ToString() : reasonPhrase;
                Body = body ?? string.Empty;
                IsSuccessStatusCode = isSuccessStatusCode;
            }

            public int StatusCode { get; }
            public string ReasonPhrase { get; }
            public string Body { get; }
            public bool IsSuccessStatusCode { get; }
        }

        private sealed class CloudCodeRestException : Exception
        {
            public CloudCodeRestException(string endpoint, RestHttpResponse response)
                : base($"HTTP {response.StatusCode} {response.ReasonPhrase}: {response.Body}")
            {
                Endpoint = endpoint;
                StatusCode = response.StatusCode;
                ReasonPhrase = response.ReasonPhrase;
                ResponseBody = response.Body;
            }

            public string Endpoint { get; }
            public int StatusCode { get; }
            public string ReasonPhrase { get; }
            public string ResponseBody { get; }

            public string FormatErrorResponse()
            {
                var wrapper = new JObject
                {
                    ["status"] = StatusCode,
                    ["statusText"] = ReasonPhrase,
                    ["endpoint"] = Endpoint,
                    ["details"] = ExtractDetails(ResponseBody),
                    ["error"] = ParseJsonOrString(ResponseBody)
                };
                return wrapper.ToString(Formatting.Indented);
            }
        }

        private static JToken ParseJsonOrString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return JValue.CreateString(string.Empty);

            try
            {
                return JToken.Parse(value);
            }
            catch (JsonException)
            {
                return JValue.CreateString(value);
            }
        }

        private static string ExtractDetails(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return "Cloud Code REST call failed with an empty response body.";

            try
            {
                var token = JToken.Parse(responseBody);
                return token.SelectToken("details")?.ToString()
                       ?? token.SelectToken("detail")?.ToString()
                       ?? token.SelectToken("message")?.ToString()
                       ?? token.SelectToken("error.message")?.ToString()
                       ?? responseBody;
            }
            catch (JsonException)
            {
                return responseBody;
            }
        }
    }
}

