import { ConnectionStatus, MessageStatus } from './whatsapp.models';

export type Severity = 'success' | 'info' | 'warn' | 'danger' | 'secondary';

export interface StatusInfo {
  label: string;
  severity: Severity;
  text: string;
}

/** How each connection status is shown. Used by the WhatsApp page and Home. */
const STATUS: Record<ConnectionStatus, StatusInfo> = {
  not_linked: { label: 'Not linked', severity: 'secondary', text: 'No WhatsApp number is linked yet.' },
  linking: { label: 'Linking', severity: 'info', text: 'Waiting for you to finish linking on your phone.' },
  connecting: { label: 'Connecting', severity: 'info', text: 'Connecting to WhatsApp…' },
  connected: { label: 'Connected', severity: 'success', text: 'Linked and connected. Messages can be sent.' },
  reconnecting: { label: 'Reconnecting', severity: 'warn', text: 'The connection dropped. It reconnects by itself.' },
  logged_out: { label: 'Logged out', severity: 'danger', text: 'This number was unlinked on the phone. Link it again.' },
  restricted: {
    label: 'Restricted',
    severity: 'danger',
    text: 'WhatsApp refused this number (restricted or banned). Nothing is sent. Check WhatsApp on the phone.',
  },
  disconnected: {
    label: 'Stopped',
    severity: 'warn',
    text: 'Another copy of the WhatsApp service took this login, so it stopped here. Press Reconnect (no new QR needed), and make sure only one copy runs.',
  },
};

export function connectionStatus(status: ConnectionStatus | null | undefined): StatusInfo {
  return STATUS[status ?? 'not_linked'];
}

export interface TickInfo {
  icon: string;
  label: string;
}

const TICKS: Record<MessageStatus, TickInfo> = {
  queued: { icon: 'pi pi-clock', label: 'Waiting' },
  sent: { icon: 'pi pi-check', label: 'Sent' },
  delivered: { icon: 'pi pi-check-circle', label: 'Delivered' },
  read: { icon: 'pi pi-eye', label: 'Read' },
  failed: { icon: 'pi pi-times-circle', label: 'Not sent' },
};

export function messageTick(status: MessageStatus): TickInfo {
  return TICKS[status];
}
