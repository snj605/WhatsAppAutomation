import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/memory_cache.dart';
import '../../core/ui.dart';
import '../chats/birthdays_chat_page.dart';
import '../chats/new_message_page.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';
import '../templates/templates_api.dart';
import 'birthday_settings_page.dart';

/// The Birthdays tab: the automatic wishes, and everyone with a birthday from today to 7 days ahead, day by day.
/// Today's people show whether their wish went out and when it reached their phone. Tick people (or a whole day)
/// to send a wish by hand.
class BirthdaysTab extends ConsumerStatefulWidget {
  const BirthdaysTab({super.key});

  @override
  ConsumerState<BirthdaysTab> createState() => BirthdaysTabState();
}

class BirthdaysTabState extends ConsumerState<BirthdaysTab> {
  List<UpcomingBirthday>? _list = MemoryCache.get<List<UpcomingBirthday>>('birthdays');
  BirthdaySettings? _settings = MemoryCache.get<BirthdaySettings>('birthdays:settings');
  String? _error;
  Set<String> _selected = <String>{};

  @override
  void initState() {
    super.initState();
    reload();
  }

  Future<void> reload() async {
    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      final (List<UpcomingBirthday> list, BirthdaySettings settings) = await (api.birthdays(days: 7), api.birthdaySettings()).wait;
      MemoryCache.set('birthdays', list);
      MemoryCache.set('birthdays:settings', settings);
      if (mounted) {
        setState(() {
          _list = list;
          _settings = settings;
          _error = null;
          _selected = _selected.where((String id) => list.any((UpcomingBirthday b) => b.contactId == id)).toSet();
        });
      }
    } on ParallelWaitError<(List<UpcomingBirthday>?, BirthdaySettings?), (AsyncError?, AsyncError?)> catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e.errors.$1?.error ?? e.errors.$2?.error ?? e));
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  Future<void> openSettings() async {
    final bool? changed = await pushPage<bool>(context, BirthdaySettingsPage(settings: _settings));
    if (changed ?? false) {
      await reload();
    }
  }

  bool _canPick(UpcomingBirthday b) => !b.optedOut;

  void _toggle(UpcomingBirthday b) {
    if (!_canPick(b)) {
      return;
    }

    setState(() {
      _selected = Set<String>.of(_selected);
      if (!_selected.remove(b.contactId)) {
        _selected.add(b.contactId);
      }
    });
  }

  void _toggleDay(List<UpcomingBirthday> day) {
    final List<String> ids = day.where(_canPick).map((UpcomingBirthday b) => b.contactId).toList();
    final bool all = ids.isNotEmpty && ids.every(_selected.contains);
    setState(() {
      _selected = Set<String>.of(_selected);
      if (all) {
        _selected.removeAll(ids);
      } else {
        _selected.addAll(ids);
      }
    });
  }

  Future<void> _wish() async {
    final List<UpcomingBirthday> people =
        (_list ?? const <UpcomingBirthday>[]).where((UpcomingBirthday b) => _selected.contains(b.contactId)).toList();
    if (people.isEmpty) {
      return;
    }

    await pushPage<void>(
      context,
      NewMessagePage(
        preset: ComposerPreset(
          name: 'Birthday wishes ${dayMonthText(DateTime.now())}',
          people: <({String id, String label})>[for (final UpcomingBirthday p in people) (id: p.contactId, label: p.name)],
          text: birthdayText,
          forBirthday: people.every((UpcomingBirthday p) => p.daysUntil == 0),
        ),
      ),
    );
    setState(() => _selected = <String>{});
    await reload();
  }

  String _dayTitle(int daysUntil, DateTime date) => switch (daysUntil) {
        0 => 'Today · ${longDayText(date)}',
        1 => 'Tomorrow · ${longDayText(date)}',
        _ => longDayText(date),
      };

  /// What happens with this person's wish: today the real state, on coming days when it is planned.
  (String, Color)? _state(UpcomingBirthday b, FThemeData theme) {
    if (b.optedOut) {
      return ('No broadcasts', theme.colors.mutedForeground);
    }

    if (b.daysUntil > 0) {
      final String? at = _settings?.time;
      return _settings?.ready ?? false ? ('Goes out at ${_timeOfDay(at)}', const Color(0xFF0EA5E9)) : null;
    }

    return switch (b.shownStatus) {
      'delivered' => (
          b.deliveredAt != null ? 'Delivered ${clockText(b.deliveredAt!)}' : 'Delivered',
          const Color(0xFF16A34A),
        ),
      'sent' => (b.sentAt != null ? 'Sent ${clockText(b.sentAt!)}' : 'Sent', theme.colors.primary),
      'pending' => ('Waiting to send', const Color(0xFFD97706)),
      'failed' => ('Failed', theme.colors.destructive),
      'skipped' => ('Skipped', theme.colors.mutedForeground),
      _ => _settings?.ready ?? false
          ? ('Goes out ${clockText(_settings!.nextRunAt!)}', const Color(0xFF0EA5E9))
          : ('Not sent', const Color(0xFFD97706)),
    };
  }

  /// "07:15" → "07:15:00 AM".
  String _timeOfDay(String? hhmm) {
    final List<String> parts = (hhmm ?? '00:00').split(':');
    final DateTime t = DateTime(2000, 1, 1, int.tryParse(parts.first) ?? 0, int.tryParse(parts.last) ?? 0);
    return clockText(t);
  }

  Widget _row(UpcomingBirthday b, FThemeData theme) {
    final (String, Color)? state = _state(b, theme);
    return ListRow(
      leading: FCheckbox(value: _selected.contains(b.contactId), enabled: _canPick(b), onChange: (_) => _toggle(b)),
      title: b.name,
      subtitle: '${b.turnsAge != null ? 'Turns ${b.turnsAge} · ' : ''}${b.phoneNumber}',
      badge: state == null ? null : Pill(state.$1, color: state.$2),
      trailing: PersonAvatar(name: b.name, size: 38),
      dimmed: b.optedOut,
      onTap: () => _toggle(b),
    );
  }

  Widget _settingsCard(FThemeData theme) {
    final BirthdaySettings? s = _settings;
    final bool on = s?.ready ?? false;
    return CardSection(
      title: 'Automatic wishes',
      children: <Widget>[
        ListRow(
          leading: IconBadge(on ? FLucideIcons.alarmClockCheck : FLucideIcons.alarmClockOff,
              color: on ? const Color(0xFF16A34A) : theme.colors.mutedForeground),
          title: on ? 'On · every day at ${_timeOfDay(s!.time)}' : 'Off',
          subtitle: on
              ? 'Template: ${s!.templateName} · Next: ${whenText(s.nextRunAt!)}'
              : 'Turn on to send birthday wishes by themselves at a time you choose',
          trailing: Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
          onTap: openSettings,
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final List<UpcomingBirthday>? list = _list;
    final DateTime now = DateTime.now();
    final List<(int, DateTime, List<UpcomingBirthday>)> days = <(int, DateTime, List<UpcomingBirthday>)>[
      for (int d = 0; d <= 7; d++)
        (
          d,
          DateTime(now.year, now.month, now.day + d),
          list?.where((UpcomingBirthday b) => b.daysUntil == d).toList() ?? <UpcomingBirthday>[],
        ),
    ];
    final int coming = days.skip(1).fold<int>(0, (int n, (int, DateTime, List<UpcomingBirthday>) d) => n + d.$3.length);
    return Column(
      children: <Widget>[
        Expanded(
          child: RefreshIndicator(
            onRefresh: reload,
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.only(bottom: 24),
              children: <Widget>[
                if (list == null && _error == null) const LoadingView(),
                if (_error != null && list == null)
                  EmptyView(icon: FLucideIcons.wifiOff, title: 'Could not load birthdays', text: _error!, action: reload, actionLabel: 'Try again'),
                if (list != null) ...<Widget>[
                  _settingsCard(theme),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
                    child: FButton(
                      variant: FButtonVariant.outline,
                      prefix: const Icon(FLucideIcons.messageCircle),
                      onPress: () async {
                        await pushPage<void>(context, const BirthdaysChatPage());
                        await reload();
                      },
                      child: const Text("Today's wishes chat"),
                    ),
                  ),
                  for (final (int d, DateTime date, List<UpcomingBirthday> people) in days)
                    if (d == 0 || people.isNotEmpty)
                      CardSection(
                        title: '${_dayTitle(d, date)}${people.isEmpty ? '' : ' · ${people.length}'}',
                        trailing: people.any(_canPick)
                            ? GestureDetector(
                                onTap: () => _toggleDay(people),
                                child: Text(
                                  people.where(_canPick).every((UpcomingBirthday b) => _selected.contains(b.contactId))
                                      ? 'Clear'
                                      : 'Select all',
                                  style: theme.typography.body.sm.copyWith(color: theme.colors.primary, fontWeight: FontWeight.w700),
                                ),
                              )
                            : null,
                        children: <Widget>[
                          if (people.isEmpty)
                            const ListRow(leading: IconBadge(FLucideIcons.cake, color: Color(0xFFEC4899)), title: 'No birthdays today'),
                          for (final UpcomingBirthday b in people) _row(b, theme),
                        ],
                      ),
                  if (coming == 0)
                    const CardSection(
                      children: <Widget>[
                        ListRow(leading: IconBadge(FLucideIcons.calendar), title: 'No birthdays in the next 7 days'),
                      ],
                    ),
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.all(16),
                      child: Text('Could not refresh: $_error', style: theme.typography.body.xs.copyWith(color: theme.colors.destructive)),
                    ),
                ],
              ],
            ),
          ),
        ),
        if (_selected.isNotEmpty)
          Container(
            padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
            decoration: BoxDecoration(
              color: theme.colors.card,
              border: Border(top: BorderSide(color: theme.colors.border, width: 0.6)),
            ),
            child: SafeArea(
              top: false,
              child: Row(
                children: <Widget>[
                  FButton(
                    variant: FButtonVariant.ghost,
                    mainAxisSize: MainAxisSize.min,
                    onPress: () => setState(() => _selected = <String>{}),
                    child: const Text('Clear'),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: FButton(
                      prefix: const Icon(FLucideIcons.send),
                      onPress: _wish,
                      child: Text('Send now to ${_selected.length} ${_selected.length == 1 ? 'person' : 'people'}'),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}
