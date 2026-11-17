import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import 'contact_models.dart';
import 'contacts_api.dart';

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
  late final TextEditingController _salutation = TextEditingController(text: _c?.salutation ?? '');
  late final TextEditingController _day = TextEditingController(text: _c?.birthday?.day.toString() ?? '');
  late final TextEditingController _month = TextEditingController(text: _c?.birthday?.month.toString() ?? '');
  late final TextEditingController _year = TextEditingController(text: _c?.birthday?.year?.toString() ?? '');
  late final TextEditingController _notes = TextEditingController(text: _c?.notes ?? '');
  late String _language = _c?.language ?? widget.defaultLanguage;
  late String? _categoryId = _c?.categoryId;
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
      _salutation,
      _day,
      _month,
      _year,
      _notes,
    ]) {
      c.dispose();
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

    final int? day = int.tryParse(_day.text.trim());
    final int? month = int.tryParse(_month.text.trim());
    final int? year = int.tryParse(_year.text.trim());
    if ((day == null) != (month == null)) {
      setState(() => _error = 'For a birthday enter both the day and the month (the year is optional).');
      return;
    }

    if ((day != null && (day < 1 || day > 31)) || (month != null && (month < 1 || month > 12))) {
      setState(() => _error = 'The birthday day must be 1 to 31 and the month 1 to 12.');
      return;
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
              salutation: _orNull(_salutation),
              birthday: day != null && month != null ? Birthday(day: day, month: month, year: year) : null,
              notes: _orNull(_notes),
              groupIds: _groupIds.toList(),
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
          _field(_salutation, 'Salutation (optional)', hint: 'Pujya, Dear…'),
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
          const SectionLabel('Birthday (optional)'),
          Row(
            children: <Widget>[
              Expanded(child: _field(_day, 'Day', hint: '14', keyboard: TextInputType.number)),
              const SizedBox(width: 8),
              Expanded(child: _field(_month, 'Month', hint: '11', keyboard: TextInputType.number)),
              const SizedBox(width: 8),
              Expanded(child: _field(_year, 'Year', hint: '1980', keyboard: TextInputType.number)),
            ],
          ),
          if (int.tryParse(_month.text) case final int m when m >= 1 && m <= 12)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Text(
                'Month: ${monthName(m)}',
                style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
              ),
            ),
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
