import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../preview/wa_bubble.dart';
import '../sending/sending_api.dart';
import 'broadcasts_api.dart';

/// Which home tab is open, so the strip can hide on the Sending tab (it has the full view).
final ValueNotifier<String> currentHomeTab = ValueNotifier<String>('chats');

/// Set by the strip to ask the home page to open a tab (the strip sits above every screen, outside the home page).
final ValueNotifier<String?> openHomeTabRequest = ValueNotifier<String?>(null);

/// The slim strip at the top of every screen, inner pages included, while something is sending, paused or waiting:
/// what, how many of how many, the next time or why it waits, Pause or Continue and Stop. Tapping it opens the full
/// queue (the Sending tab). Not shown when nothing is queued, nor on the Sending tab. Same as the website.
///
/// It wraps the whole app (above the navigator), so it stays while pages open and close.
class SendingBar extends ConsumerStatefulWidget {
  const SendingBar({super.key, required this.enabled, required this.onOpen, required this.navigator, required this.child});

  /// Signed in and allowed to send; otherwise only [child] is shown.
  final bool enabled;

  /// Opens the full queue.
  final VoidCallback onOpen;

  /// The app's navigator: the strip sits above it, so its dialogs and toasts open through it.
  final GlobalKey<NavigatorState> navigator;

  final Widget child;

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
    currentHomeTab.addListener(_changed);
    if (widget.enabled) {
      _start();
    }
  }

  @override
  void didUpdateWidget(SendingBar oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.enabled && !oldWidget.enabled) {
      _start();
    } else if (!widget.enabled && oldWidget.enabled) {
      _stopWatching();
      _queue = <QueueItem>[];
      _pacing = null;
    }
  }

  @override
  void dispose() {
    currentHomeTab.removeListener(_changed);
    _stopWatching();
    super.dispose();
  }

  void _changed() {
    if (mounted) {
      setState(() {});
    }
  }

  void _start() {
    _stopWatching();
    _load();
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => _load());
    _timer = Timer.periodic(const Duration(seconds: 10), (_) => _load());
  }

  void _stopWatching() {
    _timer?.cancel();
    _timer = null;
    _live?.cancel();
    _live = null;
  }

  Future<void> _load() async {
    if (!widget.enabled) {
      return;
    }

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
      // The strip keeps what it had; screens show errors in full.
    }
  }

  Future<void> _run(QueueItem b, String action) async {
    setState(() => _busy = true);
    try {
      await ref.read(broadcastsApiProvider).change(b.id, action);
      await _load();
    } on Object catch (e) {
      final BuildContext? context = widget.navigator.currentContext;
      if (context != null && context.mounted) {
        showErrorToast(context, 'Not changed', e);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _stop(QueueItem b) async {
    final BuildContext? context = widget.navigator.currentContext;
    if (context == null) {
      return;
    }

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

  /// Why it waits or when the next one goes, in plain words.
  String _note(QueueItem b) {
    if (b.status == 'paused') {
      return b.pauseReason ?? 'Paused';
    }

    final SendQueue? p = _pacing;
    if (p != null && !p.connected) {
      return 'WhatsApp is not connected';
    }

    if (p?.waiting case final String waiting) {
      return waiting;
    }

    final DateTime? next = p?.nextSendAt;
    if (next == null) {
      return 'Sending one by one';
    }

    final DateTime local = next.toLocal();
    return local.isBefore(DateTime.now().add(const Duration(seconds: 5))) ? 'Sending now' : 'Next at ${waTimeText(local)}';
  }

  @override
  Widget build(BuildContext context) {
    final bool shown = widget.enabled && _queue.isNotEmpty && currentHomeTab.value != 'sending';
    if (!shown) {
      return widget.child;
    }

    final FThemeData theme = context.theme;
    final QueueItem b = _queue.first;
    final bool paused = b.status == 'paused';
    final int willGet = b.counts.willGet;
    final double percent = willGet == 0 ? 0 : (b.counts.sent / willGet).clamp(0, 1).toDouble();
    final int more = _queue.length - 1;
    final Color tone = paused ? const Color(0xFFDC6803) : theme.colors.primary;

    final Widget strip = ColoredBox(
      color: theme.colors.secondary,
      child: SafeArea(
        bottom: false,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: widget.onOpen,
          child: Semantics(
            label: '${paused ? 'Paused' : 'Sending'} ${b.name}: ${b.counts.sent} of $willGet sent. Tap to see the queue.',
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 6, 4, 6),
                  child: Row(
                    children: <Widget>[
                      Icon(paused ? FLucideIcons.pause : FLucideIcons.send, size: 18, color: tone),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: <Widget>[
                            Text(
                              '${paused ? 'Paused' : 'Sending'} · ${b.name} · ${b.counts.sent} of $willGet'
                              '${more > 0 ? ' · +$more waiting' : ''}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.typography.body.sm.copyWith(fontWeight: FontWeight.w600),
                            ),
                            Text(
                              '${_note(b)} · tap to see the queue',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                            ),
                          ],
                        ),
                      ),
                      FButton.icon(
                        variant: FButtonVariant.ghost,
                        onPress: _busy ? null : () => _run(b, paused ? 'resume' : 'pause'),
                        child: Icon(paused ? FLucideIcons.play : FLucideIcons.pause),
                      ),
                      FButton.icon(
                        variant: FButtonVariant.ghost,
                        onPress: _busy ? null : () => _stop(b),
                        child: const Icon(FLucideIcons.square),
                      ),
                    ],
                  ),
                ),
                // Progress as a thin line along the bottom.
                Container(
                  height: 3,
                  color: theme.colors.border,
                  alignment: Alignment.centerLeft,
                  child: FractionallySizedBox(widthFactor: percent, child: Container(color: tone)),
                ),
              ],
            ),
          ),
        ),
      ),
    );

    // The pages below no longer need room for the status bar: the strip has it.
    return Column(
      children: <Widget>[
        strip,
        Expanded(child: MediaQuery.removePadding(context: context, removeTop: true, child: widget.child)),
      ],
    );
  }
}
