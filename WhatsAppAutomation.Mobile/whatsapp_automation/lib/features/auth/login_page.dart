import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';

import '../../core/api/api_error.dart';
import 'session_controller.dart';

class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key, this.notice});

  /// Why the user is here again, for example "Please sign in again."
  final String? notice;

  @override
  ConsumerState<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends ConsumerState<LoginPage> {
  final TextEditingController _userName = TextEditingController();
  final TextEditingController _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _userName.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _signIn() async {
    if (_userName.text.trim().isEmpty || _password.text.isEmpty) {
      setState(() => _error = 'Type your user name and password.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(sessionProvider.notifier).signIn(_userName.text, _password.text);
    } on ApiException catch (error) {
      if (mounted) {
        setState(() => _error = error.message);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final String? message = _error ?? widget.notice;
    return FScaffold(
      childPad: false,
      child: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: AutofillGroup(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: <Widget>[
                    Icon(FLucideIcons.messagesSquare, size: 64, color: theme.colors.primary),
                    const SizedBox(height: 16),
                    Text(
                      'Welcome',
                      textAlign: TextAlign.center,
                      style: theme.typography.display.xl2.copyWith(fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Sign in with the user name and password you were given.',
                      textAlign: TextAlign.center,
                      style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                    ),
                    const SizedBox(height: 32),
                    FTextField(
                      control: FTextFieldControl.managed(controller: _userName),
                      enabled: !_busy,
                      label: const Text('User name'),
                      autocorrect: false,
                      textInputAction: TextInputAction.next,
                      autofillHints: const <String>[AutofillHints.username],
                      prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
                          FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.user)),
                    ),
                    const SizedBox(height: 16),
                    // Forui's password field has its own show / hide button.
                    FTextField.password(
                      control: FTextFieldControl.managed(controller: _password),
                      enabled: !_busy,
                      label: const Text('Password'),
                      textInputAction: TextInputAction.done,
                      onSubmit: (_) => _signIn(),
                      // The password field's builder also receives the show / hide state, which is not used here.
                      prefixBuilder: (BuildContext context, FTextFieldStyle style, ValueNotifier<bool> _, Set<FTextFieldVariant> variants) =>
                          FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.lock)),
                    ),
                    if (message != null) ...<Widget>[
                      const SizedBox(height: 16),
                      FAlert(
                        variant: _error != null ? FAlertVariant.destructive : FAlertVariant.primary,
                        icon: Icon(_error != null ? FLucideIcons.circleAlert : FLucideIcons.info),
                        title: Text(message),
                      ),
                    ],
                    const SizedBox(height: 24),
                    FButton(
                      onPress: _busy ? null : _signIn,
                      prefix: _busy ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
                      child: const Text('Sign in'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
