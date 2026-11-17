import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/ui.dart';

/// Asks an admin for a new password for someone else. Returns it, or null when cancelled.
class SetPasswordPage extends StatefulWidget {
  const SetPasswordPage({super.key, required this.name, required this.minLength});

  final String name;
  final int minLength;

  @override
  State<SetPasswordPage> createState() => _SetPasswordPageState();
}

class _SetPasswordPageState extends State<SetPasswordPage> {
  final TextEditingController _password = TextEditingController();
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  void _done() {
    if (_password.text.length < widget.minLength) {
      setState(() => _error = 'The password needs at least ${widget.minLength} characters.');
      return;
    }

    Navigator.of(context).pop(_password.text);
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'New password',
        subtitle: widget.name,
        footer: SafeArea(
          top: false,
          child: Padding(padding: const EdgeInsets.all(12), child: FButton(onPress: _done, child: const Text('Set password'))),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField.password(
              control: FTextFieldControl.managed(controller: _password),
              label: Text('New password for ${widget.name}'),
              description: Text('At least ${widget.minLength} characters. They are signed out everywhere and choose their own at the next sign-in.'),
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}

/// User names may use letters, digits, ".", "_" and "-" (same rule as the server).
bool isValidUserName(String value) => RegExp(r'^[a-zA-Z0-9._-]{1,50}$').hasMatch(value);
