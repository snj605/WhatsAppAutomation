# WhatsApp Automation: mobile app (Flutter)

Android and iPhone app for the same API as the website.

## What works now (milestone M1)

- Sign in with user name and password.
- Open the app with fingerprint or face after the first sign-in (optional; locks again after 5 minutes away).
- Link WhatsApp on the same phone with a code (no QR needed), see the status, unlink.
- Chats, Contacts and Birthdays tabs are placeholders for the next milestones.

## Run on a phone against the API on this PC

1. Start the API on plain http, so the phone is not sent to an https address it does not trust:
   `dotnet run --launch-profile http` in `WhatsAppAutomation.Server/src/WhatsAppAutomation.Api`
   (it listens on `http://localhost:5163`).
2. Connect the phone (USB or wireless debugging) and check it is listed: `adb devices`.
3. Make `localhost` on the phone point to this PC: `adb reverse tcp:5163 tcp:5163`
   (repeat after the phone reconnects).
4. In this folder: `flutter pub get`, then `flutter run`.

To use another server: `flutter run --dart-define=API_BASE_URL=https://your-server`.
Plain http works only in debug builds; release builds need https.

## Checks

- `flutter analyze`
- `flutter test`

## Layout

- `lib/core`: API client, error messages, secure storage, live updates (SignalR), providers.
- `lib/features/<name>`: one folder per feature (auth, home, whatsapp), each with its screens and state.
- Rules live on the server. The app only shows data and sends requests.
