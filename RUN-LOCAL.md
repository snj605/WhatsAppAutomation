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

This **keeps all your data**. What the migrations do with what you already have:

- Tenants (P1): your existing users go into one tenant called "My company"; Owners become its admins, everyone else becomes a member with matching switches.
- Occasions (this version): each user's old birthday settings (on/off, time, template) become their **Birthday** occasion; old birthday wishes are linked to it. Contacts keep their birthdays.
- Sending: every user starts at **300 messages a day**, 50 in one go, 15 minutes rest, 30–90 seconds between messages, 09:00–21:00. Each user changes this under **Sending → Settings** (300–500 a day, from the first day; no warm-up).
- Templates: each template gets its creator's tenant; none are shared until an admin shares them.

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

Files you send (photos, videos, documents up to 512 MB) are kept on disk in `WhatsAppAutomation.Server\src\WhatsAppAutomation.Api\App_Data\files` (setting `Files:Path`). The folder is made by itself and is not in git. Files are removed after sending, except files that belong to a template.

### Keeping WhatsApp connected

- The link survives restarts: when the gateway starts, it reconnects every linked number with its saved login (no QR). A passing drop (internet, computer asleep, WhatsApp hiccup) shows **Reconnecting** with the reason and fixes itself; you never need to link again unless the device is removed on the phone (**Logged out**).
- With F5, the gateway runs **only while the API runs in Visual Studio**. Stopping debugging stops WhatsApp too; nothing is sent until you press F5 again (it then reconnects by itself). For sending that goes on unattended, run it on the server (`deploy/`, which restarts by itself), or keep the API running.
- Run **one** gateway only. Two copies (for example F5 plus a separate `npm start`, or the server and your PC with the same database) keep pushing each other off: the page then says **Stopped**. Close the extra copy and press **Reconnect** (no new QR).
- WhatsApp itself logs out linked devices when the phone is offline for about 14 days: keep the phone online now and then.

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

## 7. What to test in this version

The website and the app show the same screens, in the approved look (grey page, white cards, teal only for the main button). Light, dark or device: **Me → Look**.

**P1: tenants and members**

1. **Platform admin** (website or app): sign in as `samir` → **Dashboard** (organisations, people, numbers, messages today, numbers that need a look, activity) → Tenants → **New tenant** with its first admin (password 12+ characters). Turn a tenant off and on; a turned-off tenant can be deleted.
2. **Tenant admin**: sign in with the admin you just made → you must **choose your own password** first → Me → **Members** → **Add member** (password 6+ characters), turn switches on/off: Create own modules, Broadcasts, Import from Excel, Templates.
3. **Member**: sign in as that member → choose own password → link WhatsApp, add a contact, send a single message. Without *Broadcasts* a group send is refused; without *Templates* the Templates screen is hidden.
4. **One number per user**: link the same WhatsApp number with a second user → it is logged out again with "already linked by another user", and a notification says so.
5. **Your old data**: sign in with an old user → they are in "My company" with their contacts and messages.

**Occasions (modules)**

6. **Occasions** (rail on the website, tab in the app) → **New occasion**: Anniversary (Every year), Asthi (Every year, second name "In memory of"), an Event (One time). A member starts with none; a new tenant admin gets Birthday. Up to 20.
7. Open an occasion → **Dates** → **Add date** (choose the person, day, month, year; the second name if the occasion has one). Or **Import contacts → Download my contacts**: your real contacts with a column per occasion (and "Asthi - In memory of"); fill in dates and import it back.
8. **Next 7 days**: people by day; **Select all** for a day → **Send to N people** → the composer opens with the occasion's template (or a ready wish) → send. They show "Waiting", then "Sent" and "Delivered" with the time.
9. **Settings** of the occasion: **Send by itself** on, a time a few minutes ahead, a template with `{{name}}` (and `{{for_name}}`). At that time it sends to everyone with today's date, before other messages, and a notification says it started. Saving a time that already passed today sends to today's people who have not got it yet **within a minute**; changing the time again never sends twice to the same person.

**Sending**

10. **Sending → Settings**: messages a day 300–500, messages in one go, rest after each go, shortest and longest pause, sending hours (or any time). It shows about how many fit in a day.
11. Send two group messages → **Sending → Today's queue** shows them in order with "Starts … · done about …". Use ▲ ▼ to change which goes first; occasion wishes always stay on top. Pause, Continue and Stop work per message.
12. **Results** (from the queue, or the list icon in a chat): lists All / Waiting / Sent / Delivered / Read / Failed / Skipped. When some fail (for example a number WhatsApp refuses) → **Send again to all failed** (or tick some) → they go back in the queue. Numbers not on WhatsApp are left out. **Report** prints or saves a PDF (website).
13. Reaching the daily limit: the queue shows "Left today 0" and the rest continues the next day in the sending hours.

**Templates, files, notifications**

14. **Templates** as a tenant admin: tick **Share with my organisation** → members see it (read-only) and can use it. Attach a file to a template (from the computer/phone, or a **Google Drive link** shared with "Anyone with the link").
15. Attach a video over 100 MB → you are asked to send it as a file (WhatsApp plays only up to 100 MB). Up to 512 MB.
16. **Notifications** (bell in the app header, Alerts on the website): number logged out or used by another account, sending paused, occasion wishes started. They also pop up while the app is open.

**Occasions and your own details on contacts (new)**

17. **Set up first**: website **Occasions → Contact fields** tab (app: Occasions → **Contact fields**) → add fields, for example *City* (Text), *Shop type* (Choice: Retail, Wholesale), *Joined* (Date), *Members* (Number). The type cannot change later; choices can.
18. **A contact**: Edit → **Occasions** lists only the dates this contact has. **+ Asthi** adds one (day, month, optional year; the second name if the occasion has one). Only your own occasions can be added; remove one with ✕. **More details** shows your fields by type. A member without Birthday sees no birthday anywhere.
19. **Your old birthdays**: after updating the database (step 2), every contact's birthday is a date of your Birthday occasion — nothing is lost (the old columns stay too).
20. **Search**: Contacts → choose a field (and a value) → only those people. The same filter is in **New message → People**.
21. **In messages**: in a template or new message, the field buttons put `{{city}}` in. Each person gets their own value; a person without it is skipped with "Needs details: no City". The Excel download has a column per field and per occasion.
22. **WhatsApp preview**: in New message, Templates, an occasion's Settings and a sent chat, the message shows as on WhatsApp: green bubble, the real **photo**, the **video** (website: first frame, play inside; app: tap to play), a **document** card (PDF/Word/Excel, size), *bold* _italic_ ~strike~, time and ticks. **Preview for…** picks a person and shows exactly their message (their name, language, second name, fields), or why they would be skipped.
23. **Unlink really removes the device**: WhatsApp page → **Unlink** → on the phone, WhatsApp → Settings → **Linked devices**: the device is gone. Also try it while the PC's internet is off for a moment or right after restarting the gateway: it reconnects once just to log out. If WhatsApp truly cannot be reached, the app says so and asks you to remove it on the phone.
24. **Excel with your real data**: Import contacts → **Download my contacts** → the file has all your contacts and their occasion dates and fields (no sample rows). Change a cell, add a new person in a new row, import it: the preview says *N to update, 1 to add*; after importing, the change and the new person are there and nothing else changed. An empty cell keeps what is saved.
25. **App and website together**: link WhatsApp in the app, then sign in on the website with the same user: it shows the same number as Connected and can send; the app stays signed in and connected.

## If something goes wrong

- Build errors after updating: delete the `bin` and `obj` folders of the server projects and build again.
- The files this update replaced or removed are kept in `_backup_before_p1\` and `_backup_before_final\`.
- The phone app shows the old screens: it is an old build. Run `flutter run` again (or install the new APK) after `flutter pub get`.
