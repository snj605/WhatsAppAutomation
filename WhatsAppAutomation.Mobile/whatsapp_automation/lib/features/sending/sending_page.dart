import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../app/theme.dart';
import '../../core/format.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcasts_api.dart';
import 'results_page.dart';
import 'sending_api.dart';
import 'status_look.dart';

/// The Sending tab, the same as the website:
/// - Today's queue: what is waiting, in the order it goes out (occasions first), with when each should start and
///   finish; move up or down, pause, continue or stop; open the results.
/// - Settings: messages a day (300–500), how many in one go and the rest after, the pauses and the hours to send in.
class SendingTab extends ConsumerStatefulWidget {
  const SendingTab({super.key});

  @override
  ConsumerState<SendingTab> createState() => SendingTabState();
}

class SendingTabState extends ConsumerState<SendingTab> {
  String _tab = 'queue';
  SendQueue? _queue;
  String? _error;
  String? _busy;
  Timer? _timer;
  StreamSubscription<void>? _live;

  @override
  void initState() {
    super.initState();
    reload();
    _timer = Timer.periodic(const Duration(seconds: 20), (_) {
      if (_tab == 'queue') {
        reload();
      }
    });
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => reload());
  }

  @override
  void dispose() {
    _timer?.cancel();
    unawaited(_live?.cancel());
    super.dispose();
  }

  Future<void> reload() async {
    try {
      final SendQueue q = await ref.read(sendingApiProvider).queue();
      if (mounted) {
        setState(() {
          _queue = q;
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  Future<void> _run(QueueItem item, Future<void> Function() action, String failure) async {
    setState(() => _busy = item.id);
    try {
      await action();
      await reload();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, failure, e);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = null);
      }
    }
  }

  Future<void> _stop(QueueItem item) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Stop sending?',
      body: '"${item.name}": messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.',
      confirm: 'Stop sending',
      cancel: 'Keep sending',
      destructive: true,
    );
    if (yes) {
      await _run(item, () => ref.read(broadcastsApiProvider).change(item.id, 'cancel'), 'Not stopped');
    }
  }

  String _timing(QueueItem item) {
    if (item.status == 'paused') {
      return item.pauseReason ?? 'Paused';
    }

    final DateTime? start = item.expectedStart;
    if (start == null) {
      return 'Waiting';
    }

    final String first = start.isBefore(DateTime.now()) ? 'Sending now' : 'Starts ${whenText(start)}';
    return item.expectedFinish == null ? first : '$first · done about ${whenText(item.expectedFinish!)}';
  }

  Widget _kpi(FThemeData theme, String label, String value, {String? note, double? progress}) => Expanded(
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: theme.colors.card,
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: theme.colors.border),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(label, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
              const SizedBox(height: 4),
              Text(value, style: theme.typography.body.xl.copyWith(fontWeight: FontWeight.w700)),
              if (progress != null) ...<Widget>[
                const SizedBox(height: 6),
                FDeterminateProgress(value: progress.clamp(0, 1).toDouble()),
              ],
              if (note != null) ...<Widget>[
                const SizedBox(height: 4),
                Text(note, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
              ],
            ],
          ),
        ),
      );

  Widget _queueView(FThemeData theme) {
    final SendQueue? q = _queue;
    if (q == null) {
      return _error == null
          ? const LoadingView()
          : EmptyView(icon: FLucideIcons.wifiOff, title: 'Could not load the queue', text: _error!, action: reload, actionLabel: 'Try again');
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
          child: Column(
            children: <Widget>[
              Row(
                children: <Widget>[
                  _kpi(theme, 'Sent today', '${q.sentToday} / ${q.dailyLimit}',
                      progress: q.dailyLimit == 0 ? 0 : q.sentToday / q.dailyLimit),
                  const SizedBox(width: 8),
                  _kpi(theme, 'Left today', '${q.leftToday}', note: 'Then it continues tomorrow'),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: <Widget>[
                  _kpi(theme, 'Sending hours', q.sendingHours, note: q.inSendingHours ? 'Open now' : 'Closed now'),
                  const SizedBox(width: 8),
                  _kpi(
                    theme,
                    'WhatsApp',
                    q.connected ? 'Connected' : 'Not connected',
                    note: q.nextSendAt != null ? 'Next ${clockText(q.nextSendAt!)}' : (q.waiting ?? 'Nothing waiting'),
                  ),
                ],
              ),
            ],
          ),
        ),
        if (q.items.isEmpty)
          const EmptyView(
            icon: FLucideIcons.circleCheck,
            title: 'Nothing waiting',
            text: 'New messages and occasion wishes show here in the order they go out.',
          )
        else ...<Widget>[
          CardSection(
            title: 'In order',
            children: <Widget>[
              for (int i = 0; i < q.items.length; i++) _item(theme, q.items[i], i),
            ],
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 0),
            child: Text(
              'Occasion wishes always go first. Use the arrows to choose which message goes next. Times are estimates from '
              'your settings.',
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
        ],
      ],
    );
  }

  Widget _item(FThemeData theme, QueueItem item, int index) {
    final bool busy = _busy == item.id;
    final int willGet = item.counts.willGet;
    final double done = willGet == 0 ? 0 : (item.counts.sent + item.counts.failed) / willGet;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Container(
                width: 26,
                height: 26,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: theme.colors.muted, shape: BoxShape.circle),
                child: Text('${index + 1}', style: theme.typography.body.xs.copyWith(fontWeight: FontWeight.w700)),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600)),
              ),
              if (item.kind == 'occasion') const Pill('Occasion · first'),
            ],
          ),
          const SizedBox(height: 6),
          StatusDot(
            '${item.counts.sent} of $willGet sent${item.counts.failed > 0 ? ' · ${item.counts.failed} failed' : ''} · ${_timing(item)}',
            color: item.status == 'paused' ? AppTheme.warn : AppTheme.good,
            style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
          ),
          const SizedBox(height: 6),
          FDeterminateProgress(value: done.clamp(0, 1).toDouble()),
          const SizedBox(height: 8),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: <Widget>[
              if (item.kind == 'broadcast') ...<Widget>[
                FButton.icon(
                  variant: FButtonVariant.outline,
                  onPress: item.canMoveUp && !busy
                      ? () => _run(item, () => ref.read(sendingApiProvider).move(item.id, 'up'), 'Not moved')
                      : null,
                  child: const Icon(FLucideIcons.chevronUp),
                ),
                FButton.icon(
                  variant: FButtonVariant.outline,
                  onPress: item.canMoveDown && !busy
                      ? () => _run(item, () => ref.read(sendingApiProvider).move(item.id, 'down'), 'Not moved')
                      : null,
                  child: const Icon(FLucideIcons.chevronDown),
                ),
              ],
              FButton.icon(
                variant: FButtonVariant.outline,
                onPress: busy
                    ? null
                    : () => _run(
                          item,
                          () => ref.read(broadcastsApiProvider).change(item.id, item.status == 'sending' ? 'pause' : 'resume'),
                          'Not changed',
                        ),
                child: Icon(item.status == 'sending' ? FLucideIcons.pause : FLucideIcons.play),
              ),
              FButton.icon(variant: FButtonVariant.outline, onPress: busy ? null : () => _stop(item), child: const Icon(FLucideIcons.square)),
              FButton(
                variant: FButtonVariant.outline,
                mainAxisSize: MainAxisSize.min,
                onPress: () async {
                  await pushPage<void>(context, ResultsPage(id: item.id));
                  await reload();
                },
                child: const Text('Results'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return RefreshIndicator(
      onRefresh: reload,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
            child: ChoiceChips<String>(
              options: const <(String, String)>[('queue', "Today's queue"), ('settings', 'Settings')],
              selected: _tab,
              onChange: (String t) {
                setState(() => _tab = t);
                if (t == 'queue') {
                  reload();
                }
              },
            ),
          ),
          if (_tab == 'queue') _queueView(theme) else const SendingSettingsForm(),
        ],
      ),
    );
  }
}

/// Messages a day and sending hours: the only things the user chooses. The rhythm inside (pauses, how many in one
/// go, rests) is the system's own and changes all the time, so WhatsApp sees no pattern.
class SendingSettingsForm extends ConsumerStatefulWidget {
  const SendingSettingsForm({super.key});

  @override
  ConsumerState<SendingSettingsForm> createState() => _SendingSettingsFormState();
}

class _SendingSettingsFormState extends ConsumerState<SendingSettingsForm> {
  final TextEditingController _limit = TextEditingController();
  FTime _from = const FTime(9, 0);
  FTime _until = const FTime(21, 0);
  bool _anyTime = false;
  SendingSettings? _settings;
  bool _saving = false;
  String? _error;

  List<TextEditingController> get _all => <TextEditingController>[_limit];

  @override
  void initState() {
    super.initState();
    for (final TextEditingController c in _all) {
      c.addListener(() => setState(() {}));
    }

    _load();
  }

  @override
  void dispose() {
    for (final TextEditingController c in _all) {
      c.dispose();
    }

    super.dispose();
  }

  static FTime _parse(String hhmm) {
    final List<String> p = hhmm.split(':');
    return FTime(int.tryParse(p.first) ?? 0, p.length > 1 ? int.tryParse(p[1]) ?? 0 : 0);
  }

  static String _hhmm(FTime t) => '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  void _use(SendingSettings s) {
    _settings = s;
    _limit.text = '${s.dailyLimit}';
    _from = _parse(s.sendFrom);
    _until = _parse(s.sendUntil);
    _anyTime = s.anyTime;
  }

  Future<void> _load() async {
    try {
      final SendingSettings s = await ref.read(sendingApiProvider).settings();
      if (mounted) {
        setState(() => _use(s));
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  int _n(TextEditingController c) => int.tryParse(c.text.trim()) ?? -1;

  String? _problem() {
    final int limit = _n(_limit);
    if (limit < 300 || limit > 500) {
      return 'Messages a day: choose 300 to 500.';
    }

    if (!_anyTime && _hhmm(_from) == _hhmm(_until)) {
      return 'Choose different start and end times, or turn on "Any time".';
    }

    return null;
  }

  /// How many fit in a day with what is on the form: the same calculation as the server and the website.
  double get _hours => _anyTime ? 24 : windowHours(_hhmm(_from), _hhmm(_until));

  int _fits() => fitsPerDay(limit: _n(_limit), hours: _hours);

  /// "2 minutes", "45 seconds": the average time per message for what is on the form.
  String _every() {
    final int s = averageSeconds(limit: _n(_limit), hours: _hours).round();
    return s >= 90 ? '${(s / 60).round()} minutes' : '$s seconds';
  }

  Future<void> _save() async {
    final String? problem = _problem();
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final SendingSettings saved = await ref.read(sendingApiProvider).saveSettings(
            dailyLimit: _n(_limit),
            sendFrom: _anyTime ? '00:00' : _hhmm(_from),
            sendUntil: _anyTime ? '00:00' : _hhmm(_until),
          );
      if (mounted) {
        setState(() => _use(saved));
        showInfoToast(context, 'Saved', description: 'About ${saved.fitsPerDay} messages fit in a day.');
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

  Widget _number(TextEditingController c, String label, {String? description}) => FTextField(
        control: FTextFieldControl.managed(controller: c),
        label: Text(label),
        description: description == null ? null : Text(description),
        keyboardType: TextInputType.number,
        inputFormatters: <TextInputFormatter>[FilteringTextInputFormatter.digitsOnly],
        enabled: !_saving,
      );

  Widget _timePicker(String label, FTime value, ValueChanged<FTime> onChange) => CardSection(
        title: '$label · ${clockText(DateTime(2000, 1, 1, value.hour, value.minute))}',
        padding: const EdgeInsets.all(12),
        children: <Widget>[
          SizedBox(
            height: 160,
            child: FTimePicker(control: FTimePickerControl.managed(initial: value, onChange: onChange), minuteInterval: 5),
          ),
        ],
      );

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    if (_settings == null) {
      return _error == null
          ? const LoadingView()
          : EmptyView(icon: FLucideIcons.wifiOff, title: 'Could not load the settings', text: _error!, action: _load, actionLabel: 'Try again');
    }

    final int fits = _fits();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            spacing: 12,
            children: <Widget>[
              _number(_limit, 'Messages a day', description: '300 to 500, from the first day.'),
              Text(
                'The pauses, how many go before a rest and the rests change all the time, like a person sending, '
                'so WhatsApp sees no pattern. You only choose how many a day and the hours.',
                style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
              ),
            ],
          ),
        ),
        CardSection(
          children: <Widget>[
            ListRow(
              leading: const IconBadge(FLucideIcons.clock),
              title: 'Any time of day',
              subtitle: 'Off: only between the hours below',
              trailing: FSwitch(value: _anyTime, onChange: _saving ? null : (bool v) => setState(() => _anyTime = v)),
              onTap: _saving ? null : () => setState(() => _anyTime = !_anyTime),
            ),
          ],
        ),
        if (!_anyTime) ...<Widget>[
          _timePicker('From', _from, (FTime t) => setState(() => _from = t)),
          _timePicker('Until', _until, (FTime t) => setState(() => _until = t)),
        ],
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
          child: FAlert(
            icon: const Icon(FLucideIcons.info),
            title: Text('About $fits messages fit in a day'),
            subtitle: Text(
              fits < _n(_limit)
                  ? 'Fewer than you chose: the hours are too short for a safe pace. Choose longer hours to reach it.'
                  : 'About one message every ${_every()}. Times are in ${_settings!.timeZone}.',
            ),
          ),
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
            child: FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
          child: FButton(
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            onPress: _saving ? null : _save,
            child: const Text('Save'),
          ),
        ),
      ],
    );
  }
}
