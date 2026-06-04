# Devlog_Mail_ItemSpecificAsset

## Status

- Current phase: Execution (team spawning)
- Owner: team-lead (Claude)
- Last updated: 2026-06-04

## Problem & Product Goal

Mail attachments must carry rich per-item metadata for equipment/loot mails. Replacing the
previously shipped flat `Rarity`(string) + `Level`(int) attachment fields (see
`Devlog_Mail_Attachment_RarityLevel.md`, now superseded), each **Item** attachment now embeds
a JSON **array** of item instances inside `PayoutAssetId`. **Currency** attachments keep a plain
string `PayoutAssetId` (e.g. `"mine_1"`).

Per item instance (PascalCase, matches PO example):

| Field | Type | Default |
|---|---|---|
| `BlueprintId` | string | `""` |
| `CurrentLevel` | int | `1` |
| `Rarity` | enum int | `0` (None) |
| `InitialLevel` | int | `1` |
| `FromSource` | string | `""` |

`Rarity` enum: `None=0, Common=1, Rare=2, Epic=3, Legendary=4, Mythic=5`. Stored/serialized as int.

Must flow end-to-end through every send path (Admin global, Admin user, User gift if applicable),
survive store + read-back, and be authorable in both UnityClient AdminMailWindow and AdminWeb send
form. QA writes fully passing tests.

## Solution Direction

- `PayoutAssetId` stays `string` at wire/storage layer (no schema type change).
- For `AssetType == "Item"`: `PayoutAssetId` value is a **JSON array string** of `ItemSpecificAsset`.
- For `AssetType == "Currency"` (or other): `PayoutAssetId` stays a plain id string.
- **Remove** flat `Rarity`(string)/`Level`(int) from `MailAttachment`, `Payout`, `MailAttachmentDto`
  and all 5 mappers. Mappers already copy `PayoutAssetId`/`ItemId` through, so the embedded JSON
  rides along for free — net mapper change is field *removal*.
- Add shared `Rarity` enum + `ItemSpecificAsset` model + serialize/parse helpers in each layer.
- UI (AdminMailWindow + AdminWeb form): per-attachment, when type=Item, edit a list of item rows;
  serialize rows → JSON array → `PayoutAssetId` on send. When type=Currency, plain id input.

### Decisions (PO-ratified 2026-06-04)

1. **Replace** old flat fields (not additive).
2. `PayoutAssetId` for Item is **always a JSON array** `[{...}]` even for a single item.
3. tmux panes if available (they are), else background-agent fallback.

## Scope

- `Rarity` enum + `ItemSpecificAsset` model in CloudCode, UnityClient, AdminWeb.
- Remove flat Rarity/Level everywhere; update mappers + tests.
- Item PayoutAssetId = JSON array string; Currency = plain string.
- Author + send from AdminMailWindow (Unity Editor) and AdminWeb form.
- Round-trip + parse/serialize + enum + defaults + legacy-data tests, all green.

## Non-Scope

- No gameplay consumption of the new fields (grant logic unchanged beyond carrying data).
- No DB migration; additive-safe. Old mails: flat Rarity/Level simply disappear from schema;
  old item PayoutAssetId strings (plain) still deserialize (parser tolerates non-JSON → empty list).
- No new send endpoints.

## Target JSON contract

```json
"Attachments": [
  { "AssetType": "Currency", "Chance": 1, "PayoutAmount": 25, "PayoutAssetId": "mine_1" },
  { "AssetType": "Item", "Chance": 1, "PayoutAmount": 1,
    "PayoutAssetId": "[{\"BlueprintId\":\"\",\"CurrentLevel\":1,\"Rarity\":1,\"InitialLevel\":1,\"FromSource\":\"\"}]" }
]
```

(`PayoutAssetId` for Item is a *string* whose content is the escaped JSON array.)

## Technical Design

### Layer 1 — CloudCode (`CloudCodeModule/BackpackAdventuresModule~/Mailbox/MailboxModels.cs`)

- Add `public enum Rarity { None=0, Common=1, Rare=2, Epic=3, Legendary=4, Mythic=5 }`.
- Add `public class ItemSpecificAsset` with the 5 fields + `[JsonPropertyName]` PascalCase,
  defaults per table. `Rarity` serialized as int (enum default = numeric).
- Add `MailSchemaHelper.SerializeItemAssets(List<ItemSpecificAsset>) -> string` and
  `ParseItemAssets(string) -> List<ItemSpecificAsset>` (tolerant: empty/non-JSON → `[]`).
- **Remove** `Rarity`/`Level` from `MailAttachment`(L82,85), `Payout`(L381,382),
  `MailAttachmentDto`(L476-480) and from mappers `ToAttachments`x2, `MapAttachments`,
  `MapPayouts`, `MapAttachmentDtos` (drop the Rarity/Level copy lines).
- Send modules (`SendGlobalMailModule`, `SendUserMailModule`, `GiftMailModule`) unchanged —
  delegate to mappers; verify none copy removed fields manually.
- Optional validation: when `AssetType=="Item"`, `ParseItemAssets(PayoutAssetId)` must succeed.

### Layer 2 — UnityClient (`UnityClient/Runtime/CloudCodeModels.cs` + Editor `AdminMailWindow.cs`)

- Mirror `Rarity` enum + `ItemSpecificAsset` (serializable). Remove flat rarity/level from
  `MailAttachment`(~L111), `MailAttachmentInfo`(~L183), `MailItem.attachments` projection(~L230).
- `AdminMailWindow.cs`: per-attachment draft — type dropdown; if Item, list of item rows
  (BlueprintId, CurrentLevel, Rarity dropdown, InitialLevel, FromSource) with add/remove;
  `BuildAttachments` serializes rows → JSON array → `PayoutAssetId`. Currency → plain id field.

### Layer 3 — AdminWeb (`AdminWeb/src/types.ts` + `main.ts`)

- Remove flat `rarity`/`level` (+ PascalCase variants) from `MailAttachment`, `AttachmentDraft`,
  `MailAttachmentInfo`/`AttachmentReadonlyEntry`.
- Add `ItemSpecificAsset` TS interface + `Rarity` enum/const map.
- `main.ts`: attachment form — when type=Item show repeatable item-row editor; draft→request
  serializes rows to JSON array string in `payoutAssetId`. Readonly view parses + displays rows.

## Implementation Plan / Tasks

| # | Task | Owner | Dep |
|---|------|-------|-----|
| 1 | Ratify field names/casing, enum int, array-always, per-layer struct+mapper list | architect | — |
| 2 | CloudCode: Rarity enum + ItemSpecificAsset + helpers; remove flat fields from structs+5 mappers; verify send modules | backend-dev | 1 |
| 3 | UnityClient: enum+model, remove flat fields, AdminMailWindow item-row editor + BuildAttachments | unity-client | 1 |
| 4 | AdminWeb: types.ts + main.ts item-row editor + serialize/parse, remove flat fields | unity-client | 1 |
| 5 | QA: CloudCode serialize/parse + round-trip + enum + defaults + legacy tests; UnityClient EditMode; AdminWeb build/type | QA | 2,3,4 |

## Testing Plan

- **CloudCode** (`BackpackAdventuresModuleTests~`): `ItemSpecificAsset` serialize→parse round trip;
  enum int values (Common=1…Mythic=5,None=0); Item PayoutAssetId = JSON array survives
  MapPayouts→MapAttachmentDtos + ToAttachments; Currency PayoutAssetId stays plain string;
  defaults; legacy plain-string + missing-field tolerance. `dotnet test` green.
- **UnityClient** (`Tests/EditMode`): extend `MailboxApiPositiveTests` + `FakeCloudCodeBackend`
  to set + assert item rows round-trip through PayoutAssetId.
- **AdminWeb**: `tsc --noEmit` + `vite build` exit 0; draft→request serialization unit-checked.

## Acceptance Criteria

- All 3 layers compile/build.
- Item PayoutAssetId carries JSON array with all 5 fields; Rarity as int; Currency stays plain.
- Flat Rarity/Level fully removed; no dangling references.
- Defaults + legacy data deserialize safely.
- All new + existing tests pass.

## Model & Resource Allocation

| Phase | Model/Agent | Reason |
|---|---|---|
| Design ratify, final review | team-lead (Opus) + architect (sonnet) | Authoritative design + gate |
| CloudCode impl | backend-dev (sonnet) | Focused C# server edits |
| UnityClient + AdminWeb impl | unity-client (sonnet) | C# editor + TS UI |
| Tests | QA (sonnet) | Round-trip + build verification |

MCP: `ai-game-developer` for Unity-side inspection/test runs when connectable.

## Execution Notes

- Team `cloudcode-item-asset`: architect(gate), backend-dev(CloudCode), unity-client(UnityClient+AdminWeb), QA(tests). All sonnet, tmux panes.
- **Architect ratified 2026-06-04** — design matches Devlog, no deviations. Key findings folded in:
  - CloudCode: System.Text.Json → `[JsonPropertyName]`; enum serializes as int by DEFAULT (no converter).
  - UnityClient: **Newtonsoft.Json** → `[JsonProperty]` (NOT JsonPropertyName); enum int by default.
  - AssetType casing: storage lowercase `"item"`, DTO PascalCase `"Item"` → compare case-insensitive.
  - 5 mappers need ZERO logic change beyond removing flat Rarity/Level lines (PayoutAssetId pass-through).
  - AdminWeb has BOTH list + inline attachment editors — both updated.
  - CRG not indexed for UnityCloudCode (separate git repo) → architect used direct file read (stated).
- Breaking tests (QA owns): CloudCode `AttachmentTypeTests.cs` (6 tests), `AttachmentRarityLevelSendTests.cs` (whole file), UnityClient `MailboxApiPositiveTests.cs` P02B/P03B/P04B + P03A (Item plain-string assert) + `FakeCloudCodeBackend.cs`.

## Verification Results

- **CloudCode `dotnet test`: 104 passed / 0 failed** — independently re-run by team-lead via Windows dotnet (module net7.0, tests net9.0). Confirms System.Text.Json default emits Rarity as int.
  - New `Contract/ItemSpecificAssetTests.cs` (9): serialize↔parse round-trip, Rarity int Theory (None=0…Mythic=5), JSON array survives MapPayouts→MapAttachmentDtos→ToAttachments, Currency stays plain string, defaults, `[{}]`→defaults, legacy non-JSON→empty, null/empty→empty, `[]`.
  - Replaced `AttachmentRarityLevelSendTests.cs` → `ItemSpecificAssetSendTests` (2): JSON array in Item PayoutAssetId, Currency plain, no flat fields in stored Payout.
  - Removed 6 stale flat-field tests from `AttachmentTypeTests.cs`; kept `MapPayouts_PreservesCustomAssetTypeAndChance`.
- **AdminWeb: `tsc --noEmit` exit 0, `vite build` exit 0** (QA-run).
- **UnityClient EditMode: QA-reported 61 passed / 0 failed / 2 skipped.** Replaced P02B/P03B/P04B + fixed P03A (Item now JSON array not plain "ticket"); cleaned `FakeCloudCodeBackend.CreateMailItem`; added Newtonsoft.Json to test asmdef. Execution-method confirmation pending QA reply (EditMode needs Unity Editor / ai-game-developer MCP). Not re-run by team-lead in-session.

### Repo / commit notes
- UnityCloudCode is its OWN git repo (`dyCuong03/UnityCloudCode`, branch develop), separate from the parent Unity project repo.
- Working tree had heavy pre-existing line-ending (CRLF/LF) churn on unrelated files; HEAD contained NO flat Rarity/Level (entire prior flat-field session was uncommitted). Commit staged ONLY task files with real content; excluded `.meta` and pure-EOL-churn files (`AttachmentTypeTests.cs`, `FakeCloudCodeBackend.cs`, `style.css`, `MailboxTestRunner.cs`, `TestConstants.cs`) which are content-identical to HEAD and compile clean.

## Issues & Risks

- JSON escaping: PayoutAssetId Item value is escaped JSON-in-JSON; each layer must
  serialize/parse, not double-escape.
- Casing: nested item fields PascalCase; existing attachment camelCase input vs PascalCase output
  unchanged.
- Don't touch `.meta` files.
- Removing flat fields breaks prior tests asserting them → QA must update/remove those.

## Final State

**COMPLETE** (pending PO final review + UnityClient EditMode execution-method confirmation).

ItemSpecificAsset added end-to-end; flat Rarity/Level fully removed:
- CloudCode: `Rarity` enum + `ItemSpecificAsset` + Serialize/ParseItemAssets; Item PayoutAssetId = JSON array string (always), Currency = plain id; 5 mappers pass-through. 104 tests green.
- UnityClient: Newtonsoft model + AdminMailWindow item-row editor (Currency plain / Item rows→JSON array).
- AdminWeb: types.ts + main.ts list & inline item-row editors; tsc + vite green.

Target JSON achieved:
```json
"Attachments":[{"AssetType":"Item","Chance":1,"PayoutAmount":1,"PayoutAssetId":"[{\"BlueprintId\":\"\",\"CurrentLevel\":1,\"Rarity\":1,\"InitialLevel\":1,\"FromSource\":\"\"}]"}]
```

Files (no .meta touched):
- `CloudCodeModule/BackpackAdventuresModule~/Mailbox/MailboxModels.cs`
- `UnityClient/Runtime/CloudCodeModels.cs`, `UnityClient/Editor/CloudCodeFeature/AdminMailWindow.cs`
- `AdminWeb/src/main.ts`, `AdminWeb/src/types.ts`
- Tests: `Contract/ItemSpecificAssetTests.cs` (new), `Contract/AttachmentRarityLevelSendTests.cs` (replaced→send tests), `UnityClient/Tests/EditMode/MailboxApiPositiveTests.cs`, `BackpackAdventures.CloudCode.Client.Tests.asmdef`
