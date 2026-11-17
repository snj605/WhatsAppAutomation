# Run everything on your PC (local test)

This is the checklist for testing the current version on Windows: API, WhatsApp gateway, website and the Android app over wireless debugging. Nothing here is put in git: secrets stay in .NET user-secrets on your PC.

Requirements: .NET 10 SDK, PostgreSQL 16+, Node.js 22.18+ (or 24.15+), Flutter (stable) with Android SDK, Android phone with Android 11+.

## 1. Database and API secrets (once)

PowerShell, in `WhatsAppAutomation.Server\src\WhatsAppAutomation.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=whatsappautomation;Username=postgres;Password=<your password>"
dotnet run -- new-secret     # run 3 times, use one value for each line below
dotnet user-secrets set "Auth:SigningKey" "<value 1>"
dotnet user-secrets set "Gateway:Secret" "<value 2>"
dotnet user-secrets set "GatewayDev:AuthStateKey" "<value 3>"
```

If you already did this before, skip it: the values are still there (`dotnet user-secrets list` shows them).

## 2. Update the database (every time you get a new version)

```powershell
dotnet run -- migrate
```

This **keeps all your data**. With this version (tenants): your existing users go into one tenant called "My company"; Owners become its admins, everyone else becomes a member with matching switches.

## 3. Create the platform admin (once)

```powershell
dotnet run -- create-platform-admin --username samir --display-name "Samir"
```

It asks for the password twice (at least 12 characters, not shown). This account runs the platform: tenants and their admins. It has no WhatsApp number or contacts of its own.

## 4. Start the API and the gateway

Press **F5** in Visual Studio with `WhatsAppAutomation.Api` as the startup project (it also starts the gateway and the website), or:

```powershell
cd WhatsAppAutomation.Server\src\WhatsAppAutomation.Api
dotnet run --launch-profile https
```

The first time only: `npm install` in `whatsappautomation.gateway` and in `whatsappautomation.client`.

Check: http://localhost:5163/health shows "Healthy" (or "Degraded" while the gateway is still starting).

## 5. Website

Open https://localhost:49679 (Visual Studio opens it for you).

## 6. Android app over wireless debugging

1. Phone: Settings → Developer options → **Wireless debugging** on → **Pair device with pairing code**.
2. PC (same Wi-Fi):
   ```powershell
   adb pair <phone-ip>:<pairing-port>      # enter the 6-digit code
   adb connect <phone-ip>:<debug-port>     # the port shown on the Wireless debugging screen
   adb devices                             # the phone must be listed as "device"
   adb reverse tcp:5163 tcp:5163           # the phone's localhost:5163 now reaches the API on the PC
   ```
3. Run the app:
   ```powershell
   cd WhatsAppAutomation.Mobile\whatsapp_automation
   flutter pub get
   flutter run
   ```

`adb reverse` is lost when the phone reconnects: run it again if the app says "cannot reach server". Without adb reverse you can point the app at the PC's address instead: `flutter run --dart-define=API_BASE_URL=http://<pc-ip>:5163` (then Windows Firewall must allow port 5163, and the API must listen on that address).

## 7. What to test in this version (P1: tenants and members)

1. **Platform admin** (website or app): sign in as `samir` → Tenants → **New tenant** with its first admin (password 12+ characters). Turn a tenant off and on; a turned-off tenant can be deleted.
2. **Tenant admin**: sign in with the admin you just made → you must **choose your own password** first → Me → **Members** → **Add member** (password 6+ characters), turn switches on/off: Create own modules, Broadcasts, Import from Excel, Templates.
3. **Member**: sign in as that member → choose own password → link WhatsApp, add a contact, send a single message. Without *Broadcasts* a group send is refused; without *Templates* the Templates screen is hidden.
4. **One number per user**: link the same WhatsApp number with a second user → it is logged out again with "already linked by another user".
5. **Your old data**: sign in with an old user → they are in "My company" with their contacts and messages.

## If something goes wrong

- Build errors after updating: delete the `bin` and `obj` folders of the server projects and build again.
- The files this update replaced or removed are kept in `_backup_before_p1\`.
