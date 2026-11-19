import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../app/theme_mode.dart';
import '../../core/providers.dart';
import '../../core/ui.dart';
import '../account/change_password_page.dart';
import '../admin/members_page.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import '../contacts/import_page.dart';
import '../contacts/lists_page.dart';
import '../notifications/notifications_page.dart';
import '../templates/templates_page.dart';
import '../update/update_controller.dart';
import '../whatsapp/link_whatsapp_page.dart';
import '../whatsapp/test_message_page.dart';
import '../whatsapp/whatsapp_controller.dart';
import '../whatsapp/whatsapp_models.dart';

/// The user's own settings: WhatsApp link, templates, lists, import, fingerprint/face unlock, admin, sign out.
class MePage extends ConsumerStatefulWidget {
  const MePage({super.key});

  @override
  ConsumerState<MePage> createState() => _MePageState();
}

class _MePageState extends ConsumerState<MePage> {
  bool _biometricsAvailable = false;
  bool _biometricsOn = false;

  @override
  void initState() {
    super.initState();
    _loadBiometrics();
  }

  Future<void> _loadBiometrics() async {
    final bool available = await ref.read(biometricServiceProvider).isAvailable();
    final bool on = await ref.read(sessionProvider.notifier).isBiometricsOn();
    if (mounted) {
      setState(() {
        _biometricsAvailable = available;
        _biometricsOn = on;
      });
    }
  }

  Future<void> _setBiometrics(bool on) async {
    final bool changed = await ref.read(sessionProvider.notifier).setBiometrics(on: on);
    if (mounted && changed) {
      setState(() => _biometricsOn = on);
    }
  }

  Future<void> _checkForUpdates() async {
    final UpdateController updates = ref.read(updateProvider.notifier);
    if (!await updates.check(manual: true) || !mounted) {
      final String? error = ref.read(updateProvider).error;
      if (mounted && error != null) {
        showInfoToast(context, 'Could not check', description: error);
      }

      return;
    }

    final UpdateState update = ref.read(updateProvider);
    if (update.release == null) {
      showInfoToast(context, 'You have the latest version', description: 'Version ${update.currentVersion}');
    }
    // When there is a new version, the update bar (or the "Update needed" screen) appears by itself.
  }

  Future<void> _signOut() async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Sign out?',
      body: 'You will need your password to sign in again. Sending that is already running goes on.',
      confirm: 'Sign out',
      destructive: true,
    );
    if (yes) {
      await ref.read(sessionProvider.notifier).signOut();
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final SessionState session = ref.watch(sessionProvider);
    final WhatsAppState whatsApp = ref.watch(whatsAppProvider);
    final WhatsAppAccount? account = whatsApp.account;
    final UpdateState update = ref.watch(updateProvider);
    final SignedIn? signedIn = session is SignedIn ? session : null;
    final AppUser? user = signedIn?.user;
    bool can(String permission) => signedIn?.can(permission) ?? false;
    final bool connected = account?.isConnected ?? false;
    const Widget chevron = Icon(FLucideIcons.chevronRight, size: 18);

    return ListView(
      padding: const EdgeInsets.only(bottom: 24),
      children: <Widget>[
        if (user != null) ...<Widget>[
          const SizedBox(height: 20),
          Center(child: PersonAvatar(name: user.displayName, size: 84)),
          const SizedBox(height: 10),
          Center(
            child: Text(user.displayName, style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w700)),
          ),
          Center(
            child: Text('@${user.userName} · ${user.kindLine}', style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground)),
          ),
          const SizedBox(height: 8),
        ],
        if (can('whatsapp.link'))
        CardSection(
          title: 'WhatsApp',
          children: <Widget>[
            ListRow(
              leading: IconBadge(
                FLucideIcons.smartphone,
                color: connected ? theme.colors.primary : const Color(0xFFD97706),
              ),
              title: whatsApp.loading
                  ? 'Checking…'
                  : account == null
                      ? (whatsApp.error ?? 'Not linked')
                      : account.label,
              subtitle: account?.phoneNumber ?? 'Tap to link your number',
              badge: connected ? const Pill('Live') : null,
              trailing: chevron,
              onTap: () => pushPage<void>(context, const LinkWhatsAppPage()),
            ),
            if (can('messages.send') && (account?.isLinked ?? false))
              ListRow(
                leading: const IconBadge(FLucideIcons.send, color: Color(0xFF3B82F6)),
                title: 'Message one person',
                subtitle: 'Type a message to one number',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const TestMessagePage()),
              ),
          ],
        ),
        if (can('messages.send'))
          CardSection(
            children: <Widget>[
              ListRow(
                leading: const IconBadge(FLucideIcons.bell),
                title: 'Notifications',
                subtitle: 'Your number, sending and occasion wishes',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const NotificationsPage()),
              ),
            ],
          ),
        if (can('contacts.view'))
        CardSection(
          title: 'Messages & contacts',
          children: <Widget>[
            if (can('templates.manage'))
              ListRow(
                leading: const IconBadge(FLucideIcons.fileText),
                title: 'Message templates',
                subtitle: 'Your saved messages',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const TemplatesPage()),
              ),
            if (can('contacts.view'))
              ListRow(
                leading: const IconBadge(FLucideIcons.tags, color: Color(0xFF8B5CF6)),
                title: 'Categories and groups',
                subtitle: 'Family, friends, elders…',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const ListsPage()),
              ),
            if (can('contacts.import'))
              ListRow(
                leading: const IconBadge(FLucideIcons.fileSpreadsheet, color: Color(0xFF10B981)),
                title: 'Import contacts',
                subtitle: 'From an Excel or CSV file',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const ImportPage()),
              ),
          ],
        ),
          CardSection(
            title: 'Security',
            children: <Widget>[
              ListRow(
                leading: const IconBadge(FLucideIcons.lock, color: Color(0xFF475467)),
                title: 'Change password',
                subtitle: 'Choose a new one',
                trailing: chevron,
                onTap: () => pushPage<void>(context, const ChangePasswordPage()),
              ),
              if (_biometricsAvailable)
              ListRow(
                leading: const IconBadge(FLucideIcons.fingerprint, color: Color(0xFFEC4899)),
                title: 'Fingerprint or face',
                subtitle: 'Asked when the app opens, and after 5 minutes away',
                trailing: FSwitch(value: _biometricsOn, onChange: _setBiometrics),
                onTap: () => _setBiometrics(!_biometricsOn),
              ),
            ],
          ),
        if (can('members.manage'))
          CardSection(
            title: 'Admin',
            children: <Widget>[
              if (can('members.manage'))
                ListRow(
                  leading: const IconBadge(FLucideIcons.users, color: Color(0xFF6366F1)),
                  title: 'Members',
                  subtitle: 'Add people and choose what they may do',
                  trailing: chevron,
                  onTap: () => pushPage<void>(context, const MembersPage()),
                ),
            ],
          ),
        CardSection(
          title: 'Look',
          padding: const EdgeInsets.all(12),
          children: <Widget>[
            ChoiceChips<ThemeMode>(
              options: <(ThemeMode, String)>[
                for (final ThemeMode m in <ThemeMode>[ThemeMode.light, ThemeMode.dark, ThemeMode.system]) (m, themeModeLabel(m)),
              ],
              selected: ref.watch(themeModeProvider),
              onChange: (ThemeMode m) => ref.read(themeModeProvider.notifier).set(m),
            ),
          ],
        ),
        if (UpdateController.supported)
          CardSection(
            title: 'App',
            children: <Widget>[
              ListRow(
                leading: const IconBadge(FLucideIcons.refreshCw, color: Color(0xFF0EA5E9)),
                title: update.release != null ? 'Update to version ${update.release!.version}' : 'Check for updates',
                subtitle: update.currentVersion.isEmpty ? null : 'You have version ${update.currentVersion}',
                trailing: update.checking ? const FCircularProgress.loader() : chevron,
                onTap: update.checking
                    ? null
                    : update.release != null
                        ? () => ref.read(updateProvider.notifier).install()
                        : _checkForUpdates,
              ),
            ],
          ),
        CardSection(
          children: <Widget>[
            ListRow(
              leading: IconBadge(FLucideIcons.logOut, color: theme.colors.destructive),
              title: 'Sign out',
              onTap: _signOut,
            ),
          ],
        ),
      ],
    );
  }
}
