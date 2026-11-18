import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../../core/memory_cache.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import '../sending/results_page.dart';
import '../sending/sending_api.dart';
import 'chat_bubble.dart';

/// How a person's message stands, in words. "read" is shown as Delivered: the app shows sent / delivered / failed.
({String label, IconData icon}) recipientStatus(String status) => switch (status) {
      'sent' => (label: 'Sent', icon: FLucideIcons.check),
      'delivered' || 'read' => (label: 'Delivered', icon: FLucideIcons.checkCheck),
      'failed' => (label: 'Failed', icon: FLucideIcons.circleX),
      'skipped' => (label: 'Skipped', icon: FLucideIcons.circleMinus),
      _ => (label: 'Waiting', icon: FLucideIcons.clock),
    };

/// The reason shown for a failed or skipped person: the server's words, or a plain fallback.
String problemReason(Recipient r) {
  final String? reason = r.reason?.trim();
  if (reason != null && reason.isNotEmpty) {
    return reason;
  }

  return r.status == 'skipped' ? 'Skipped' : 'WhatsApp did not accept the message';
}

/// One message's details: where sending stands, Pause / Continue / Stop, who got it, why some failed,
/// and "Send again to failed". Same as the website's chat page, plus the grouped reasons.
class BroadcastPage extends ConsumerStatefulWidget {
  const BroadcastPage({super.key, required this.id});

  final String id;

  @override
  ConsumerState<BroadcastPage> createState() => _BroadcastPageState();
}

class _BroadcastPageState extends ConsumerState<BroadcastPage> {
  late BroadcastDetails? _details = MemoryCache.get<BroadcastDetails>('broadcast:${widget.id}');
  Pacing? _pacing;
  String? _error;
  bool _busy = false;

  /// all, waiting, sent, delivered, problems, or `reason:<text>` for one failure reason.
  String _filter = 'all';
  Timer? _timer;
  StreamSubscription<void>? _live;

  @override
  void initState() {
    super.initState();
    _load(showErrors: true);
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) {
      if (_details?.summary.isLive ?? true) {
        _load(showErrors: false);
      }
    });
    // Slow polling as a fallback when the live connection is down.
    _timer = Timer.periodic(const Duration(seconds: 15), (_) {
      if (_details?.summary.isLive ?? false) {
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

  Future<void> _load({required bool showErrors}) async {
    try {
      final BroadcastsApi api = ref.read(broadcastsApiProvider);
      final (BroadcastDetails details, Pacing pacing) = await (api.get(widget.id), api.pacing()).wait;
      MemoryCache.set('broadcast:${widget.id}', details);
      if (mounted) {
        setState(() {
          _details = details;
          _pacing = pacing;
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_details == null) {
          setState(() => _error = errorText(e));
        } else if (showErrors) {
          showErrorToast(context, 'Could not refresh the message', e);
        }
      }
    }
  }

  Future<void> _run(String action) async {
    setState(() => _busy = true);
    try {
      await ref.read(broadcastsApiProvider).change(widget.id, action);
      await _load(showErrors: true);
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

  Future<void> _stop() async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Stop sending?',
      body: 'Messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.',
      confirm: 'Stop sending',
      cancel: 'Keep sending',
      destructive: true,
    );
    if (yes) {
      await _run('cancel');
    }
  }

  /// Puts the people who failed back in this message's queue (numbers not on WhatsApp are left out).
  Future<void> _sendAgain(BroadcastDetails d, List<Recipient> failed) async {
    if (failed.isEmpty) {
      return;
    }

    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.rotateCcw,
      title: 'Send again to ${failed.length} ${failed.length == 1 ? 'person' : 'people'}?',
      body: 'They go back in the queue and go out one by one. Numbers not on WhatsApp are left out.',
      confirm: 'Send again',
      cancel: 'Not now',
    );
    if (!yes || !mounted) {
      return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(sendingApiProvider).retry(widget.id, null);
      if (mounted) {
        showInfoToast(context, 'Sending again', description: 'They are back in the queue.');
      }

      await _load(showErrors: true);
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not sent again', e);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  String _statusLine(BroadcastSummary s) {
    final BroadcastCounts c = s.counts;
    switch (s.status) {
      case 'sending':
        final Pacing? p = _pacing;
        final String next = p?.waiting ?? (p?.nextSendAt != null ? 'Next one at ${clockText(p!.nextSendAt!)}.' : '');
        return 'Sending: ${c.sent} of ${c.willGet} sent. $next'.trim();
      case 'paused':
        return 'Paused after ${c.sent} of ${c.willGet}. ${s.pauseReason ?? 'Press Continue to go on.'}';
      case 'completed':
        return 'Done: ${c.sent} sent, ${c.delivered} delivered${c.failed > 0 ? ', ${c.failed} could not be sent' : ''}.';
      default:
        return 'Stopped: ${c.sent} sent, the rest will not get it.';
    }
  }

  bool _matches(Recipient r) {
    if (_filter.startsWith('reason:')) {
      return (r.status == 'failed' || r.status == 'skipped') && problemReason(r) == _filter.substring(7);
    }

    return switch (_filter) {
      'waiting' => r.status == 'pending',
      'sent' => r.status == 'sent',
      'delivered' => r.status == 'delivered' || r.status == 'read',
      'problems' => r.status == 'failed' || r.status == 'skipped',
      _ => true,
    };
  }

  Widget _stat(FThemeData theme, String value, String label, Color color, String filter) => Expanded(
        child: GestureDetector(
          onTap: () => setState(() => _filter = _filter == filter ? 'all' : filter),
          child: Container(
            padding: const EdgeInsets.symmetric(vertical: 12),
            decoration: BoxDecoration(
              color: color.withValues(alpha: _filter == filter ? 0.22 : 0.10),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: _filter == filter ? color : const Color(0x00000000)),
            ),
            child: Column(
              children: <Widget>[
                Text(value, style: theme.typography.display.xl.copyWith(color: color, fontWeight: FontWeight.w700)),
                Text(label, style: theme.typography.body.xs.copyWith(color: color, fontWeight: FontWeight.w600)),
              ],
            ),
          ),
        ),
      );

  Color _recipientColor(FThemeData theme, String status) => switch (status) {
        'sent' => theme.colors.mutedForeground,
        'delivered' || 'read' => theme.colors.primary,
        'failed' || 'skipped' => theme.colors.destructive,
        _ => const Color(0xFFD97706),
      };

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final BroadcastDetails? d = _details;
    if (d == null) {
      return PageScaffold(
        title: 'Message',
        child: _error == null
            ? const LoadingView()
            : EmptyView(
                icon: FLucideIcons.wifiOff,
                title: 'Could not load the message',
                text: _error!,
                action: () {
                  setState(() => _error = null);
                  _load(showErrors: true);
                },
                actionLabel: 'Try again',
              ),
      );
    }

    final BroadcastSummary s = d.summary;
    final BroadcastCounts c = s.counts;
    final double raw = c.willGet <= 0 ? 0 : c.sent / c.willGet;
    final double percent = raw > 1 ? 1 : raw;
    final List<(String?, String)> texts = d.text != null
        ? <(String?, String)>[(null, d.text!)]
        : <(String?, String?)>[('English', d.textEn), ('Hindi', d.textHi), ('Gujarati', d.textGu)]
            .where(((String?, String?) t) => t.$2 != null)
            .map(((String?, String?) t) => (t.$1, t.$2!))
            .toList();
    final List<Recipient> people = d.recipients.where(_matches).toList();
    final List<Recipient> failed = d.recipients.where((Recipient r) => r.status == 'failed' && r.contactId != null).toList();
    final Map<String, int> reasons = <String, int>{};
    for (final Recipient r in d.recipients.where((Recipient r) => r.status == 'failed' || r.status == 'skipped')) {
      final String reason = problemReason(r);
      reasons[reason] = (reasons[reason] ?? 0) + 1;
    }

    final List<MapEntry<String, int>> sortedReasons = reasons.entries.toList()
      ..sort((MapEntry<String, int> a, MapEntry<String, int> b) => b.value.compareTo(a.value));
    final Recipient? example = d.recipients.where((Recipient r) => r.text != null && r.status != 'skipped').firstOrNull;
    final TextStyle chip = TextStyle(
      backgroundColor: theme.colors.secondary,
      color: theme.colors.secondaryForeground,
      fontWeight: FontWeight.w600,
    );
    final Color tone = statusColor(context, s.status);
    final bool canSendAgain = failed.isNotEmpty;

    return PageScaffold(
      title: s.name,
      subtitle: '${dayMonthText(s.createdAt)} · ${timeText(s.createdAt)}',
      actions: <Widget>[
        FHeaderAction(
          icon: const Icon(FLucideIcons.listChecks),
          onPress: () async {
            await pushPage<void>(context, ResultsPage(id: widget.id));
            await _load(showErrors: false);
          },
        ),
      ],
      child: RefreshIndicator(
        onRefresh: () => _load(showErrors: true),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.only(bottom: 24),
          children: <Widget>[
            CardSection(
              padding: const EdgeInsets.all(16),
              children: <Widget>[
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: <Widget>[
                    Row(
                      children: <Widget>[
                        Pill(
                          switch (s.status) {
                            'sending' => 'Sending',
                            'paused' => 'Paused',
                            'completed' => 'Done',
                            _ => 'Stopped',
                          },
                          color: tone,
                        ),
                        const Spacer(),
                        Text(
                          '${(percent * 100).round()}%',
                          style: theme.typography.display.lg.copyWith(fontWeight: FontWeight.w700, color: tone),
                        ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    Text(_statusLine(s), style: theme.typography.body.sm),
                    const SizedBox(height: 10),
                    FDeterminateProgress(value: percent),
                    const SizedBox(height: 14),
                    Row(
                      children: <Widget>[
                        _stat(theme, '${c.sent}', 'Sent', theme.colors.mutedForeground, 'sent'),
                        const SizedBox(width: 8),
                        _stat(theme, '${c.delivered}', 'Delivered', theme.colors.primary, 'delivered'),
                        const SizedBox(width: 8),
                        _stat(theme, '${c.pending}', 'Waiting', const Color(0xFFD97706), 'waiting'),
                        const SizedBox(width: 8),
                        _stat(theme, '${c.failed + c.skipped}', 'Problems', theme.colors.destructive, 'problems'),
                      ],
                    ),
                    if (s.isLive) ...<Widget>[
                      const SizedBox(height: 14),
                      Row(
                        children: <Widget>[
                          Expanded(
                            child: s.status == 'sending'
                                ? FButton(
                                    variant: FButtonVariant.outline,
                                    prefix: const Icon(FLucideIcons.pause),
                                    onPress: _busy ? null : () => _run('pause'),
                                    child: const Text('Pause'),
                                  )
                                : FButton(
                                    prefix: const Icon(FLucideIcons.play),
                                    onPress: _busy ? null : () => _run('resume'),
                                    child: const Text('Continue'),
                                  ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: FButton(
                              variant: FButtonVariant.destructive,
                              prefix: const Icon(FLucideIcons.square),
                              onPress: _busy ? null : _stop,
                              child: const Text('Stop'),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ],
                ),
              ],
            ),
            if (sortedReasons.isNotEmpty)
              CardSection(
                title: 'Why some did not get it',
                children: <Widget>[
                  for (final MapEntry<String, int> r in sortedReasons)
                    ListRow(
                      leading: IconBadge(FLucideIcons.circleAlert, size: 34, color: theme.colors.destructive),
                      title: r.key,
                      subtitle: '${r.value} ${r.value == 1 ? 'person' : 'people'} · tap to see who',
                      trailing: _filter == 'reason:${r.key}'
                          ? Icon(FLucideIcons.check, size: 18, color: theme.colors.primary)
                          : const Icon(FLucideIcons.chevronRight, size: 18),
                      onTap: () => setState(() => _filter = _filter == 'reason:${r.key}' ? 'all' : 'reason:${r.key}'),
                    ),
                  if (canSendAgain)
                    Padding(
                      padding: const EdgeInsets.all(12),
                      child: FButton(
                        prefix: const Icon(FLucideIcons.refreshCw),
                        onPress: _busy ? null : () => _sendAgain(d, failed),
                        child: Text('Send again to ${failed.length} failed'),
                      ),
                    ),
                ],
              ),
            CardSection(
              title: 'Message',
              padding: const EdgeInsets.all(14),
              children: <Widget>[
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    if (d.attachment case final BroadcastAttachment f)
                      Container(
                        margin: const EdgeInsets.only(bottom: 10),
                        padding: const EdgeInsets.all(10),
                        decoration: BoxDecoration(color: theme.colors.secondary, borderRadius: BorderRadius.circular(10)),
                        child: Row(
                          children: <Widget>[
                            Icon(fileIcon(f.contentType), size: 20, color: theme.colors.secondaryForeground),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                f.fileName,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: theme.typography.body.sm.copyWith(color: theme.colors.secondaryForeground),
                              ),
                            ),
                            Text(
                              fileSizeText(f.sizeBytes),
                              style: theme.typography.body.xs.copyWith(color: theme.colors.secondaryForeground),
                            ),
                          ],
                        ),
                      ),
                    for (final (String? label, String text) in texts) ...<Widget>[
                      if (label != null)
                        Text(label, style: theme.typography.body.xs.copyWith(fontWeight: FontWeight.w700)),
                      Text.rich(TextSpan(children: highlightedText(text, chip: chip)), style: theme.typography.body.md),
                      const SizedBox(height: 8),
                    ],
                    if (example != null) ...<Widget>[
                      Container(height: 0.6, color: theme.colors.border, margin: const EdgeInsets.symmetric(vertical: 6)),
                      Text(
                        '${example.name} got:',
                        style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground, fontWeight: FontWeight.w600),
                      ),
                      const SizedBox(height: 4),
                      Text(example.text!, style: theme.typography.body.sm),
                    ],
                  ],
                ),
              ],
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              child: ChoiceChips<String>(
                options: <(String, String)>[
                  ('all', 'All ${c.total}'),
                  ('waiting', 'Waiting ${c.pending}'),
                  ('sent', 'Sent ${c.sent - c.delivered}'),
                  ('delivered', 'Delivered ${c.delivered}'),
                  ('problems', 'Problems ${c.failed + c.skipped}'),
                ],
                selected: _filter.startsWith('reason:') ? 'problems' : _filter,
                onChange: (String f) => setState(() => _filter = f),
              ),
            ),
            if (people.isEmpty)
              Padding(
                padding: const EdgeInsets.all(24),
                child: Text(
                  'Nobody here.',
                  textAlign: TextAlign.center,
                  style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                ),
              )
            else
              CardSection(
                children: <Widget>[
                  for (final Recipient r in people.take(500))
                    ListRow(
                      leading: PersonAvatar(name: r.name.isEmpty ? r.phoneNumber : r.name, size: 38),
                      title: r.name.isEmpty ? r.phoneNumber : r.name,
                      subtitle: r.status == 'failed' || r.status == 'skipped' ? problemReason(r) : r.phoneNumber,
                      trailing: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: <Widget>[
                          Icon(recipientStatus(r.status).icon, size: 14, color: _recipientColor(theme, r.status)),
                          const SizedBox(width: 4),
                          Text(
                            recipientStatus(r.status).label,
                            style: theme.typography.body.xs.copyWith(
                              color: _recipientColor(theme, r.status),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            if (people.length > 500)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Text(
                  'Showing 500 of ${people.length}. Use the filters above to narrow down.',
                  style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
