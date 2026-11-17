import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';

import 'session_controller.dart';

/// Shown when the app is locked: the owner opens it with fingerprint or face.
class LockPage extends ConsumerStatefulWidget {
  const LockPage({super.key, required this.userName});

  final String userName;

  @override
  ConsumerState<LockPage> createState() => _LockPageState();
}

class _LockPageState extends ConsumerState<LockPage> {
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    // Ask at once, so opening the app is a single touch.
    WidgetsBinding.instance.addPostFrameCallback((_) => _unlock());
  }

  Future<void> _unlock() async {
    if (_busy) {
      return;
    }

    setState(() => _busy = true);
    await ref.read(sessionProvider.notifier).unlock();
    if (mounted) {
      setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return FScaffold(
      childPad: false,
      child: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  Icon(FLucideIcons.fingerprint, size: 72, color: theme.colors.primary),
                  const SizedBox(height: 16),
                  Text(
                    'Locked',
                    textAlign: TextAlign.center,
                    style: theme.typography.display.xl2.copyWith(fontWeight: FontWeight.w600),
                  ),
                  if (widget.userName.isNotEmpty) ...<Widget>[
                    const SizedBox(height: 4),
                    Text(widget.userName, textAlign: TextAlign.center, style: theme.typography.body.md),
                  ],
                  const SizedBox(height: 32),
                  FButton(
                    onPress: _busy ? null : _unlock,
                    prefix: const Icon(FLucideIcons.lockOpen),
                    child: const Text('Unlock'),
                  ),
                  const SizedBox(height: 8),
                  FButton(
                    variant: FButtonVariant.ghost,
                    onPress: _busy ? null : () => ref.read(sessionProvider.notifier).signOut(),
                    child: const Text('Sign in with password instead'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
