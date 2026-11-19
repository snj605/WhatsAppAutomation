import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../../core/ui.dart';
import 'whatsapp_gate.dart';

/// One test message and how it went (GET /api/v1/whatsapp/messages).
class OutgoingMessage {
  const OutgoingMessage({required this.id, required this.to, required this.text, required this.status, required this.createdAt});

  factory OutgoingMessage.fromJson(Json json) => OutgoingMessage(
        id: json['id'] as String,
        to: json['toPhoneNumber'] as String? ?? '',
        text: json['text'] as String? ?? '',
        status: json['status'] as String? ?? 'queued',
        createdAt: asDateOrNull(json['createdAt']) ?? DateTime.now(),
      );

  final String id;
  final String to;
  final String text;

  /// queued, sent, delivered, read or failed.
  final String status;
  final DateTime createdAt;
}

/// Send one message to one number, to check that the linked WhatsApp works. Same as the website's WhatsApp page.
class TestMessagePage extends ConsumerStatefulWidget {
  const TestMessagePage({super.key});

  @override
  ConsumerState<TestMessagePage> createState() => _TestMessagePageState();
}

class _TestMessagePageState extends ConsumerState<TestMessagePage> {
  final TextEditingController _to = TextEditingController(text: '+91');
  final TextEditingController _text = TextEditingController(text: '');
  List<OutgoingMessage>? _messages;
  bool _sending = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _to.dispose();
    _text.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final List<OutgoingMessage> list = asList(
        await ref.read(apiClientProvider).get('/api/v1/whatsapp/messages', query: <String, String>{'take': '20'}),
      ).map(OutgoingMessage.fromJson).toList();
      if (mounted) {
        setState(() => _messages = list);
      }
    } on Object {
      if (mounted) {
        setState(() => _messages = const <OutgoingMessage>[]);
      }
    }
  }

  Future<void> _send() async {
    final String to = _to.text.replaceAll(RegExp(r'\s'), '');
    if (!RegExp(r'^\+\d{8,15}$').hasMatch(to)) {
      setState(() => _error = 'Type the full number with country code, like +919825012345.');
      return;
    }

    if (!await ensureWhatsAppConnected(context, ref) || !mounted) {
      return;
    }

    if (_text.text.trim().isEmpty) {
      setState(() => _error = 'Type the message.');
      return;
    }

    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      await ref.read(apiClientProvider).post('/api/v1/whatsapp/messages', <String, dynamic>{'to': to, 'text': _text.text.trim()});
      if (mounted) {
        showInfoToast(context, 'Message sent', description: to);
        await _load();
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _sending = false);
      }
    }
  }

  ({String label, IconData icon, Color color}) _status(FThemeData theme, String s) => switch (s) {
        'sent' => (label: 'Sent', icon: FLucideIcons.check, color: theme.colors.primary),
        // Read counts as delivered: the app shows only sent / delivered / failed.
        'delivered' || 'read' => (label: 'Delivered', icon: FLucideIcons.checkCheck, color: theme.colors.primary),
        'failed' => (label: 'Failed', icon: FLucideIcons.circleX, color: theme.colors.destructive),
        _ => (label: 'Queued', icon: FLucideIcons.clock, color: theme.colors.mutedForeground),
      };

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return PageScaffold(
      title: 'Message one person',
      subtitle: 'Check that your WhatsApp sends',
      child: ListView(
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          CardSection(
            padding: const EdgeInsets.all(14),
            children: <Widget>[
              Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  FTextField(
                    control: FTextFieldControl.managed(controller: _to),
                    label: const Text('To'),
                    hint: '+919825012345',
                    keyboardType: TextInputType.phone,
                    inputFormatters: <TextInputFormatter>[FilteringTextInputFormatter.allow(RegExp(r'[0-9+\s]'))],
                    enabled: !_sending,
                  ),
                  const SizedBox(height: 12),
                  FTextField.multiline(
                    control: FTextFieldControl.managed(controller: _text),
                    label: const Text('Message'),
                    minLines: 3,
                    maxLines: 6,
                    enabled: !_sending,
                  ),
                  const SizedBox(height: 14),
                  FButton(
                    onPress: _sending ? null : _send,
                    prefix: _sending
                        ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
                        : const Icon(FLucideIcons.send),
                    child: const Text('Send'),
                  ),
                  if (_error != null) ...<Widget>[
                    const SizedBox(height: 12),
                    FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
                  ],
                ],
              ),
            ],
          ),
          if (_messages == null) const LoadingView(),
          if (_messages != null && _messages!.isNotEmpty)
            CardSection(
              title: 'Recent test messages',
              children: <Widget>[
                for (final OutgoingMessage m in _messages!)
                  ListRow(
                    leading: IconBadge(_status(theme, m.status).icon, color: _status(theme, m.status).color),
                    title: m.to,
                    subtitle: m.text,
                    trailing: Column(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: <Widget>[
                        Text(
                          _status(theme, m.status).label,
                          style: theme.typography.body.xs.copyWith(
                            color: _status(theme, m.status).color,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        Text(listTimeText(m.createdAt), style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
                      ],
                    ),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}
