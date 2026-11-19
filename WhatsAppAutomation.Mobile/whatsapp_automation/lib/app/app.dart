import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../core/config.dart';
import '../features/account/change_password_page.dart';
import '../features/admin/dashboard_page.dart';
import '../features/auth/auth_models.dart';
import '../features/auth/lock_page.dart';
import '../features/auth/login_page.dart';
import '../features/auth/session_controller.dart';
import '../features/broadcasts/sending_bar.dart';
import '../features/home/home_page.dart';
import '../features/update/update_gate.dart';
import 'theme.dart';
import 'theme_mode.dart';

class App extends ConsumerStatefulWidget {
  const App({super.key});

  @override
  ConsumerState<App> createState() => _AppState();
}

class _AppState extends ConsumerState<App> with WidgetsBindingObserver {
  final GlobalKey<NavigatorState> _navigator = GlobalKey<NavigatorState>();
  DateTime? _leftAt;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      _leftAt ??= DateTime.now();
    } else if (state == AppLifecycleState.resumed) {
      final DateTime? leftAt = _leftAt;
      _leftAt = null;
      if (leftAt != null && DateTime.now().difference(leftAt) >= AppConfig.lockAfter) {
        ref.read(sessionProvider.notifier).lock();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    // Leaving the signed-in state closes every open screen, so nothing stays visible above the gate.
    ref.listen<SessionState>(sessionProvider, (SessionState? previous, SessionState next) {
      if (next is! SignedIn) {
        _navigator.currentState?.popUntil((Route<dynamic> route) => route.isFirst);
      }
    });

    final SessionState session = ref.watch(sessionProvider);
    // The sending strip shows above every screen for a signed-in user who may send (not on the forced password
    // change, the lock screen or the platform admin's screens).
    final bool strip = session is SignedIn &&
        !session.user.mustChangePassword &&
        session.user.kind != UserKind.platformAdmin &&
        session.can('messages.send');
    return MaterialApp(
      title: 'WhatsApp Automation',
      debugShowCheckedModeBanner: false,
      // Light, dark, or the phone's setting: the user chooses under Me.
      themeMode: ref.watch(themeModeProvider),
      theme: AppTheme.materialLight,
      darkTheme: AppTheme.materialDark,
      // Forui's own texts (for example the show/hide password button). The app stays in English:
      // supportedLocales is left at its default.
      localizationsDelegates: FLocalizations.localizationsDelegates,
      navigatorKey: _navigator,
      // FTheme sits above the navigator so every screen and dialog gets the Forui theme.
      builder: (BuildContext context, Widget? child) => FTheme(
        data: Theme.brightnessOf(context) == Brightness.light ? AppTheme.light : AppTheme.dark,
        child: FToaster(
          child: FTooltipGroup(
            child: UpdateGate(
              child: SendingBar(
                enabled: strip,
                navigator: _navigator,
                // The full queue: back to the home screen, on the Sending tab.
                onOpen: () {
                  _navigator.currentState?.popUntil((Route<dynamic> route) => route.isFirst);
                  openHomeTabRequest.value = 'sending';
                },
                child: child!,
              ),
            ),
          ),
        ),
      ),
      home: switch (session) {
        SessionStarting() => const _Starting(),
        SessionUnreachable(:final String message) => _Unreachable(message),
        SignedOut(:final String? message) => LoginPage(notice: message),
        Locked(:final String userName) => LockPage(userName: userName),
        SignedIn(:final AppUser user) when user.mustChangePassword => const ChangePasswordPage(forced: true),
        SignedIn(:final AppUser user) when user.kind == UserKind.platformAdmin => const PlatformHomePage(),
        SignedIn() => const HomePage(),
      },
    );
  }
}

class _Starting extends StatelessWidget {
  const _Starting();

  @override
  Widget build(BuildContext context) =>
      const FScaffold(child: Center(child: FCircularProgress.loader(semanticsLabel: 'Starting')));
}

class _Unreachable extends ConsumerWidget {
  const _Unreachable(this.message);

  final String message;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    return FScaffold(
      childPad: false,
      child: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Icon(FLucideIcons.wifiOff, size: 56, color: theme.colors.mutedForeground),
                const SizedBox(height: 16),
                Text(message, textAlign: TextAlign.center, style: theme.typography.body.md),
                const SizedBox(height: 24),
                FButton(
                  onPress: () => ref.read(sessionProvider.notifier).start(),
                  child: const Text('Try again'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
