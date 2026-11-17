/** Shapes from /api/v1/whatsapp. They mirror the C# DTOs; enums arrive as snake_case text. */
export type ConnectionStatus =
  | 'not_linked'
  | 'linking'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'logged_out'
  | 'restricted'
  | 'disconnected';

export interface WhatsAppAccount {
  status: ConnectionStatus;
  statusReason: string | null;
  statusChangedAt: string | null;
  phoneNumber: string | null;
  profileName: string | null;
  linkedAt: string | null;
}

export type MessageStatus = 'queued' | 'sent' | 'delivered' | 'read' | 'failed';

export interface OutgoingMessage {
  id: string;
  toPhoneNumber: string;
  text: string;
  status: MessageStatus;
  errorCode: string | null;
  createdAt: string;
  sentAt: string | null;
  deliveredAt: string | null;
  readAt: string | null;
}

export type LinkMethod = 'qr' | 'code';
