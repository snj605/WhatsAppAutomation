import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../core/api/api_error.dart';
import '../core/providers.dart';

/// Before an important change (sending hours, an occasion's automatic sending, deleting), the owner confirms it is
/// them: fingerprint or face (or the phone's own PIN), or the password when the phone has none of those. So nothing
/// changes by a slip of the finger while scrolling. Returns true when confirmed.
Future<bool> confirmIdentity(BuildContext context, WidgetRef ref, String reason) async {
  final bool biometrics = await ref.read(biometricServiceProvider).isAvailable();
  if (biometrics) {
    return ref.read(biometricServiceProvider).confirmOwner(reason);
  }

  if (!context.mounted) {
    return false;
  }

  return await showFDialog<bool>(
        context: context,
        builder: (BuildContext context, style, Animation<double> animation) => FDialog(
          style: style,
          animation: animation,
          builder: (BuildContext context, style) => _PasswordCheck(reason: reason, style: style),
        ),
      ) ??
      false;
}

class _PasswordCheck extends ConsumerStatefulWidget {
  const _PasswordCheck({required this.reason, required this.style});

  final String reason;
  final FDialogStyle style;

  @override
  ConsumerState<_PasswordCheck> createState() => _PasswordCheckState();
}

class _PasswordCheckState extends ConsumerState<_PasswordCheck> {
  final TextEditingController _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  Future<void> _check() async {
    if (_password.text.isEmpty) {
      setState(() => _error = 'Type your password.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(apiClientProvider).post('/api/v1/auth/confirm-password', <String, Object?>{'password': _password.text});
      if (mounted) {
        Navigator.of(context).pop(true);
      }
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            const Padding(
              padding: EdgeInsets.only(left: 8, right: 8, bottom: 12),
              child: Icon(FLucideIcons.shieldCheck, size: 40),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 8, right: 8, bottom: 9),
              child: DefaultTextStyle.merge(style: widget.style.titleTextStyle, child: const Text("Confirm it's you")),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
              child: DefaultTextStyle.merge(style: widget.style.bodyTextStyle, child: Text('${widget.reason} Type your password.')),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
              child: FTextField.password(
                control: FTextFieldControl.managed(controller: _password),
                enabled: !_busy,
                autofocus: true,
                textInputAction: TextInputAction.done,
                onSubmit: (_) => _check(),
              ),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
                child: Text(_error!, style: TextStyle(color: context.theme.colors.destructive)),
              ),
            Column(
              mainAxisSize: MainAxisSize.min,
              spacing: 10,
              children: <Widget>[
                FButton(onPress: _busy ? null : _check, child: const Text('Confirm')),
                FButton(
                  variant: FButtonVariant.outline,
                  onPress: _busy ? null : () => Navigator.of(context).pop(false),
                  child: const Text('Cancel'),
                ),
              ],
            ),
          ],
        ),
      );
}
