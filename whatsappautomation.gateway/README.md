# WhatsApp gateway

Holds the WhatsApp Web connections for the WhatsApp Automation API, one per linked number, using
Baileys. It has no business logic: the API decides what to send and when; this service links
numbers, keeps them connected, sends, and reports back.

- `src/main.ts`: start-up and shutdown.
- `src/http.ts`: the small HTTP API the .NET API calls (shared secret required).
- `src/session.ts`: one number: linking (QR or phone code), reconnecting, sending, ticks.
- `src/session-manager.ts`: all numbers.
- `src/auth-store.ts`: the saved WhatsApp login, encrypted (AES-256-GCM) in PostgreSQL.
- `src/events.ts`: posts QR codes, connection changes and ticks to the API.

Run: `npm install`, then usually nothing else: in development the API starts it on F5.
See the main README, section "WhatsApp service: first run".

Logs never contain message text, phone numbers or QR codes. Baileys' own log is off by default
(`BAILEYS_LOG_LEVEL=silent`) because it can contain phone numbers; turn it on only to debug.
