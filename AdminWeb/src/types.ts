// ─── TypeScript DTOs mirrored from CloudCodeModels.cs ───────────────────────────
// Keep in sync with:
//   UnityClient/Runtime/CloudCodeModels.cs
//   CloudCodeModule/BackpackAdventuresModule~/Mailbox/*.cs

// ── Rarity enum (matches C# Rarity in CloudCodeModels.cs) ───────────────────────
export const Rarity = {
  None: 0,
  Common: 1,
  Rare: 2,
  Epic: 3,
  Legendary: 4,
  Mythic: 5,
} as const
export type RarityValue = typeof Rarity[keyof typeof Rarity]
export const RARITY_LABELS: Record<RarityValue, string> = {
  0: 'None',
  1: 'Common',
  2: 'Rare',
  3: 'Epic',
  4: 'Legendary',
  5: 'Mythic',
}

// ── Per-item instance (PascalCase keys, Rarity as int) ───────────────────────────
export interface ItemSpecificAsset {
  BlueprintId: string
  CurrentLevel: number
  Rarity: RarityValue
  InitialLevel: number
  FromSource: string
}

// ── Wire format for attachments ─────────────────────────────────────────────────
export interface MailAttachment {
  type: 'currency' | 'item' | string;
  id: string;
  itemId: string;
  amount: number;
  quantity: number;
  chance: number;
}

// ── Local draft (mirrors AttachmentDraft in AdminMailWindow.cs) ──────────────────
export interface AttachmentDraft {
  payoutAssetId: string;
  assetType: string;
  payoutAmount: number;
  chance: number;
  itemRows: ItemSpecificAsset[];
}

// ── SendGlobalMail ────────────────────────────────────────────────────────────────
export interface SendGlobalMailRequest {
  subject: string;
  body: string;
  expiresAt?: string | null;
  mailCategory?: string | null;
  senderName?: string | null;
  dedupKey?: string | null;
  attachments?: MailAttachment[] | null;
  adminToken?: string | null;
  operatorId: string;
  targetUserIds?: string[] | null;
}

export interface SendGlobalMailResponse {
  mailId?: string;
  globalMailId?: string;
  sentAt?: string;
}

// ── GetGlobalMails ────────────────────────────────────────────────────────────────
export interface GetGlobalMailsRequest {
  page: number;
  pageSize: number;
}

// Cloud Save / server-side attachment info (Payout shape)
export interface MailAttachmentInfo {
  PayoutAssetId?: string;
  Chance?: number;
  AssetType?: string;
  PayoutAmount?: number;
  // lowercase variants for tolerant parsing
  payoutAssetId?: string;
  chance?: number;
  assetType?: string;
  payoutAmount?: number;
}

// MailInfo (MailItemDto inner)
export interface MailInfo {
  Title?: string;
  Content?: string;
  StartTime?: string;
  ExpireTime?: string;
  Period?: number;
  Attachment?: MailAttachmentInfo[];
  // lowercase variants
  title?: string;
  content?: string;
  startTime?: string;
  expireTime?: string;
  attachment?: MailAttachmentInfo[];
}

export interface MailMetaData {
  IsRead?: boolean;
  IsClaimed?: boolean;
  MailCategory?: string;
  SenderType?: string;
  Sender?: string;
  DedupKey?: string;
  // lowercase variants
  isRead?: boolean;
  isClaimed?: boolean;
  mailCategory?: string;
  senderType?: string;
  sender?: string;
  dedupKey?: string;
}

// Mail record as returned by GetGlobalMails.
// Tolerates both Pascal-case (server MailItemDto) and camelCase.
export interface MailRecord {
  MessageId?: string;
  messageId?: string;
  TargetUserIds?: string[] | null;
  targetUserIds?: string[] | null;
  MailInfo?: MailInfo;
  mailInfo?: MailInfo;
  MailMetaData?: MailMetaData;
  mailMetaData?: MailMetaData;
}

export interface GetGlobalMailsResponse {
  Mails?: MailRecord[];
  mails?: MailRecord[];
  TotalCount?: number;
  totalCount?: number;
  Page?: number;
  page?: number;
  PageSize?: number;
  pageSize?: number;
  HasMore?: boolean;
  hasMore?: boolean;
}

// ── SetMailEndTime ────────────────────────────────────────────────────────────────
export interface SetMailEndTimeRequest {
  mailId: string;
  endTime: string | null;
  adminToken?: string | null;
  operatorId: string;
}

export interface SetMailEndTimeResponse {
  mailId?: string;
  endTime?: string | null;
}

// ── UpdateGlobalMail ─────────────────────────────────────────────────────────────
export interface UpdateGlobalMailRequest {
  mailId: string;
  subject: string;
  body: string;
  attachments?: MailAttachment[] | null;
  adminToken?: string | null;
  operatorId: string;
}

export interface UpdateGlobalMailResponse {
  mailId?: string;
}

// ── ExpireMail ────────────────────────────────────────────────────────────────────
export interface ExpireMailRequest {
  mailId: string;
  adminToken?: string | null;
  operatorId: string;
}

export interface ExpireMailResponse {
  mailId?: string;
  expiredAt?: string;
}

// ── DeleteGlobalMail ──────────────────────────────────────────────────────────────
export interface DeleteGlobalMailRequest {
  mailId: string;
  adminToken?: string | null;
  operatorId: string;
}

export interface DeleteMailResponse {
  mailId?: string;
}

// ── PurgeExpired ──────────────────────────────────────────────────────────────────
export interface PurgeExpiredRequest {
  adminToken?: string | null;
  operatorId: string;
}

export interface PurgeExpiredResponse {
  purgedCount?: number;
  purgedAt?: string;
}

// ── UGS Environments API response ─────────────────────────────────────────────────
export interface UgsEnvironment {
  id: string;
  name: string;
  isDefault?: boolean;
  projectId?: string;
}

export interface UgsEnvironmentsResponse {
  results?: UgsEnvironment[];
}

// ── Category options (matches AdminMailWindow.cs CategoryOptions) ─────────────────
export const CATEGORY_OPTIONS = [
  'System',
  'Event',
  'Compensation',
  'Gift',
  'Support',
  'PatchNote',
] as const;

export type MailCategory = typeof CATEGORY_OPTIONS[number];

// ── Helper: normalise mail record fields ──────────────────────────────────────────
export function mailId(m: MailRecord): string {
  return m.MessageId ?? m.messageId ?? '(unknown)';
}

export function mailTitle(m: MailRecord): string {
  const info = m.MailInfo ?? m.mailInfo;
  return info?.Title ?? info?.title ?? '';
}

export function mailContent(m: MailRecord): string {
  const info = m.MailInfo ?? m.mailInfo;
  return info?.Content ?? info?.content ?? '';
}

export function mailStartTime(m: MailRecord): string {
  const info = m.MailInfo ?? m.mailInfo;
  return info?.StartTime ?? info?.startTime ?? '';
}

export function mailEndTime(m: MailRecord): string | null {
  const info = m.MailInfo ?? m.mailInfo;
  return info?.ExpireTime ?? info?.expireTime ?? null;
}

export function mailTargetUsers(m: MailRecord): string[] {
  return m.TargetUserIds ?? m.targetUserIds ?? [];
}

export function mailAttachments(m: MailRecord): MailAttachmentInfo[] {
  const info = m.MailInfo ?? m.mailInfo;
  return info?.Attachment ?? info?.attachment ?? [];
}
