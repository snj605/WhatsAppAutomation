import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../chats/broadcast_page.dart';
import '../sending/sending_api.dart';
import 'broadcasts_api.dart';

/// The bar at the top of the app while something is being sent: progress, Pause / Continue / Stop.
/// Hidden when nothing is sending. Same as the website's sending bar.
class SendingBar extends ConsumerStatefulWidget {
  const SendingBar({super.key});

  @override
  ConsumerState<SendingBar> createState() => _SendingBarState();
}

class _SendingBarState extends ConsumerState<SendingBar> {
  List<QueueItem> _queue = <QueueItem>[];
  SendQueue? _pacing;
  bool _busy = false;
  Timer? _timer;
  StreamSubscription<void>? _live;

  @override
  void initState() {
    super.initState();
    _load();
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => _load());
    _timer = Timer.periodic(const Duration(seconds: 15), (_) => _load());
  }

  @override
  void dispose() {
    _timer?.cancel();
    _live?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      // One call: the queue in sending order (occasions first, then the user's order) with the pace.
      final SendQueue queue = await ref.read(sendingApiProvider).queue();
      if (mounted) {
        setState(() {
          _queue = queue.items;
          _pacing = queue;
        });
      }
    } on Object {
      // The bar keeps what it had; screens show errors in full.
    }
  }

  Future<void> _run(QueueItem b, String action) async {
    setState(() => _busy = true);
    try {
      await ref.read(broadcastsApiProvider).change(b.id, action);
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not changed', e);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _stop(QueueItem b) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Stop sending?',
      body: '"${b.name}": messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.',
      confirm: 'Stop sending',
      cancel: 'Keep sending',
      destructive: true,
    );
    if (yes) {
      await _run(b, 'cancel');
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_queue.isEmpty) {
      return const SizedBox.shrink();
    }

    final FThemeData theme = context.theme;
    final QueueItem b = _queue.first;
    final int willGet = b.counts.willGet;
    final double percent = willGet == 0 ? 0 : (b.counts.sent / willGet).clamp(0, 1).toDouble();
    final String note = b.status == 'paused'
        ? (b.pauseReason ?? 'Paused')
        : _pacing?.waiting ?? (_pacing?.nextSendAt != null ? 'Next at ${clockText(_pacing!.nextSendAt!)}' : 'Sending one by one');
    final int more = _queue.length - 1;

    return GestureDetector(
      onTap: () => pushPage<void>(context, BroadcastPage(id: b.id)),
      child: Container(
        padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
        decoration: BoxDecoration(
          color: theme.colors.secondary,
          border: Border(bottom: BorderSide(color: theme.colors.border)),
        ),
        child: Row(
          children: <Widget>[
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Text(
                    '${b.status == 'paused' ? 'Paused' : 'Sending'}: ${b.name}${more > 0 ? ' (+$more more)' : ''}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.typography.body.sm.copyWith(fontWeight: FontWeight.w600),
                  ),
                  const SizedBox(height: 4),
                  FDeterminateProgress(value: percent),
                  const SizedBox(height: 4),
                  Text(
                    '${b.counts.sent} of $willGet · $note',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            FButton.icon(
              variant: FButtonVariant.ghost,
              onPress: _busy ? null : () => _run(b, b.status == 'paused' ? 'resume' : 'pause'),
              child: Icon(b.status == 'paused' ? FLucideIcons.play : FLucideIcons.pause),
            ),
            FButton.icon(
              variant: FButtonVariant.ghost,
              onPress: _busy ? null : () => _stop(b),
              child: const Icon(FLucideIcons.square),
            ),
          ],
        ),
      ),
    );
  }
}
