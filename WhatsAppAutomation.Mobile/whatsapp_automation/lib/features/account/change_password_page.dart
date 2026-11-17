import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';

/// Change your own password. With [forced], the first screen after an admin chose your password: nothing else
/// opens until you choose your own. Same as the website's change-password page.
class ChangePasswordPage extends ConsumerStatefulWidget {
  const ChangePasswordPage({super.key, this.forced = false});

  final bool forced;

  @override
  ConsumerState<ChangePasswordPage> createState() => _ChangePasswordPageState();
}

class _ChangePasswordPageState extends ConsumerState<ChangePasswordPage> {
  final TextEditingController _current = TextEditingController();
  final TextEditingController _next = TextEditingController();
  final TextEditingController _repeat = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
    _repeat.dispose();
    super.dispose();
  }

  /// Admins need 12 characters, members 6 (the server checks this too, and refuses obvious ones).
  int get _minLength {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.user.kind == UserKind.member ? 6 : 12;
  }

  Future<void> _save() async {
    if (_current.text.isEmpty || _next.text.length < _minLength) {
      setState(() => _error = 'The new password needs at least $_minLength characters.');
      return;
    }

    if (_next.text != _repeat.text) {
      setState(() => _error = 'The two new passwords are not the same.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(sessionProvider.notifier).changePassword(_current.text, _next.text);
      if (mounted && !widget.forced) {
        showInfoToast(context, 'Password changed');
        Navigator.of(context).pop();
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final Widget form = ListView(
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        if (widget.forced) ...<Widget>[
          Text('Choose your password', style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w700)),
          const SizedBox(height: 6),
          Text(
            'Your admin set a first password for you. Choose your own before you start.',
            style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
          ),
          const SizedBox(height: 20),
        ],
        FTextField.password(
          control: FTextFieldControl.managed(controller: _current),
          label: Text(widget.forced ? 'Password you were given' : 'Current password'),
          enabled: !_saving,
        ),
        const SizedBox(height: 12),
        FTextField.password(
          control: FTextFieldControl.managed(controller: _next),
          label: const Text('New password'),
          description: Text('At least $_minLength characters. Not the user name, and not easy ones like 123456.'),
          enabled: !_saving,
        ),
        const SizedBox(height: 12),
        FTextField.password(
          control: FTextFieldControl.managed(controller: _repeat),
          label: const Text('New password again'),
          enabled: !_saving,
        ),
        if (_error != null) ...<Widget>[
          const SizedBox(height: 12),
          FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
        ],
        const SizedBox(height: 20),
        FButton(onPress: _saving ? null : _save, child: Text(_saving ? 'Saving…' : 'Save password')),
        if (widget.forced) ...<Widget>[
          const SizedBox(height: 8),
          FButton(
            variant: FButtonVariant.outline,
            onPress: _saving ? null : () => ref.read(sessionProvider.notifier).signOut(),
            child: const Text('Sign out'),
          ),
        ],
      ],
    );

    return widget.forced
        ? FScaffold(childPad: false, child: SafeArea(child: Center(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 520), child: form))))
        : PageScaffold(title: 'Change password', child: form);
  }
}
