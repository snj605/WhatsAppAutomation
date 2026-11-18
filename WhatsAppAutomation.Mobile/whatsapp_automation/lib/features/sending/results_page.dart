import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import 'sending_api.dart';
import 'status_look.dart';

const List<(String, String)> _lists = <(String, String)>[
  ('all', 'All'),
  ('pending', 'Waiting'),
  ('sent', 'Sent'),
  ('delivered', 'Delivered'),
  ('read', 'Read'),
  ('failed', 'Failed'),
  ('skipped', 'Skipped'),
];
const int _pageSize = 50;

/// Results of one message, the same as the website: who got it, by list, and "Send again" for the failed ones
/// (all of them, or the ones ticked). Numbers not on WhatsApp are never sent again.
class ResultsPage extends ConsumerStatefulWidget {
  const ResultsPage({super.key, required this.id});

  final String id;

  @override
  ConsumerState<ResultsPage> createState() => _ResultsPageState();
}

class _ResultsPageState extends ConsumerState<ResultsPage> {
  String _list = 'all';
  RecipientsPage? _data;
  int _page = 1;
  String? _error;
  Set<String> _picked = <String>{};
  bool _retrying = false;

  @override
  void initState() {
    super.initState();
    _load(1);
  }

  Future<void> _load(int page, [String? list]) async {
    final String which = list ?? _list;
    try {
      final RecipientsPage data = await ref.read(sendingApiProvider).recipients(widget.id, which, page: page, pageSize: _pageSize);
      if (mounted) {
        setState(() {
          _list = which;
          _data = data;
          _page = page;
          _picked = <String>{};
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  int _count(BroadcastCounts c, String list) => switch (list) {
        'pending' => c.pending,
        'sent' => c.sent,
        'delivered' => c.delivered,
        'read' => c.read,
        'failed' => c.failed,
        'skipped' => c.skipped,
        _ => c.total,
      };

  Future<void> _retry({required bool onlyPicked}) async {
    final BroadcastCounts? c = _data?.summary.counts;
    final int n = onlyPicked ? _picked.length : (c?.failed ?? 0);
    if (n == 0) {
      return;
    }

    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.rotateCcw,
      title: 'Send again to $n ${n == 1 ? 'person' : 'people'}?',
      body: 'They go back in the queue and go out one by one. Numbers not on WhatsApp are left out.',
      confirm: 'Send again',
      cancel: 'Not now',
    );
    if (!yes || !mounted) {
      return;
    }

    setState(() => _retrying = true);
    try {
      await ref.read(sendingApiProvider).retry(widget.id, onlyPicked ? _picked.toList() : null);
      if (mounted) {
        showInfoToast(context, 'Sending again', description: 'They are back in the queue.');
      }

      await _load(1);
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not sent again', e);
      }
    } finally {
      if (mounted) {
        setState(() => _retrying = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final RecipientsPage? data = _data;
    final BroadcastCounts? c = data?.summary.counts;
    final int pages = data == null ? 1 : (data.total / _pageSize).ceil().clamp(1, 1 << 30);
    return PageScaffold(
      title: data?.summary.name ?? 'Results',
      subtitle: data == null ? null : '${dayMonthText(data.summary.createdAt)} · ${c!.total} people',
      child: data == null
          ? (_error == null
              ? const LoadingView()
              : EmptyView(icon: FLucideIcons.wifiOff, title: 'Could not load the results', text: _error!, action: () => _load(1), actionLabel: 'Try again'))
          : RefreshIndicator(
              onRefresh: () => _load(_page),
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.only(bottom: 24),
                children: <Widget>[
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                    child: ChoiceChips<String>(
                      options: <(String, String)>[
                        for (final (String id, String label) in _lists) (id, '$label ${_count(c!, id)}'),
                      ],
                      selected: _list,
                      onChange: (String l) => _load(1, l),
                    ),
                  ),
                  if (c!.failed > 0)
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        spacing: 8,
                        children: <Widget>[
                          FButton(
                            prefix: const Icon(FLucideIcons.rotateCcw),
                            onPress: _retrying ? null : () => _retry(onlyPicked: false),
                            child: Text('Send again to all ${c.failed} failed'),
                          ),
                          if (_picked.isNotEmpty)
                            FButton(
                              variant: FButtonVariant.outline,
                              onPress: _retrying ? null : () => _retry(onlyPicked: true),
                              child: Text('Send again to ${_picked.length} ticked'),
                            ),
                        ],
                      ),
                    ),
                  if (data.items.isEmpty)
                    const EmptyView(icon: FLucideIcons.circleMinus, text: 'Nobody in this list')
                  else
                    CardSection(
                      children: <Widget>[
                        for (final RecipientRow r in data.items) _row(theme, r),
                      ],
                    ),
                  if (pages > 1)
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Row(
                        children: <Widget>[
                          FButton(
                            variant: FButtonVariant.outline,
                            mainAxisSize: MainAxisSize.min,
                            onPress: _page > 1 ? () => _load(_page - 1) : null,
                            child: const Text('Previous'),
                          ),
                          Expanded(child: Text('Page $_page of $pages', textAlign: TextAlign.center, style: theme.typography.body.xs)),
                          FButton(
                            variant: FButtonVariant.outline,
                            mainAxisSize: MainAxisSize.min,
                            onPress: _page < pages ? () => _load(_page + 1) : null,
                            child: const Text('Next'),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
    );
  }

  Widget _row(FThemeData theme, RecipientRow r) {
    final (String text, Color color) = statusLook(r.status);
    final DateTime? at = r.readAt ?? r.deliveredAt ?? r.sentAt;
    final bool pickable = r.status == 'failed';
    return ListRow(
      leading: pickable
          ? FCheckbox(
              value: _picked.contains(r.id),
              onChange: (_) => setState(() {
                _picked = Set<String>.of(_picked);
                if (!_picked.remove(r.id)) {
                  _picked.add(r.id);
                }
              }),
            )
          : PersonAvatar(name: r.name, size: 38),
      title: r.name,
      subtitle: <String>[
        if (at != null) whenText(at),
        r.phoneNumber,
        if (r.reason != null) r.reason!,
        if (r.attempts > 1) 'tried ${r.attempts} times',
      ].join(' · '),
      badge: StatusDot(
        text,
        color: color,
        style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
      ),
      onTap: pickable
          ? () => setState(() {
                _picked = Set<String>.of(_picked);
                if (!_picked.remove(r.id)) {
                  _picked.add(r.id);
                }
              })
          : null,
    );
  }
}
