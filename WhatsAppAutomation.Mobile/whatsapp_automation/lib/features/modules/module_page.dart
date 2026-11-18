import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../../core/ui.dart';
import '../chats/new_message_page.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';
import '../preview/wa_bubble.dart';
import '../preview/wa_person_preview.dart';
import '../sending/status_look.dart';
import '../templates/templates_api.dart';
import '../templates/templates_page.dart';
import 'modules_api.dart';

/// One occasion (Birthday, Anniversary, Asthi, ...), the same as on the website:
/// - Next 7 days: everyone with the date, day by day, with what happened to today's wishes. Tick people (or a whole
///   day) and send by hand.
/// - Dates (Yearly and Once): who has which date.
/// - Settings: send by itself at a time with a template, rename, delete.
class ModulePage extends ConsumerStatefulWidget {
  const ModulePage({super.key, required this.moduleId});

  final String moduleId;

  @override
  ConsumerState<ModulePage> createState() => _ModulePageState();
}

class _ModulePageState extends ConsumerState<ModulePage> {
  String _tab = 'days';
  OccasionModule? _module;
  List<ModuleDayPerson>? _people;
  List<MessageTemplate> _templates = const <MessageTemplate>[];
  String? _error;
  Set<String> _selected = <String>{};

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final ModulesApi api = ref.read(modulesApiProvider);
      final List<OccasionModule> all = await api.list();
      final OccasionModule? m = all.where((OccasionModule x) => x.id == widget.moduleId).firstOrNull;
      if (m == null) {
        if (mounted) {
          setState(() => _error = 'This occasion was not found. It may have been deleted.');
        }

        return;
      }

      final List<ModuleDayPerson> people = await api.days(m.id);
      List<MessageTemplate> templates = const <MessageTemplate>[];
      try {
        templates = await ref.read(templatesApiProvider).list();
      } on Object {
        // The list still works without templates.
      }

      if (mounted) {
        setState(() {
          _module = m;
          _people = people;
          _templates = templates;
          _error = null;
          _selected = _selected.where((String id) => people.any((ModuleDayPerson p) => p.contactId == id)).toSet();
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  bool _canPick(ModuleDayPerson p) => !p.optedOut;

  void _toggle(ModuleDayPerson p) {
    if (!_canPick(p)) {
      return;
    }

    setState(() {
      _selected = Set<String>.of(_selected);
      if (!_selected.remove(p.contactId)) {
        _selected.add(p.contactId);
      }
    });
  }

  void _toggleDay(List<ModuleDayPerson> day) {
    final List<String> ids = day.where(_canPick).map((ModuleDayPerson p) => p.contactId).toList();
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

  Future<void> _send() async {
    final OccasionModule? m = _module;
    final List<ModuleDayPerson> people =
        (_people ?? const <ModuleDayPerson>[]).where((ModuleDayPerson p) => _selected.contains(p.contactId)).toList();
    if (m == null || people.isEmpty) {
      return;
    }

    final MessageTemplate? template = _templates.where((MessageTemplate t) => t.id == m.templateId).firstOrNull;
    await pushPage<void>(
      context,
      NewMessagePage(
        preset: ComposerPreset(
          name: '${m.name} ${dayMonthText(DateTime.now())}',
          people: <({String id, String label})>[for (final ModuleDayPerson p in people) (id: p.contactId, label: p.name)],
          text: template?.text ?? defaultWish(m),
          moduleId: m.id,
          attachment: template?.attachment,
        ),
      ),
    );
    setState(() => _selected = <String>{});
    await _load();
  }

  String _dayTitle(int daysUntil, DateTime date) => switch (daysUntil) {
        0 => 'Today · ${longDayText(date)}',
        1 => 'Tomorrow · ${longDayText(date)}',
        _ => longDayText(date),
      };

  String _detail(ModuleDayPerson p) {
    final OccasionModule? m = _module;
    return <String>[
      if (p.years != null) m?.kind == 'birthday' ? 'Turns ${p.years}' : '${p.years} years',
      if (p.forName != null && m?.forNameLabel != null) '${m!.forNameLabel}: ${p.forName}',
      p.phoneNumber,
    ].join(' · ');
  }

  /// Today: what happened to the wish. Coming days: when it goes out by itself.
  (String, Color)? _state(ModuleDayPerson p) {
    final OccasionModule m = _module!;
    if (p.optedOut) {
      return ('No broadcasts', statusLook('skipped').$2);
    }

    if (p.daysUntil > 0) {
      return m.ready ? ('Goes out ${hhmmText(m.time)}', statusLook('sent').$2) : null;
    }

    if (p.status == null || p.status == 'not_sent') {
      return m.ready ? ('Goes out ${whenText(m.nextRunAt!)}', statusLook('sent').$2) : ('Not sent', statusLook('pending').$2);
    }

    final (String text, Color color) = statusLook(p.status);
    final DateTime? at = p.status == 'delivered' || p.status == 'read' ? p.deliveredAt : (p.status == 'sent' ? p.sentAt : null);
    return (at == null ? text : '$text ${clockText(at)}', color);
  }

  Widget _days(FThemeData theme) {
    final OccasionModule m = _module!;
    final List<ModuleDayPerson> list = _people ?? const <ModuleDayPerson>[];
    final DateTime now = DateTime.now();
    final List<(int, DateTime, List<ModuleDayPerson>)> days = <(int, DateTime, List<ModuleDayPerson>)>[
      for (int d = 0; d <= 7; d++)
        (d, DateTime(now.year, now.month, now.day + d), list.where((ModuleDayPerson p) => p.daysUntil == d).toList()),
    ];
    final int coming = list.where((ModuleDayPerson p) => p.daysUntil > 0).length;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        CardSection(
          children: <Widget>[
            ListRow(
              leading: IconBadge(m.ready ? FLucideIcons.alarmClockCheck : FLucideIcons.alarmClockOff),
              title: m.ready ? 'Sends by itself every day at ${hhmmText(m.time)}' : 'Automatic sending is off',
              subtitle: m.ready
                  ? 'Template: ${m.templateName} · Next: ${whenText(m.nextRunAt!)}'
                  : 'Turn it on to send at a time you choose, with a template',
              trailing: Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
              onTap: () => setState(() => _tab = 'settings'),
            ),
          ],
        ),
        for (final (int d, DateTime date, List<ModuleDayPerson> people) in days)
          if (d == 0 || people.isNotEmpty)
            CardSection(
              title: '${_dayTitle(d, date)}${people.isEmpty ? '' : ' · ${people.length}'}',
              trailing: people.any(_canPick)
                  ? GestureDetector(
                      onTap: () => _toggleDay(people),
                      child: Text(
                        people.where(_canPick).every((ModuleDayPerson p) => _selected.contains(p.contactId)) ? 'Clear' : 'Select all',
                        style: theme.typography.body.sm.copyWith(color: theme.colors.primary, fontWeight: FontWeight.w700),
                      ),
                    )
                  : null,
              children: <Widget>[
                if (people.isEmpty) ListRow(leading: IconBadge(moduleIcon(m)), title: 'Nobody today'),
                for (final ModuleDayPerson p in people) _row(p, theme),
              ],
            ),
        if (coming == 0)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
            child: Text(
              m.kind == 'birthday'
                  ? 'Nobody else in the next 7 days.'
                  : 'Nobody else in the next 7 days. Add dates in Dates, or with a "${m.name}" column in the Excel import.',
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
      ],
    );
  }

  Widget _row(ModuleDayPerson p, FThemeData theme) {
    final (String, Color)? state = _state(p);
    return ListRow(
      leading: FCheckbox(value: _selected.contains(p.contactId), enabled: _canPick(p), onChange: (_) => _toggle(p)),
      title: p.name,
      subtitle: state == null ? _detail(p) : '${state.$1} · ${_detail(p)}',
      badge: state == null ? null : StatusDot('', color: state.$2),
      trailing: PersonAvatar(name: p.name, size: 38),
      dimmed: p.optedOut,
      onTap: () => _toggle(p),
    );
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final OccasionModule? m = _module;
    return PageScaffold(
      title: m?.name ?? 'Occasion',
      subtitle: m?.kindText,
      footer: _tab == 'days' && _selected.isNotEmpty
          ? SafeArea(
              top: false,
              child: Container(
                padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
                decoration: BoxDecoration(
                  color: theme.colors.card,
                  border: Border(top: BorderSide(color: theme.colors.border)),
                ),
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
                        onPress: _send,
                        child: Text('Send to ${_selected.length} ${_selected.length == 1 ? 'person' : 'people'}'),
                      ),
                    ),
                  ],
                ),
              ),
            )
          : null,
      child: m == null
          ? (_error == null
              ? const LoadingView()
              : EmptyView(icon: FLucideIcons.circleAlert, title: 'Could not open this occasion', text: _error!, action: _load, actionLabel: 'Try again'))
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.only(bottom: 24),
                children: <Widget>[
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                    child: ChoiceChips<String>(
                      options: <(String, String)>[
                        ('days', 'Next 7 days'),
                        if (m.kind != 'birthday') ('dates', 'Dates'),
                        ('settings', 'Settings'),
                      ],
                      selected: _tab,
                      onChange: (String t) => setState(() => _tab = t),
                    ),
                  ),
                  switch (_tab) {
                    'dates' => _ModuleDates(module: m, onChanged: _load),
                    'settings' => _ModuleSettings(
                        module: m,
                        templates: _templates,
                        onSaved: (OccasionModule saved) {
                          setState(() => _module = saved);
                          unawaited(ref.read(modulesProvider.notifier).reload());
                        },
                        onTemplatesChanged: _load,
                      ),
                    _ => _days(theme),
                  },
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.all(16),
                      child: Text('Could not refresh: $_error', style: theme.typography.body.xs.copyWith(color: theme.colors.destructive)),
                    ),
                ],
              ),
            ),
    );
  }
}

/// Settings of one occasion: automatic sending (time and template), its name and second name, delete.
class _ModuleSettings extends ConsumerStatefulWidget {
  const _ModuleSettings({required this.module, required this.templates, required this.onSaved, required this.onTemplatesChanged});

  final OccasionModule module;
  final List<MessageTemplate> templates;
  final ValueChanged<OccasionModule> onSaved;
  final Future<void> Function() onTemplatesChanged;

  @override
  ConsumerState<_ModuleSettings> createState() => _ModuleSettingsState();
}

class _ModuleSettingsState extends ConsumerState<_ModuleSettings> {
  late bool _auto = widget.module.autoSend;
  late FTime _time = _parse(widget.module.time) ?? const FTime(7, 15);
  late String? _templateId = widget.module.templateId;
  late final TextEditingController _name = TextEditingController(text: widget.module.name);
  late final TextEditingController _label = TextEditingController(text: widget.module.forNameLabel ?? '');
  bool _saving = false;
  String? _error;

  static FTime? _parse(String? hhmm) {
    final List<String>? parts = hhmm?.split(':');
    if (parts == null || parts.length != 2) {
      return null;
    }

    final int? h = int.tryParse(parts[0]);
    final int? m = int.tryParse(parts[1]);
    return h == null || m == null || h > 23 || m > 59 ? null : FTime(h, m);
  }

  String get _hhmm => '${_time.hour.toString().padLeft(2, '0')}:${_time.minute.toString().padLeft(2, '0')}';

  @override
  void dispose() {
    _name.dispose();
    _label.dispose();
    super.dispose();
  }

  Future<void> _saveSending() async {
    if (_auto && _templateId == null) {
      setState(() => _error = 'Choose the template to send.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final OccasionModule saved =
          await ref.read(modulesApiProvider).setSending(widget.module.id, enabled: _auto, time: _hhmm, templateId: _templateId);
      widget.onSaved(saved);
      if (mounted) {
        showInfoToast(
          context,
          saved.ready ? 'Automatic sending is on' : 'Automatic sending is off',
          description: saved.ready ? 'Next: ${whenText(saved.nextRunAt!)}' : null,
        );
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

  Future<void> _saveDetails() async {
    final String name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _error = 'Give the occasion a name.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final OccasionModule saved = await ref.read(modulesApiProvider).update(
            widget.module.id,
            name,
            widget.module.kind == 'birthday' || _label.text.trim().isEmpty ? null : _label.text.trim(),
          );
      widget.onSaved(saved);
      if (mounted) {
        showInfoToast(context, 'Saved');
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

  Future<void> _delete() async {
    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.trash2,
      title: 'Delete ${widget.module.name}?',
      body: 'Its dates are deleted and wishes still waiting are stopped. Contacts and sent messages stay.',
      confirm: 'Delete',
      cancel: 'Keep',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      await ref.read(modulesApiProvider).remove(widget.module.id);
      unawaited(ref.read(modulesProvider.notifier).reload());
      if (mounted) {
        showInfoToast(context, '${widget.module.name} deleted');
        Navigator.of(context).pop();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final MessageTemplate? chosen = widget.templates.where((MessageTemplate t) => t.id == _templateId).firstOrNull;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        CardSection(
          title: 'Automatic sending',
          children: <Widget>[
            ListRow(
              leading: IconBadge(moduleIcon(widget.module)),
              title: 'Send by itself',
              subtitle: 'Every day at the time below, to everyone with the date that day',
              trailing: FSwitch(value: _auto, onChange: _saving ? null : (bool v) => setState(() => _auto = v)),
              onTap: _saving ? null : () => setState(() => _auto = !_auto),
            ),
          ],
        ),
        CardSection(
          title: 'Time · starts at ${clockText(DateTime(2000, 1, 1, _time.hour, _time.minute))}',
          padding: const EdgeInsets.all(12),
          children: <Widget>[
            SizedBox(
              height: 180,
              child: FTimePicker(
                control: FTimePickerControl.managed(initial: _time, onChange: (FTime t) => setState(() => _time = t)),
                minuteInterval: 5,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Wishes go out one by one, a little apart, and always before other messages. If you turn this on after '
              'today\'s time, it starts tomorrow; you can still send today\'s by hand.',
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ],
        ),
        CardSection(
          title: 'Template',
          children: <Widget>[
            if (widget.templates.isEmpty)
              ListRow(
                leading: const IconBadge(FLucideIcons.filePlus),
                title: 'No templates yet',
                subtitle: 'Make one with {{name}} in it, then choose it here',
                trailing: const Icon(FLucideIcons.chevronRight, size: 18),
                onTap: () async {
                  await pushPage<void>(context, const TemplatesPage());
                  await widget.onTemplatesChanged();
                },
              ),
            for (final MessageTemplate t in widget.templates)
              ListRow(
                leading: FCheckbox(value: t.id == _templateId, onChange: (_) => setState(() => _templateId = t.id)),
                title: t.shared ? '${t.name} · Shared' : t.name,
                subtitle: sampleText(t.text),
                onTap: () => setState(() => _templateId = t.id),
              ),
          ],
        ),
        if (chosen != null)
          CardSection(
            title: 'How it looks on WhatsApp',
            padding: const EdgeInsets.all(12),
            children: <Widget>[WaPersonPreview(text: chosen.text, file: WaFile.upload(chosen.attachment), moduleId: widget.module.id)],
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
          child: FButton(onPress: _saving ? null : _saveSending, child: const Text('Save sending')),
        ),
        CardSection(
          title: 'Details',
          padding: const EdgeInsets.all(12),
          children: <Widget>[
            FTextField(control: FTextFieldControl.managed(controller: _name), label: const Text('Name'), enabled: !_saving),
            if (widget.module.kind != 'birthday') ...<Widget>[
              const SizedBox(height: 12),
              FTextField(
                control: FTextFieldControl.managed(controller: _label),
                label: const Text('Second name (optional)'),
                hint: 'In memory of',
                description: const Text('When set, each date can have a second name, used as {{for_name}}.'),
                enabled: !_saving,
              ),
            ],
            const SizedBox(height: 12),
            FButton(variant: FButtonVariant.outline, onPress: _saving ? null : _saveDetails, child: const Text('Save details')),
          ],
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
            child: FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: FButton(
            variant: FButtonVariant.destructive,
            prefix: const Icon(FLucideIcons.trash2),
            onPress: _saving ? null : _delete,
            child: Text('Delete ${widget.module.name}'),
          ),
        ),
      ],
    );
  }
}

/// Who has which date in a Yearly or Once occasion: search, add, change and remove.
class _ModuleDates extends ConsumerStatefulWidget {
  const _ModuleDates({required this.module, required this.onChanged});

  final OccasionModule module;
  final Future<void> Function() onChanged;

  @override
  ConsumerState<_ModuleDates> createState() => _ModuleDatesState();
}

class _ModuleDatesState extends ConsumerState<_ModuleDates> {
  final TextEditingController _search = TextEditingController();
  List<ModuleEntry>? _items;
  int _total = 0;
  int _page = 1;
  String? _error;
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _load(1);
    _search.addListener(() {
      _debounce?.cancel();
      _debounce = Timer(const Duration(milliseconds: 350), () => _load(1));
    });
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load(int page) async {
    try {
      final ({List<ModuleEntry> items, int total}) result =
          await ref.read(modulesApiProvider).entries(widget.module.id, search: _search.text.trim(), page: page);
      if (mounted) {
        setState(() {
          _items = result.items;
          _total = result.total;
          _page = page;
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  Future<void> _edit(ModuleEntry? e) async {
    final bool? saved = await pushPage<bool>(context, _EntryEditPage(module: widget.module, entry: e));
    if (saved ?? false) {
      await _load(_page);
      await widget.onChanged();
    }
  }

  Future<void> _remove(ModuleEntry e) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Remove ${e.name}?',
      body: 'Their date is taken out of ${widget.module.name}. The contact stays.',
      confirm: 'Remove',
      cancel: 'Keep',
      destructive: true,
    );
    if (!yes) {
      return;
    }

    try {
      await ref.read(modulesApiProvider).removeEntry(widget.module.id, e.contactId);
      await _load(_page);
      await widget.onChanged();
    } on Object catch (err) {
      if (mounted) {
        showErrorToast(context, 'Not removed', err);
      }
    }
  }

  String _date(ModuleEntry e) => '${e.day} ${monthName(e.month)}${e.year != null ? ' ${e.year}' : ''}';

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final List<ModuleEntry>? items = _items;
    final int pages = (_total / 50).ceil().clamp(1, 1 << 30);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
          child: Row(
            children: <Widget>[
              Expanded(
                child: FTextField(control: FTextFieldControl.managed(controller: _search), hint: 'Search name or number'),
              ),
              const SizedBox(width: 8),
              FButton(mainAxisSize: MainAxisSize.min, prefix: const Icon(FLucideIcons.plus), onPress: () => _edit(null), child: const Text('Add')),
            ],
          ),
        ),
        if (items == null && _error == null) const LoadingView(),
        if (_error != null && items == null) EmptyView(icon: FLucideIcons.wifiOff, text: _error!, action: () => _load(1), actionLabel: 'Try again'),
        if (items != null && items.isEmpty)
          EmptyView(
            icon: FLucideIcons.calendar,
            title: 'No dates yet',
            text: 'Add them here, or put a "${widget.module.name}" column (day/month/year) in the Excel import.',
            action: () => _edit(null),
            actionLabel: 'Add date',
          ),
        if (items != null && items.isNotEmpty)
          CardSection(
            children: <Widget>[
              for (final ModuleEntry e in items)
                ListRow(
                  leading: PersonAvatar(name: e.name, size: 38),
                  title: e.name,
                  subtitle: <String>[
                    _date(e),
                    if (e.forName != null && widget.module.forNameLabel != null) '${widget.module.forNameLabel}: ${e.forName}',
                    e.phoneNumber,
                  ].join(' · '),
                  trailing: FButton.icon(variant: FButtonVariant.ghost, onPress: () => _remove(e), child: const Icon(FLucideIcons.trash2)),
                  onTap: () => _edit(e),
                ),
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
                Expanded(
                  child: Text('Page $_page of $pages · $_total', textAlign: TextAlign.center, style: theme.typography.body.xs),
                ),
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
    );
  }
}

/// Adds or changes one person's date in an occasion.
class _EntryEditPage extends ConsumerStatefulWidget {
  const _EntryEditPage({required this.module, this.entry});

  final OccasionModule module;
  final ModuleEntry? entry;

  @override
  ConsumerState<_EntryEditPage> createState() => _EntryEditPageState();
}

class _EntryEditPageState extends ConsumerState<_EntryEditPage> {
  final TextEditingController _query = TextEditingController();
  late final TextEditingController _day = TextEditingController(text: '${widget.entry?.day ?? DateTime.now().day}');
  late final TextEditingController _month = TextEditingController(text: '${widget.entry?.month ?? DateTime.now().month}');
  late final TextEditingController _year = TextEditingController(
    text: widget.entry?.year?.toString() ?? (widget.module.kind == 'once' ? '${DateTime.now().year}' : ''),
  );
  late final TextEditingController _forName = TextEditingController(text: widget.entry?.forName ?? '');
  late String? _contactId = widget.entry?.contactId;
  late String _contactName = widget.entry?.name ?? '';
  List<Contact> _hits = const <Contact>[];
  Timer? _debounce;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _query.addListener(() {
      _debounce?.cancel();
      final String q = _query.text.trim();
      if (q.length < 2) {
        setState(() => _hits = const <Contact>[]);
        return;
      }

      _debounce = Timer(const Duration(milliseconds: 300), () async {
        try {
          final ContactPage page = await ref.read(contactsApiProvider).contacts(search: q, pageSize: 8);
          if (mounted) {
            setState(() => _hits = page.items);
          }
        } on Object {
          if (mounted) {
            setState(() => _hits = const <Contact>[]);
          }
        }
      });
    });
  }

  @override
  void dispose() {
    _debounce?.cancel();
    for (final TextEditingController c in <TextEditingController>[_query, _day, _month, _year, _forName]) {
      c.dispose();
    }

    super.dispose();
  }

  Future<void> _save() async {
    final int? day = int.tryParse(_day.text.trim());
    final int? month = int.tryParse(_month.text.trim());
    final int? year = _year.text.trim().isEmpty ? null : int.tryParse(_year.text.trim());
    if (_contactId == null) {
      setState(() => _error = 'Choose the person.');
      return;
    }

    if (day == null || month == null || day < 1 || day > 31 || month < 1 || month > 12) {
      setState(() => _error = 'Enter the day (1–31) and month (1–12).');
      return;
    }

    if (widget.module.kind == 'once' && year == null) {
      setState(() => _error = 'A one-time date needs the year.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(modulesApiProvider).setEntry(
            widget.module.id,
            _contactId!,
            day: day,
            month: month,
            year: year,
            forName: _forName.text.trim().isEmpty ? null : _forName.text.trim(),
          );
      if (mounted) {
        showInfoToast(context, 'Saved for $_contactName');
        Navigator.of(context).pop(true);
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

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: widget.entry == null ? 'Add date' : 'Change date',
        subtitle: widget.module.name,
        footer: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: FButton(onPress: _saving ? null : _save, child: const Text('Save')),
          ),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            const SectionLabel('Person'),
            if (_contactId != null)
              CardSection(
                margin: EdgeInsets.zero,
                children: <Widget>[
                  ListRow(
                    leading: PersonAvatar(name: _contactName, size: 38),
                    title: _contactName,
                    trailing: widget.entry == null
                        ? FButton.icon(
                            variant: FButtonVariant.ghost,
                            onPress: () => setState(() => _contactId = null),
                            child: const Icon(FLucideIcons.x),
                          )
                        : null,
                  ),
                ],
              )
            else ...<Widget>[
              FTextField(control: FTextFieldControl.managed(controller: _query), hint: 'Type a name or number', autofocus: true),
              if (_hits.isNotEmpty)
                CardSection(
                  margin: const EdgeInsets.only(top: 8),
                  children: <Widget>[
                    for (final Contact c in _hits)
                      ListRow(
                        leading: PersonAvatar(name: c.displayName, size: 34),
                        title: c.displayName,
                        subtitle: c.phoneNumber,
                        onTap: () => setState(() {
                          _contactId = c.id;
                          _contactName = c.displayName;
                          _hits = const <Contact>[];
                          _query.clear();
                        }),
                      ),
                  ],
                ),
            ],
            const SizedBox(height: 16),
            Row(
              children: <Widget>[
                Expanded(
                  child: FTextField(
                    control: FTextFieldControl.managed(controller: _day),
                    label: const Text('Day'),
                    keyboardType: TextInputType.number,
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: FTextField(
                    control: FTextFieldControl.managed(controller: _month),
                    label: const Text('Month'),
                    keyboardType: TextInputType.number,
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: FTextField(
                    control: FTextFieldControl.managed(controller: _year),
                    label: Text(widget.module.kind == 'once' ? 'Year' : 'Year (optional)'),
                    keyboardType: TextInputType.number,
                  ),
                ),
              ],
            ),
            if (widget.module.forNameLabel != null) ...<Widget>[
              const SizedBox(height: 16),
              FTextField(
                control: FTextFieldControl.managed(controller: _forName),
                label: Text(widget.module.forNameLabel!),
                description: const Text('Used in the message as {{for_name}}.'),
              ),
            ],
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}
