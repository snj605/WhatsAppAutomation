# WhatsAppAutomation

Personal WhatsApp broadcast app for one or two users.

```
WhatsAppAutomation.slnx              The Visual Studio solution. Open this file.
whatsappautomation.client/           Angular web app.
whatsappautomation.gateway/          Node.js service that holds the WhatsApp connections (Baileys). No business logic.
WhatsAppAutomation.Server/           Everything for the ASP.NET Core (.NET 10) backend:
  Directory.Build.props              Settings shared by all backend projects.
  src/
    WhatsAppAutomation.Domain/         Entities and business rules (no frameworks).
    WhatsAppAutomation.Application/    Use cases as CQRS commands and queries (MediatR), with validation.
      Common/                          Behaviors, Interfaces, Messaging, Models (Result).
      Features/<Feature>/              Commands/, Queries/, and the feature's errors and rules.
    WhatsAppAutomation.Infrastructure/ PostgreSQL (EF Core), ASP.NET Core Identity, JWT.
    WhatsAppAutomation.Api/            Web API host and admin CLI. This is the project you run.
  tests/
    WhatsAppAutomation.Application.Tests/  Unit tests for use cases (no database).
    WhatsAppAutomation.Api.Tests/          API, architecture and integration tests against real PostgreSQL.
docs/LEARNING.md                     Learning notes.
```

A folder is added only when the first code that needs it is written (for example `Features/Contacts` when contacts are built).

The API is made for any client: the Angular app today and a phone app later. It uses JWT bearer tokens, refresh tokens tied to a device id, versioned routes (`/api/v1/...`) and standard JSON errors. The phone API returns the refresh token in the response body; the web app gets it in an HttpOnly cookie instead (see [Sign-in for the web app](#sign-in-for-the-web-app)).

New to .NET? Start with [docs/LEARNING.md](docs/LEARNING.md): it explains every concept used here and where to find it in the code.

## Backend: first run

Requirements: .NET 10 SDK, PostgreSQL 16 or later.

1. Set the two secrets with user-secrets (never put them in `appsettings*.json`):

   ```
   cd WhatsAppAutomation.Server/src/WhatsAppAutomation.Api
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=whatsappautomation;Username=...;Password=..."
   dotnet user-secrets set "Auth:SigningKey" "<at least 32 random characters>"
   ```

   Optional: `dotnet user-secrets set "MediatR:LicenseKey" "<key>"`. MediatR needs a licence key in production (free Community licence for small businesses); without one it logs a warning.

   On a server, use environment variables instead: `ConnectionStrings__Default`, `Auth__SigningKey`, `MediatR__LicenseKey`.

2. Create the database tables:

   ```
   dotnet run -- migrate
   ```

3. Create the platform admin, once (there is no sign-up screen). The password is asked for twice and is not shown:

   ```
   dotnet run -- create-platform-admin --username samir --display-name "Samir"
   ```

   There are three kinds of user:

   - **Platform admin**: creates tenants (a business or a family) with their first admin, turns tenants on or off, and manages tenant admins. Sees counts only, never a tenant's contacts or messages.
   - **Tenant admin**: may do everything inside their tenant and adds its members. A tenant can have several admins; the last active one cannot be turned off or removed.
   - **Member**: links their own number, keeps their own contacts and sends single messages. The tenant admin turns on extra switches per member: *Create own modules*, *Broadcasts*, *Import from Excel*, *Templates*.

   Passwords need at least 6 characters and must not be obvious (like `123456` or the user name); admins need at least 12. Someone whose password was chosen by an admin must choose their own at the first sign-in.

   Upgrading a database from before tenants keeps everything: its users go into one tenant called "My company", Owners become its tenant admins and everyone else a member with matching switches. Then create the platform admin as above.

4. Run the API: `dotnet run`, or F5 in Visual Studio (which also starts the web app, see below).

## Web app: first run

Requirements: Node.js 22.22.3 or later in the 22 line, 24.15 or later in the 24 line, or 26+ (what Angular 22 accepts).

1. Install the packages once, and again whenever `package.json` changes:

   ```
   cd whatsappautomation.client
   npm install
   ```

2. Paste your PrimeNG licence key into `whatsappautomation.client/src/app/core/config/primeng-license.ts`. Without it the app works but shows an "Invalid PrimeUI License" banner.

3. Press F5 in Visual Studio with `WhatsAppAutomation.Api` as the startup project. It starts the API and runs `npm start` for the web app, then opens https://localhost:49679. During development the web app forwards `/api` and `/health` to the API (`src/proxy.conf.js`), so both look like one site to the browser.

   Without Visual Studio: `dotnet run --launch-profile https` in the Api folder does the same.

4. Sign in with the platform admin you created, and add the first tenant.

The menu shows only what the signed-in user may open.

Front-end commands (in `whatsappautomation.client`): `npm test -- --watch=false` runs the unit tests (Vitest), `npm run build` makes the production build.

## WhatsApp service: first run

The gateway in `whatsappautomation.gateway` is the only part that talks to WhatsApp. It uses [Baileys](https://github.com/WhiskeySockets/Baileys) 7.0.0-rc14, an unofficial library: WhatsApp does not allow automated sending from personal numbers, and a number can be restricted or banned. Send only to people who expect your messages.

Requirements: Node.js 22.18 or later (it runs the TypeScript files directly). No Docker needed.

1. Install its packages once, and again whenever its `package.json` changes:

   ```
   cd whatsappautomation.gateway
   npm install
   ```

2. Make two secrets and store them in the API's user secrets. Run `new-secret` twice and use one value for each:

   ```
   cd WhatsAppAutomation.Server/src/WhatsAppAutomation.Api
   dotnet run -- new-secret
   dotnet user-secrets set "Gateway:Secret" "<first value>"
   dotnet user-secrets set "GatewayDev:AuthStateKey" "<second value>"
   ```

   - `Gateway:Secret` proves to each side that the other is genuine (the API calling the gateway, and the gateway posting events back).
   - `GatewayDev:AuthStateKey` encrypts the saved WhatsApp login (AES-256-GCM) in the `whatsapp_auth_state` table. Keep it safe: anyone with the database **and** this key could send as your number. If you lose or change it, you just link the number again.

3. Run `dotnet run -- migrate` once more (it adds the WhatsApp tables), then press F5. In Development the API starts the gateway for you (`node src/main.ts`) and stops it when you stop debugging; its log lines appear in the API output with `[gateway]`. Set `GatewayDev:AutoStart` to `false` if you prefer to run `npm start` in the gateway folder yourself (then set the same variables it gets: see `GatewayDevLauncher.cs`).

4. In the app open **WhatsApp**, then either **Show QR code** and scan it from the phone (WhatsApp → Linked devices → Link a device), or enter the number and **Get code** and type that code on the phone (Link with phone number instead). Linking stops after 3 minutes if it is not finished. Then send yourself a test message.

The login is saved, so after a restart the number reconnects without scanning again. If you unlink the device from the phone, the app shows "Logged out" and you link again.

Gateway commands (in `whatsappautomation.gateway`): `npm test` runs its tests, `npm run typecheck` checks the types. To include the database test, also set `GATEWAY_TEST_PGDATABASE` to an empty database and the `PGHOST`, `PGUSER`, `PGPASSWORD` variables.

On a server (later): run the gateway as its own service on the private network only, with `GATEWAY_SECRET`, `GATEWAY_AUTH_KEY` (or `GATEWAY_AUTH_KEY_FILE`), `GATEWAY_EVENTS_URL` and the `PG*` variables; set `Gateway__BaseUrl` and `Gateway__Secret` on the API; and make sure the reverse proxy never forwards `/internal`.

## API

| Endpoint | Permission | Purpose |
| --- | --- | --- |
| `POST /api/v1/auth/login` | none | `{ userName, password, deviceId, deviceName? }` → access token (10 min) + refresh token (30 days) + the user. |
| `POST /api/v1/auth/refresh` | none | `{ refreshToken, deviceId }` → a new pair. The old refresh token stops working. |
| `POST /api/v1/auth/logout` | none | `{ refreshToken }` → ends the session on that device. |
| `GET /api/v1/auth/me` | signed in | The user and their permissions. Apps use the permissions to show or hide screens. |
| `PUT /api/v1/auth/password` | signed in | `{ currentPassword, newPassword }`. Also allowed while the account must change its password (`user.mustChangePassword`), when nothing else is. |
| `GET/POST /api/v1/members` | `members.manage` | The people of your tenant. `{ userName, displayName, password, isAdmin, switches: [] }`; they choose their own password at the first sign-in. |
| `PUT /api/v1/members/{id}` | `members.manage` | `{ displayName, isAdmin, switches: [] }`. Switches: `modules.manage`, `broadcasts.send`, `contacts.import`, `templates.manage`. |
| `PUT /api/v1/members/{id}/active`, `PUT /api/v1/members/{id}/password`, `DELETE /api/v1/members/{id}` | `members.manage` | Turn off (signs out everywhere), set a new password, delete with all their data. Not on yourself; the last active admin stays. |
| `GET/POST /api/v1/platform/tenants`, `PUT/DELETE /api/v1/platform/tenants/{id}`, `PUT …/{id}/active` | `platform.tenants` | Tenants with counts only. Create takes `{ name, adminUserName, adminDisplayName, adminPassword }`. Only a turned-off tenant can be deleted. |
| `GET/POST /api/v1/platform/tenants/{id}/admins`, `PUT/DELETE …/admins/{adminId}`, `PUT …/active`, `PUT …/password` | `platform.tenants` | The tenant's admins. |
| `GET /api/v1/whatsapp` | signed in | Your WhatsApp link: `status` (`not_linked`, `linking`, `connecting`, `connected`, `reconnecting`, `logged_out`, `restricted`, `disconnected`), number, WhatsApp name, when linked. |
| `POST /api/v1/whatsapp/link` | `whatsapp.link` | `{ method: "qr" }` or `{ method: "code", phoneNumber: "+91…" }`. The QR codes or the phone code arrive live (see below). |
| `POST /api/v1/whatsapp/unlink` | `whatsapp.link` | Logs out on WhatsApp and deletes the saved login. Also cancels linking. |
| `GET /api/v1/whatsapp/messages` | `messages.send` | Your last 20 messages with their ticks (`?take=` up to 100). |
| `POST /api/v1/whatsapp/messages` | `messages.send` | `{ to: "+91…", text }` sends one message now. 10 a minute per user. Numbers need the country code. |
| `GET/POST /api/v1/categories`, `PUT/DELETE /api/v1/categories/{id}` | `contacts.view` / `contacts.manage` | Your categories (`{ name, color }`). A category that contacts use cannot be deleted. |
| `GET/POST /api/v1/groups`, `PUT/DELETE /api/v1/groups/{id}` | `contacts.view` / `contacts.manage` | Your groups with the number of contacts. |
| `POST /api/v1/groups/{id}/members` | `contacts.manage` | `{ contactIds: [], add }` adds or removes contacts. |
| `GET /api/v1/contacts` | `contacts.view` | `?search=&categoryId=&groupId=&needsDetails=&page=&pageSize=`. |
| `GET/POST /api/v1/contacts`, `PUT /api/v1/contacts/{id}` | `contacts.view` / `contacts.manage` | One contact. The phone number is unique per user; the name in the contact's language is required. |
| `POST /api/v1/contacts/delete` | `contacts.manage` | `{ ids: [] }`. |
| `GET /api/v1/contacts/import/template` | `contacts.import` | Excel template with instructions and dropdowns. |
| `POST /api/v1/contacts/import?apply=false\|true` | `contacts.import` | Upload `.xlsx` or UTF-8 `.csv` (5 MB, 5000 rows). `apply=false` only checks and shows each row; `apply=true` saves. |
| `GET/PUT /api/v1/preferences` | signed in | `{ defaultLanguage: "en"\|"hi"\|"gu" }`. |
| `POST /api/v1/broadcasts/preview` | `messages.send` (more than one contact: `broadcasts.send`) | Same body as create: who gets it, in which language, and who is skipped and why. Sends nothing. |
| `GET/POST /api/v1/broadcasts`, `GET /api/v1/broadcasts/{id}` | `messages.send` (more than one contact: `broadcasts.send`) | `{ name, textEn?, textHi?, textGu?, categoryIds, groupIds, contactIds }`. Texts may use `{name}` and `{salutation}`. Details include every recipient with their ticks. |
| `POST /api/v1/broadcasts/{id}/pause\|resume\|cancel` | `messages.send` (more than one contact: `broadcasts.send`) | Cancel skips everyone not sent yet. |
| `GET /api/v1/broadcasts/pacing` | `messages.send` (more than one contact: `broadcasts.send`) | Sent today, today's limit, when the next message goes, and why it is waiting. |
| `GET /health` | none | Database and WhatsApp gateway. The gateway being down shows as "Degraded", not "Unhealthy". |

Live updates use SignalR at `/hubs/whatsapp` (access token as `?access_token=`). Events: `qr` `{ qr }`, `pairingCode` `{ code }`, `account` (same shape as `GET /api/v1/whatsapp`), `message` (one message with its ticks). Each user receives only their own.

`POST /internal/whatsapp/events` is for the gateway only (shared secret in `X-Gateway-Secret`).

Permissions are defined in code (`WhatsAppAutomation.Server/src/WhatsAppAutomation.Domain/Authorization/Permissions.cs`); who has which follows from the kind of user and, for members, their switches (`UserKinds.cs`). Changes take effect on the next request.

### Sign-in for the web app

A browser should not keep a 30-day refresh token where JavaScript can read it, so the web app uses these endpoints instead of the three above. They do the same work.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/v1/auth/web/xsrf` | Sets the `XSRF-TOKEN` cookie. The app calls it on start. |
| `POST /api/v1/auth/web/login` | `{ userName, password }` → access token + the user. The refresh token goes into the `waa_refresh` cookie. |
| `POST /api/v1/auth/web/refresh` | No body: uses the cookies → a new access token, and a new refresh cookie. |
| `POST /api/v1/auth/web/logout` | Ends the session and deletes the cookie. |

- `waa_refresh` and `waa_device` are HttpOnly (JavaScript cannot read them), Secure, SameSite=Strict, and only sent to `/api/v1/auth/web`.
- The access token stays only in the app's memory. Reloading the page gets a new one from the cookie.
- These POSTs need the `X-XSRF-TOKEN` header to equal the `XSRF-TOKEN` cookie (Angular adds it). A wrong or missing header gets 403 with code `auth.xsrf_failed`.

How sessions work:

- `deviceId` is a random id the app creates once per install and keeps in secure storage. Each device has its own session; signing in again on a device replaces that device's session.
- Refresh tokens rotate on every use and only their SHA-256 hash is stored. If an already used token comes back, or comes from a different device, the whole session for that device is ended, because that means the token was copied.
- Clients must not refresh twice in parallel with the same token: the second call ends the session. Queue refreshes in the client.
- 5 wrong passwords lock the account for 15 minutes. Sign-in, refresh and logout are limited to 10 requests a minute per IP.

## Broadcasts: how sending is kept safe

WhatsApp bans numbers that send like a robot. So a broadcast never goes out at once: the server sends **one message at a time**, in the background, with these rules (section `Broadcasting` in `appsettings.json`):

| Setting | Default | Meaning |
| --- | --- | --- |
| `SendFrom`, `SendUntil`, `TimeZone` | 09:00 to 20:00, `Asia/Kolkata` | Sends only in these hours. The same value twice means any time. |
| `MinDelaySeconds`, `MaxDelaySeconds` | 45 to 120 | Random wait between two messages. |
| `BatchSize`, `BatchRestMinMinutes`, `BatchRestMaxMinutes` | 10, then 10 to 20 minutes | A longer rest after every 10 messages. |
| `DailyCap` | 150 | At most this many messages a day (counting single messages too). |
| `WarmUpCaps` | 20, 40, 75 | Lower limits in weeks 1, 2 and 3 after linking the number. |
| `ContactGapDays` | 0 (off) | When above 0, a contact gets at most one broadcast in this many days and is skipped otherwise. Off because recipients are people already in the sender's phone book. |
| `MaxFailuresInRow` | 3 | After 3 failed sends in a row, all your broadcasts pause. |
| `Enabled` | true | `false` stops the background sender (broadcasts stay as they are). Read once at start, so restart the API after changing it. |

Broadcasts also pause by themselves when the number is unlinked, logged out or restricted, and wait (without failing) while WhatsApp is reconnecting or the gateway is down. Resume them from the broadcast's page.

Each contact gets the text in their own language. If there is no text in their language, the user's default language is used; if there is none in that either, they are skipped. A text that uses `{name}` skips contacts who have no name in that language ("Needs details").

Every member may send to one contact at a time. Sending to several people, groups or categories needs the *Broadcasts* switch.

## Tests

```
dotnet test WhatsAppAutomation.slnx
```

Web app tests: see [Web app: first run](#web-app-first-run).

Integration tests start PostgreSQL in Docker through Testcontainers. To use an existing PostgreSQL instead, set `WAA_TEST_POSTGRES` to a connection string for that server; each run creates and drops its own database.
