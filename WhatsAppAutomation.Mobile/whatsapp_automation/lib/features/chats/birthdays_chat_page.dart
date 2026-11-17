import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/theme.dart';
import '../../core/format.dart';
import '../../core/memory_cache.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import '../templates/templates_api.dart';
import 'broadcast_page.dart';
import 'chat_bubble.dart';
import 'chat_models.dart';
import 'chats_api.dart';
import 'message_bar.dart';
import 'send_flow.dart';

/// How each person's wish stands, in words (same as the website).
({String label, IconData icon}) birthdayStatus(String status) => switch (status) {
      'pending' => (label: 'Waiting', icon: FLucideIcons.clock),
      'sent' => (label: 'Sent', icon: FLucideIcons.check),
      // Read counts as delivered: the app shows only sent / delivered / failed.
      'delivered' || 'read' => (label: 'Delivered', icon: FLucideIcons.checkCheck),
      'failed' => (label: 'Failed', icon: FLucideIcons.circleX),
      'skipped' => (label: 'Skipped', icon: FLucideIcons.circleMinus),
      _ => (label: 'Not sent', icon: FLucideIcons.circle),
    };

/// Today's Birthdays: one chat for the whole day. A checklist shows everyone with a birthday today and whether they
/// got wishes yet; the ones not wished yet are ticked, and the message box sends them a personal wish.
class BirthdaysChatPage extends ConsumerStatefulWidget {
  const BirthdaysChatPage({super.key});

  @override
  ConsumerState<BirthdaysChatPage> createState() => _BirthdaysChatPageState();
}

class _BirthdaysChatPageState extends ConsumerState<BirthdaysChatPage> {
  BirthdaysChat? _chat = MemoryCache.get<BirthdaysChat>('chat:birthdays');
  String? _error;
  StreamSubscription<void>? _live;
  Set<String> _selected = <String>{};
  bool _touched = false;
  bool _sending = false;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _load(showErrors: true);
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => _load(showErrors: false));
    _timer = Timer.periodic(const Duration(seconds: 15), (_) {
      if (_chat?.people.any((BirthdayPerson p) => p.status == 'pending') ?? false) {
        _load(showErrors: false);
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _live?.cancel();
    super.dispose();
  }

  Iterable<BirthdayPerson> get _notSent =>
      (_chat?.people ?? const <BirthdayPerson>[]).where((BirthdayPerson p) => p.status == 'not_sent' && !p.optedOut);

  Future<void> _load({required bool showErrors}) async {
    try {
      final BirthdaysChat chat = await ref.read(chatsApiProvider).birthdays();
      if (!mounted) {
        return;
      }

      MemoryCache.set('chat:birthdays', chat);
      setState(() {
        _chat = chat;
        _error = null;
        // Until the user changes it, the ones not wished yet are ticked.
        if (!_touched) {
          _selected = _notSent.map((BirthdayPerson p) => p.contactId).toSet();
        }
      });
    } on Object catch (e) {
      if (mounted && _chat == null) {
        setState(() => _error = errorText(e));
      } else if (showErrors && mounted) {
        showErrorToast(context, 'Could not refresh birthdays', e);
      }
    }
  }

  void _toggle(BirthdayPerson p) {
    if (p.optedOut) {
      return;
    }

    setState(() {
      _touched = true;
      _selected = Set<String>.of(_selected);
      if (!_selected.remove(p.contactId)) {
        _selected.add(p.contactId);
      }
    });
  }

  Future<bool> _send(String text, String? attachmentId) async {
    if (_selected.isEmpty) {
      return false;
    }

    setState(() => _sending = true);
    try {
      final BroadcastInput input = BroadcastInput(
        name: 'Birthday wishes ${dayMonthText(DateTime.now())}',
        text: text,
        attachmentId: attachmentId,
        contactIds: _selected.toList(),
        forBirthday: true,
      );
      final BroadcastSummary? created = await confirmAndSend(context, ref, input);
      if (created != null) {
        _touched = false;
        await _load(showErrors: false);
      }

      return created != null;
    } finally {
      if (mounted) {
        setState(() => _sending = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final BirthdaysChat? chat = _chat;
    final int total = chat?.people.length ?? 0;
    final int done = chat?.people.where((BirthdayPerson p) => p.status != 'not_sent').length ?? 0;
    return PageScaffold(
      title: 'Birthdays today',
      leading: const PersonAvatar(name: 'Birthdays', size: 36, icon: FLucideIcons.cake),
      subtitle: chat == null ? null : '${longDayText(chat.date)} · $done of $total wished',
      child: Column(
        children: <Widget>[
          Expanded(
            child: ColoredBox(
              color: chatWallpaper(context),
              child: chat == null
                  ? (_error == null
                      ? const LoadingView()
                      : EmptyView(
                          icon: FLucideIcons.wifiOff,
                          title: 'Could not load birthdays',
                          text: _error!,
                          action: () {
                            setState(() => _error = null);
                            _load(showErrors: true);
                          },
                          actionLabel: 'Try again',
                        ))
                  : ListView(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      children: <Widget>[
                        CardSection(
                          title: '$total ${total == 1 ? 'birthday' : 'birthdays'} today',
                          trailing: total == 0
                              ? null
                              : FButton(
                                  variant: FButtonVariant.ghost,
                                  size: FButtonSizeVariant.sm,
                                  mainAxisSize: MainAxisSize.min,
                                  onPress: () => setState(() {
                                    _touched = true;
                                    _selected = _notSent.map((BirthdayPerson p) => p.contactId).toSet();
                                  }),
                                  child: const Text('Select not sent'),
                                ),
                          children: <Widget>[
                            Padding(
                              padding: const EdgeInsets.fromLTRB(14, 14, 14, 10),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: <Widget>[
                                  FDeterminateProgress(value: total == 0 ? 0 : done / total),
                                  const SizedBox(height: 6),
                                  Text(
                                    total == 0 ? 'No birthdays today.' : '$done of $total wished',
                                    style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                                  ),
                                ],
                              ),
                            ),
                            for (final BirthdayPerson p in chat.people)
                              ListRow(
                                leading: FCheckbox(
                                  value: _selected.contains(p.contactId),
                                  enabled: !p.optedOut,
                                  onChange: (_) => _toggle(p),
                                  semanticsLabel: 'Wish ${p.name}',
                                ),
                                title: p.name,
                                subtitle:
                                    '${p.turnsAge != null ? 'Turns ${p.turnsAge} · ' : ''}${p.optedOut ? 'No broadcasts' : p.phoneNumber}',
                                dimmed: p.optedOut,
                                trailing: Pill(
                                  birthdayStatus(p.status).label,
                                  color: p.status == 'not_sent'
                                      ? theme.colors.mutedForeground
                                      : p.status == 'failed' || p.status == 'skipped'
                                          ? theme.colors.destructive
                                          : theme.colors.primary,
                                ),
                                onTap: () => _toggle(p),
                              ),
                          ],
                        ),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                          child: Column(
                            children: messagesWithDayLines(
                              chat.messages,
                              (ChatMessage m) => pushPage<void>(context, BroadcastPage(id: m.summary.id)),
                            ),
                          ),
                        ),
                      ],
                    ),
            ),
          ),
          if (chat != null)
            MessageBar(
              initialText: birthdayText,
              hint: 'Write your birthday wish',
              disabled: _selected.isEmpty,
              disabledNote: 'Tick who should get the wish.',
              busy: _sending,
              onSend: _send,
            ),
        ],
      ),
    );
  }
}
