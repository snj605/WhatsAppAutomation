import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/providers.dart';
import '../../core/storage/secure_store.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import '../birthdays/birthdays_page.dart';
import '../broadcasts/sending_bar.dart';
import '../chats/chats_page.dart';
import '../contacts/contacts_page.dart';
import '../whatsapp/whatsapp_controller.dart';
import '../whatsapp/whatsapp_gate.dart';
import '../whatsapp/whatsapp_models.dart';
import 'me_page.dart';

/// The app after sign-in: four tabs, like the website, with the sending bar on top.
class HomePage extends ConsumerStatefulWidget {
  const HomePage({super.key});

  @override
  ConsumerState<HomePage> createState() => _HomePageState();
}

const List<(IconData, String)> _tabs = <(IconData, String)>[
  (FLucideIcons.messageCircle, 'Chats'),
  (FLucideIcons.users, 'Contacts'),
  (FLucideIcons.cake, 'Birthdays'),
  (FLucideIcons.user, 'Me'),
];

/// The tabs as a column on the left, for tablets and landscape.
class _SideBar extends StatelessWidget {
  const _SideBar({required this.index, required this.onChange});

  final int index;
  final ValueChanged<int> onChange;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Container(
      width: 104,
      decoration: BoxDecoration(
        color: theme.colors.card,
        border: Border(right: BorderSide(color: theme.colors.border, width: 0.6)),
      ),
      child: SafeArea(
        right: false,
        child: Column(
          children: <Widget>[
            const SizedBox(height: 12),
            for (int i = 0; i < _tabs.length; i++)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                child: Semantics(
                  button: true,
                  selected: i == index,
                  label: _tabs[i].$2,
                  child: GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: () => onChange(i),
                    child: Container(
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      decoration: BoxDecoration(
                        color: i == index ? theme.colors.primary.withValues(alpha: 0.12) : null,
                        borderRadius: BorderRadius.circular(14),
                      ),
                      child: Column(
                        children: <Widget>[
                          Icon(_tabs[i].$1, size: 24, color: i == index ? theme.colors.primary : theme.colors.mutedForeground),
                          const SizedBox(height: 6),
                          Text(
                            _tabs[i].$2,
                            style: theme.typography.body.xs.copyWith(
                              color: i == index ? theme.colors.primary : theme.colors.mutedForeground,
                              fontWeight: i == index ? FontWeight.w700 : FontWeight.w500,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _HomePageState extends ConsumerState<HomePage> {
  final GlobalKey<ChatsTabState> _chats = GlobalKey<ChatsTabState>();
  final GlobalKey<ContactsTabState> _contacts = GlobalKey<ContactsTabState>();
  final GlobalKey<BirthdaysTabState> _birthdays = GlobalKey<BirthdaysTabState>();
  int _tab = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _offerBiometrics());
  }

  /// Asked once after the first sign-in on a phone that has a fingerprint or face registered.
  Future<void> _offerBiometrics() async {
    final SecureStore store = ref.read(secureStoreProvider);
    final SessionController session = ref.read(sessionProvider.notifier);
    if (await store.read(SecureStore.biometricsAsked) != null ||
        await session.isBiometricsOn() ||
        !await ref.read(biometricServiceProvider).isAvailable()) {
      return;
    }

    await store.write(SecureStore.biometricsAsked, '1');
    if (!mounted) {
      return;
    }

    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.fingerprint,
      title: 'Open the app with your fingerprint or face?',
      body: 'Next time you will not need to type your password. You can change this later under Me.',
      confirm: 'Yes, turn on',
      cancel: 'Not now',
    );
    if (yes) {
      await session.setBiometrics(on: true);
    }
  }

  void _select(int index) {
    setState(() => _tab = index);
    // Fresh data when coming back to a tab.
    switch (index) {
      case 0:
        _chats.currentState?.reload();
      case 2:
        _birthdays.currentState?.reload();
    }
  }

  List<Widget> _actions(SessionState session) {
    final bool canSend = session is SignedIn && session.can('messages.send');
    final bool canModules = session is SignedIn && session.can('modules.manage');
    final bool canManageContacts = session is SignedIn && session.can('contacts.manage');
    return switch (_tab) {
      0 when canSend => <Widget>[
          FHeaderAction(
            icon: const Icon(FLucideIcons.squarePen),
            onPress: () => _chats.currentState?.newMessage(),
          ),
        ],
      1 => <Widget>[
          FHeaderAction(icon: const Icon(FLucideIcons.tags), onPress: () => _contacts.currentState?.openLists()),
          if (canManageContacts)
            FHeaderAction(icon: const Icon(FLucideIcons.userPlus), onPress: () => _contacts.currentState?.add()),
        ],
      2 when canModules => <Widget>[
          FHeaderAction(
            icon: const Icon(FLucideIcons.alarmClock),
            onPress: () => _birthdays.currentState?.openSettings(),
          ),
        ],
      _ => const <Widget>[],
    };
  }

  @override
  Widget build(BuildContext context) {
    const List<String> titles = <String>['Chats', 'Contacts', 'Birthdays', 'Me'];
    final SessionState session = ref.watch(sessionProvider);
    // When WhatsApp connects again, messages that were paused while it was not linked wait for the user's
    // decision: resume or stop them.
    ref.listen<WhatsAppState>(whatsAppProvider, (WhatsAppState? previous, WhatsAppState next) {
      final WhatsAppAccount? before = previous?.account;
      if (before != null && !before.isConnected && (next.account?.isConnected ?? false)) {
        offerWaitingBroadcasts(context, ref);
      }
    });
    // Tablets and phones in landscape get a side bar and a centred page instead of the bottom bar.
    final bool wide = MediaQuery.sizeOf(context).width >= 720;
    final Widget pages = IndexedStack(
      index: _tab,
      children: <Widget>[
        ChatsTab(key: _chats),
        ContactsTab(key: _contacts),
        BirthdaysTab(key: _birthdays),
        const MePage(),
      ],
    );
    return FScaffold(
      childPad: false,
      header: FHeader(title: Text(titles[_tab]), suffixes: _actions(session)),
      footer: wide
          ? null
          : FBottomNavigationBar(
              index: _tab,
              onChange: _select,
              children: <FBottomNavigationBarItem>[
                for (final (IconData icon, String label) in _tabs)
                  FBottomNavigationBarItem(icon: Icon(icon), label: Text(label)),
              ],
            ),
      child: Column(
        children: <Widget>[
          const SendingBar(),
          Expanded(
            child: wide
                ? Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: <Widget>[
                      _SideBar(index: _tab, onChange: _select),
                      Expanded(
                        child: Align(
                          alignment: Alignment.topCenter,
                          child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 860), child: pages),
                        ),
                      ),
                    ],
                  )
                : pages,
          ),
        ],
      ),
    );
  }
}
