import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import 'module_page.dart';
import 'modules_api.dart';

const int maxModules = 20;

/// The Occasions tab: every occasion of the user (Birthday, Anniversary, Asthi, ...) and a button to make a new one.
/// Members make their own; nothing is added for them. Same as the website's Occasions page.
class ModulesTab extends ConsumerWidget {
  const ModulesTab({super.key});

  Future<void> _open(BuildContext context, WidgetRef ref, OccasionModule m) async {
    await pushPage<void>(context, ModulePage(moduleId: m.id));
    await ref.read(modulesProvider.notifier).reload();
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    final AsyncValue<List<OccasionModule>> modules = ref.watch(modulesProvider);
    return RefreshIndicator(
      onRefresh: () => ref.read(modulesProvider.notifier).reload(),
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          ...switch (modules) {
            AsyncData<List<OccasionModule>>(:final List<OccasionModule> value) when value.isEmpty => <Widget>[
                EmptyView(
                  icon: FLucideIcons.calendarPlus,
                  title: 'No occasions yet',
                  text: 'Make one for birthdays, anniversaries or any date you send wishes on.',
                  action: () => openModuleBuilder(context, ref),
                  actionLabel: 'New occasion',
                ),
              ],
            AsyncData<List<OccasionModule>>(:final List<OccasionModule> value) => <Widget>[
                CardSection(
                  children: <Widget>[
                    for (final OccasionModule m in value)
                      ListRow(
                        leading: IconBadge(moduleIcon(m)),
                        title: m.name,
                        subtitle: m.ready ? 'Sends by itself at ${hhmmText(m.time)}' : 'Sends by hand',
                        badge: m.todayCount > 0 ? Pill('${m.todayCount} today') : null,
                        trailing: Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
                        onTap: () => _open(context, ref, m),
                      ),
                  ],
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
                  child: Text(
                    'Up to $maxModules occasions. Add dates on each occasion, or with a column of the same name in the '
                    'Excel import.',
                    style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                  ),
                ),
              ],
            AsyncError<List<OccasionModule>>(:final Object error) => <Widget>[
                EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load occasions',
                  text: errorText(error),
                  action: () => ref.read(modulesProvider.notifier).reload(),
                  actionLabel: 'Try again',
                ),
              ],
            _ => <Widget>[const LoadingView()],
          },
        ],
      ),
    );
  }
}

/// Opens the builder; after creating, the new occasion's page opens.
Future<void> openModuleBuilder(BuildContext context, WidgetRef ref) async {
  final List<OccasionModule> current = ref.read(modulesProvider).value ?? const <OccasionModule>[];
  if (current.length >= maxModules) {
    showInfoToast(context, 'You have $maxModules occasions', description: 'Delete one to make another.');
    return;
  }

  final OccasionModule? created =
      await pushPage<OccasionModule>(context, ModuleBuilderPage(hasBirthday: current.any((OccasionModule m) => m.kind == 'birthday')));
  if (created == null || !context.mounted) {
    return;
  }

  await ref.read(modulesProvider.notifier).reload();
  if (context.mounted) {
    await pushPage<void>(context, ModulePage(moduleId: created.id));
    await ref.read(modulesProvider.notifier).reload();
  }
}

/// A new occasion: its name, when it comes (every year, one time, or birthdays) and an optional second name.
class ModuleBuilderPage extends ConsumerStatefulWidget {
  const ModuleBuilderPage({super.key, required this.hasBirthday});

  /// Birthdays can be made only once.
  final bool hasBirthday;

  @override
  ConsumerState<ModuleBuilderPage> createState() => _ModuleBuilderPageState();
}

class _ModuleBuilderPageState extends ConsumerState<ModuleBuilderPage> {
  final TextEditingController _name = TextEditingController();
  final TextEditingController _label = TextEditingController();
  String _kind = 'yearly';
  bool _secondName = false;
  bool _saving = false;
  String? _error;

  static const List<(String, String, String?)> _ideas = <(String, String, String?)>[
    ('Anniversary', 'yearly', null),
    ('Asthi', 'yearly', 'In memory of'),
    ('Punyatithi', 'yearly', 'In memory of'),
    ('Event', 'once', null),
  ];

  @override
  void dispose() {
    _name.dispose();
    _label.dispose();
    super.dispose();
  }

  void _useIdea(String name, String kind, String? label) => setState(() {
        _name.text = name;
        _kind = kind;
        _secondName = label != null;
        _label.text = label ?? '';
      });

  Future<void> _create() async {
    final String name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _error = 'Give the occasion a name.');
      return;
    }

    if (_kind != 'birthday' && _secondName && _label.text.trim().isEmpty) {
      setState(() => _error = 'Name the second name, for example "In memory of".');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final OccasionModule created = await ref
          .read(modulesApiProvider)
          .create(name, _kind, _kind != 'birthday' && _secondName ? _label.text.trim() : null);
      if (mounted) {
        showInfoToast(context, '${created.name} is ready', description: 'Add dates, then choose when it sends.');
        Navigator.of(context).pop(created);
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
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final List<(String, String, String)> kinds = <(String, String, String)>[
      ('yearly', 'Every year', 'Same day and month each year (anniversary, asthi)'),
      ('once', 'One time', 'A date with a year (an event)'),
      if (!widget.hasBirthday) ('birthday', 'Birthdays', 'Uses each contact’s birthday'),
    ];
    return PageScaffold(
      title: 'New occasion',
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            onPress: _saving ? null : _create,
            child: const Text('Create'),
          ),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          ChoiceChips<String>(
            options: <(String, String)>[
              for (final (String name, String _, String? _) in _ideas) (name, name),
              if (!widget.hasBirthday) ('Birthday', 'Birthday'),
            ],
            selected: _name.text,
            onChange: (String v) {
              final (String, String, String?)? idea = _ideas.where(((String, String, String?) i) => i.$1 == v).firstOrNull;
              if (idea != null) {
                _useIdea(idea.$1, idea.$2, idea.$3);
              } else {
                _useIdea('Birthday', 'birthday', null);
              }
            },
          ),
          const SizedBox(height: 16),
          FTextField(
            control: FTextFieldControl.managed(controller: _name),
            label: const Text('Name'),
            hint: 'For example: Anniversary',
            enabled: !_saving,
          ),
          const SizedBox(height: 16),
          const SectionLabel('When'),
          CardSection(
            margin: EdgeInsets.zero,
            children: <Widget>[
              for (final (String id, String title, String sub) in kinds)
                ListRow(
                  leading: FCheckbox(value: _kind == id, onChange: (_) => setState(() => _kind = id)),
                  title: title,
                  subtitle: sub,
                  onTap: () => setState(() => _kind = id),
                ),
            ],
          ),
          if (_kind != 'birthday') ...<Widget>[
            const SizedBox(height: 16),
            CardSection(
              margin: EdgeInsets.zero,
              children: <Widget>[
                ListRow(
                  leading: const IconBadge(FLucideIcons.userRound),
                  title: 'Each date also has a second name',
                  subtitle: 'For example the person remembered',
                  trailing: FSwitch(value: _secondName, onChange: (bool v) => setState(() => _secondName = v)),
                  onTap: () => setState(() => _secondName = !_secondName),
                ),
              ],
            ),
            if (_secondName) ...<Widget>[
              const SizedBox(height: 12),
              FTextField(
                control: FTextFieldControl.managed(controller: _label),
                label: const Text('Label'),
                hint: 'In memory of',
                    description: const Text('Use it in the message as {{for_name}}.'),
              ),
            ],
          ],
          if (_error != null) ...<Widget>[
            const SizedBox(height: 12),
            FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
          ],
          const SizedBox(height: 12),
          Text(
            'After creating it, add dates by hand or with a column of the same name in the Excel import, then choose the time '
            'and template it sends with.',
            style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
          ),
        ],
      ),
    );
  }
}
