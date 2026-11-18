import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/ui.dart';
import '../modules/modules_api.dart';
import '../modules/modules_page.dart';
import 'contact_fields_page.dart';
import 'salutations_page.dart';
import 'contact_models.dart';
import 'contacts_api.dart';

/// One occasion date being edited.
class _OccasionRow {
  _OccasionRow(this.moduleId, {int? day, int? month, int? year, String? forName})
      : day = TextEditingController(text: day?.toString() ?? ''),
        month = TextEditingController(text: month?.toString() ?? ''),
        year = TextEditingController(text: year?.toString() ?? ''),
        forName = TextEditingController(text: forName ?? '');

  final String moduleId;
  final TextEditingController day;
  final TextEditingController month;
  final TextEditingController year;
  final TextEditingController forName;

  void dispose() {
    day.dispose();
    month.dispose();
    year.dispose();
    forName.dispose();
  }
}

/// "2019-03-05" → "05-03-2019", the form people type dates in.
String _dateForInput(String stored) {
  final List<String> p = stored.split('-');
  return p.length == 3 ? '${p[2]}-${p[1]}-${p[0]}' : stored;
}

/// "5-3-2019", "05/03/2019" or "2019-03-05" → "2019-03-05"; null when it is not a real date.
String? _dateToStore(String typed) {
  final List<int?> p = typed.trim().split(RegExp(r'[-/.\s]+')).map(int.tryParse).toList();
  if (p.length != 3 || p.any((int? x) => x == null)) {
    return null;
  }

  final (int y, int m, int d) = p[0]! > 31 ? (p[0]!, p[1]!, p[2]!) : (p[2]!, p[1]!, p[0]!);
  final DateTime t = DateTime(y, m, d);
  if (y < 1900 || y > 2200 || t.month != m || t.day != d) {
    return null;
  }

  return '${y.toString().padLeft(4, '0')}-${m.toString().padLeft(2, '0')}-${d.toString().padLeft(2, '0')}';
}

/// Add or edit a contact: same fields and checks as the website's contact dialog.
/// Returns the saved contact.
class ContactFormPage extends ConsumerStatefulWidget {
  const ContactFormPage({
    super.key,
    this.contact,
    required this.categories,
    required this.groups,
    required this.defaultLanguage,
  });

  /// Null for a new contact.
  final Contact? contact;
  final List<Category> categories;
  final List<Group> groups;
  final String defaultLanguage;

  @override
  ConsumerState<ContactFormPage> createState() => _ContactFormPageState();
}

class _ContactFormPageState extends ConsumerState<ContactFormPage> {
  late final Contact? _c = widget.contact;
  late final TextEditingController _phone = TextEditingController(text: _c?.phoneNumber ?? '+91');
  late final TextEditingController _savedName = TextEditingController(text: _c?.savedName ?? '');
  late final TextEditingController _nameEn = TextEditingController(text: _c?.nameEn ?? '');
  late final TextEditingController _nameHi = TextEditingController(text: _c?.nameHi ?? '');
  late final TextEditingController _nameGu = TextEditingController(text: _c?.nameGu ?? '');
  late final List<_OccasionRow> _occasions = <_OccasionRow>[
    for (final ContactOccasion o in _c?.occasions ?? const <ContactOccasion>[])
      _OccasionRow(o.moduleId, day: o.day, month: o.month, year: o.year, forName: o.forName),
  ];

  /// Text, number and date fields by field id (choice values are in [_choices]).
  final Map<String, TextEditingController> _fieldText = <String, TextEditingController>{};
  late final Map<String, String?> _choices = <String, String?>{...?_c?.fields};
  late final TextEditingController _notes = TextEditingController(text: _c?.notes ?? '');
  late String _language = _c?.language ?? widget.defaultLanguage;
  late String? _categoryId = _c?.categoryId;
  late String? _salutationId = _c?.salutationId;
  late Set<String> _groupIds = _c?.groups.map((({String id, String name}) g) => g.id).toSet() ?? <String>{};
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    for (final TextEditingController c in <TextEditingController>[
      _phone,
      _savedName,
      _nameEn,
      _nameHi,
      _nameGu,
      _notes,
      ..._fieldText.values,
    ]) {
      c.dispose();
    }

    for (final _OccasionRow o in _occasions) {
      o.dispose();
    }

    super.dispose();
  }

  String? _orNull(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();

  Future<void> _save() async {
    final String realName = switch (_language) {
      'hi' => _nameHi.text,
      'gu' => _nameGu.text,
      _ => _nameEn.text,
    }
        .trim();
    if (_phone.text.trim().isEmpty) {
      setState(() => _error = 'Enter the WhatsApp number.');
      return;
    }

    if (realName.isEmpty) {
      setState(() => _error = 'Enter the real name in ${languageLabel(_language)}: messages use it.');
      return;
    }

    final List<OccasionModule> modules = ref.read(modulesProvider).value ?? const <OccasionModule>[];
    final List<ContactOccasion> occasions = <ContactOccasion>[];
    for (final _OccasionRow o in _occasions) {
      final OccasionModule? m = modules.where((OccasionModule x) => x.id == o.moduleId).firstOrNull;
      final String name = m?.name ?? 'the occasion';
      final int? day = int.tryParse(o.day.text.trim());
      final int? month = int.tryParse(o.month.text.trim());
      final int? year = int.tryParse(o.year.text.trim());
      if (day == null || month == null || day < 1 || day > 31 || month < 1 || month > 12) {
        setState(() => _error = 'For $name enter the day (1 to 31) and the month (1 to 12), or remove it.');
        return;
      }

      if (m?.kind == 'once' && year == null) {
        setState(() => _error = '$name happens one time: enter the year too.');
        return;
      }

      if (year != null && (year < 1900 || year > 2200 || DateTime(year, month, day).day != day)) {
        setState(() => _error = 'The date for $name is not a real date.');
        return;
      }

      occasions.add(ContactOccasion(
        moduleId: o.moduleId,
        day: day,
        month: month,
        year: year,
        forName: m?.forNameLabel != null && o.forName.text.trim().isNotEmpty ? o.forName.text.trim() : null,
      ));
    }

    final List<ContactField> fields = ref.read(contactFieldsProvider).value ?? const <ContactField>[];
    final Map<String, String?> values = <String, String?>{};
    for (final ContactField f in fields) {
      if (f.type == 'choice') {
        values[f.id] = _choices[f.id];
        continue;
      }

      final String typed = _fieldText[f.id]?.text.trim() ?? '';
      if (typed.isEmpty) {
        values[f.id] = null;
      } else if (f.type == 'date') {
        final String? stored = _dateToStore(typed);
        if (stored == null) {
          setState(() => _error = '${f.name}: enter a date like 05-03-2019.');
          return;
        }

        values[f.id] = stored;
      } else if (f.type == 'number' && num.tryParse(typed.replaceAll(',', '')) == null) {
        setState(() => _error = '${f.name}: enter a number.');
        return;
      } else {
        values[f.id] = typed;
      }
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final Contact saved = await ref.read(contactsApiProvider).saveContact(
            _c?.id,
            ContactInput(
              phoneNumber: _phone.text.trim(),
              savedName: _orNull(_savedName),
              nameEn: _orNull(_nameEn),
              nameHi: _orNull(_nameHi),
              nameGu: _orNull(_nameGu),
              language: _language,
              categoryId: _categoryId,
              salutationId: _salutationId,
              notes: _orNull(_notes),
              groupIds: _groupIds.toList(),
              // Only sent once the occasions are known, so a failed load never clears the contact's dates.
              occasions: ref.read(modulesProvider).hasValue ? occasions : null,
              fields: ref.read(contactFieldsProvider).hasValue ? values : null,
            ),
          );
      if (mounted) {
        Navigator.of(context).pop(saved);
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

  Widget _field(TextEditingController c, String label, {String? hint, String? description, TextInputType? keyboard}) =>
      Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: FTextField(
          control: FTextFieldControl.managed(controller: c),
          label: Text(label),
          hint: hint,
          description: description == null ? null : Text(description),
          keyboardType: keyboard,
          enabled: !_saving,
        ),
      );

  /// The contact's dates, only for the user's own occasions; "Add" lists the occasions not on it yet.
  List<Widget> _occasionSection(FThemeData theme) {
    final AsyncValue<List<OccasionModule>> loaded = ref.watch(modulesProvider);
    final List<OccasionModule> modules = loaded.value ?? const <OccasionModule>[];
    final TextStyle muted = theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground);
    final List<OccasionModule> notAdded =
        modules.where((OccasionModule m) => !_occasions.any((_OccasionRow o) => o.moduleId == m.id)).toList();
    return <Widget>[
      const SectionLabel('Occasions'),
      if (loaded.hasError)
        Padding(padding: const EdgeInsets.only(bottom: 12), child: Text('Occasions could not load; dates stay as they are.', style: muted))
      else if (!loaded.hasValue)
        const Padding(padding: EdgeInsets.only(bottom: 12), child: FCircularProgress.loader(size: FCircularProgressSizeVariant.sm))
      else if (modules.isEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: GestureDetector(
            onTap: () => openModuleBuilder(context, ref),
            child: Text('Make an occasion first (Birthday, Anniversary, Asthi…), then add their dates here.', style: muted),
          ),
        )
      else ...<Widget>[
        for (final _OccasionRow o in _occasions)
          if (modules.where((OccasionModule m) => m.id == o.moduleId).firstOrNull case final OccasionModule m)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: DecoratedBox(
                decoration: BoxDecoration(border: Border.all(color: theme.colors.border), borderRadius: BorderRadius.circular(12)),
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(12, 8, 4, 0),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Row(
                        children: <Widget>[
                          Icon(moduleIcon(m), size: 18),
                          const SizedBox(width: 8),
                          Expanded(child: Text(m.name, style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600))),
                          FButton.icon(
                            variant: FButtonVariant.ghost,
                            onPress: _saving
                                ? null
                                : () => setState(() {
                                      _occasions.remove(o);
                                      o.dispose();
                                    }),
                            child: const Icon(FLucideIcons.x),
                          ),
                        ],
                      ),
                      const SizedBox(height: 4),
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: Row(
                          children: <Widget>[
                            Expanded(child: _field(o.day, 'Day', hint: '14', keyboard: TextInputType.number)),
                            const SizedBox(width: 8),
                            Expanded(child: _field(o.month, 'Month', hint: '11', keyboard: TextInputType.number)),
                            const SizedBox(width: 8),
                            Expanded(
                              child: _field(o.year, m.kind == 'once' ? 'Year' : 'Year (optional)', hint: '1980', keyboard: TextInputType.number),
                            ),
                          ],
                        ),
                      ),
                      if (m.forNameLabel != null)
                        Padding(padding: const EdgeInsets.only(right: 8), child: _field(o.forName, '${m.forNameLabel} (optional)')),
                    ],
                  ),
                ),
              ),
            ),
        if (notAdded.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: ChoiceChips<String>(
              options: <(String, String)>[for (final OccasionModule m in notAdded) (m.id, '+ ${m.name}')],
              selected: null,
              onChange: _saving ? null : (String id) => setState(() => _occasions.add(_OccasionRow(id))),
            ),
          ),
      ],
    ];
  }

  /// Picked from the user's own list (Shri / શ્રી, …); none is fine.
  List<Widget> _salutationSection(FThemeData theme) {
    final List<Salutation> list = ref.watch(salutationsProvider).value ?? const <Salutation>[];
    return <Widget>[
      const SectionLabel('Salutation'),
      if (list.isEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: GestureDetector(
            onTap: () => pushPage<void>(context, const SalutationsPage()),
            child: Text(
              'No salutations yet. Add them (Shri / શ્રી, …) in Salutations.',
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
        )
      else
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: ChoiceChips<String?>(
            options: <(String?, String)>[(null, 'None'), for (final Salutation s in list) (s.id, s.label)],
            selected: _salutationId,
            onChange: _saving ? null : (String? id) => setState(() => _salutationId = id),
          ),
        ),
    ];
  }

  /// The user's own fields, by type. Empty fields are left empty (nothing is stored for them).
  List<Widget> _fieldSection(FThemeData theme) {
    final List<ContactField> fields = ref.watch(contactFieldsProvider).value ?? const <ContactField>[];
    final TextStyle muted = theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground);
    return <Widget>[
      const SectionLabel('More details'),
      if (fields.isEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: GestureDetector(
            onTap: () => pushPage<void>(context, const ContactFieldsPage()),
            child: Text('Add your own fields, like City or Shop name, in Contact fields.', style: muted),
          ),
        )
      else
        for (final ContactField f in fields)
          if (f.type == 'choice')
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Text(f.name, style: theme.typography.body.sm.copyWith(fontWeight: FontWeight.w600)),
                  const SizedBox(height: 6),
                  ChoiceChips<String?>(
                    options: <(String?, String)>[(null, 'None'), for (final String o in f.options) (o, o)],
                    selected: _choices[f.id],
                    onChange: _saving ? null : (String? v) => setState(() => _choices[f.id] = v),
                  ),
                ],
              ),
            )
          else
            _field(
              _fieldText.putIfAbsent(
                f.id,
                () => TextEditingController(
                  text: switch (_c?.fields[f.id]) {
                    final String v when f.type == 'date' => _dateForInput(v),
                    final String v => v,
                    null => '',
                  },
                ),
              ),
              f.name,
              hint: switch (f.type) {
                'date' => 'DD-MM-YYYY',
                'number' => '0',
                _ => null,
              },
              description: 'In messages: {{${f.key}}}',
              keyboard: switch (f.type) {
                'number' => const TextInputType.numberWithOptions(decimal: true, signed: true),
                'date' => TextInputType.datetime,
                _ => null,
              },
            ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final bool isNew = _c == null;
    return PageScaffold(
      title: isNew ? 'Add contact' : 'Edit contact',
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            onPress: _saving ? null : _save,
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            child: Text(isNew ? 'Add contact' : 'Save'),
          ),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: FTextField(
              control: FTextFieldControl.managed(controller: _phone),
              label: const Text('WhatsApp number'),
              hint: '+919825012345',
              keyboardType: TextInputType.phone,
              inputFormatters: <TextInputFormatter>[FilteringTextInputFormatter.allow(RegExp(r'[0-9+\s]'))],
              enabled: !_saving,
            ),
          ),
          _field(_savedName, 'Saved name (optional)', description: 'How you know them, for example "Ramesh uncle".'),
          const SectionLabel('Language of their messages'),
          ChoiceChips<String>(
            options: languages,
            selected: _language,
            onChange: _saving ? null : (String l) => setState(() => _language = l),
          ),
          const SizedBox(height: 12),
          _field(_nameEn, _language == 'en' ? 'Name in English (needed)' : 'Name in English'),
          _field(_nameHi, _language == 'hi' ? 'Name in Hindi (needed)' : 'Name in Hindi'),
          _field(_nameGu, _language == 'gu' ? 'Name in Gujarati (needed)' : 'Name in Gujarati'),
          ..._salutationSection(theme),
          const SectionLabel('Category'),
          if (widget.categories.isEmpty)
            Text('No categories yet.', style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground))
          else
            ChoiceChips<String?>(
              options: <(String?, String)>[
                (null, 'None'),
                for (final Category c in widget.categories) (c.id, c.name),
              ],
              selected: _categoryId,
              onChange: _saving ? null : (String? id) => setState(() => _categoryId = id),
            ),
          const SizedBox(height: 12),
          const SectionLabel('Groups'),
          if (_c?.notOnWhatsApp ?? false)
            Text(
              'This number is not on WhatsApp, so it cannot be in groups. Changing the number checks it again.',
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            )
          else if (widget.groups.isEmpty)
            Text('No groups yet.', style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground))
          else
            MultiChoiceChips(
              options: <(String, String)>[for (final Group g in widget.groups) (g.id, g.name)],
              selected: _groupIds,
              onToggle: _saving
                  ? null
                  : (String id) => setState(() {
                        _groupIds = Set<String>.of(_groupIds);
                        if (!_groupIds.remove(id)) {
                          _groupIds.add(id);
                        }
                      }),
            ),
          const SizedBox(height: 12),
          ..._occasionSection(theme),
          ..._fieldSection(theme),
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: FTextField.multiline(
              control: FTextFieldControl.managed(controller: _notes),
              label: const Text('Notes (optional)'),
              minLines: 2,
              maxLines: 5,
            ),
          ),
          if (_error != null)
            FAlert(
              variant: FAlertVariant.destructive,
              icon: const Icon(FLucideIcons.circleAlert),
              title: Text(_error!),
            ),
        ],
      ),
    );
  }
}
