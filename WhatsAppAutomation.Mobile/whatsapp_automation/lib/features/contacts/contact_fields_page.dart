import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../app/confirm_identity.dart';
import '../../core/ui.dart';
import 'contact_models.dart';
import 'contacts_api.dart';

const int maxContactFields = 30;

/// The user's own contact fields (city, shop name, anniversary date, …): shown on each contact, used in messages as
/// {{key}}, as columns in the Excel import, and as a filter. Same as the website's Contact fields tab.
class ContactFieldsPage extends ConsumerWidget {
  const ContactFieldsPage({super.key});

  Future<void> _edit(BuildContext context, WidgetRef ref, ContactField? field) async {
    final List<ContactField> current = ref.read(contactFieldsProvider).value ?? const <ContactField>[];
    if (field == null && current.length >= maxContactFields) {
      showInfoToast(context, 'You have $maxContactFields fields', description: 'Delete one to add another.');
      return;
    }

    final bool? changed = await pushPage<bool>(context, ContactFieldEditPage(field: field));
    if (changed ?? false) {
      await ref.read(contactFieldsProvider.notifier).reload();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    final AsyncValue<List<ContactField>> fields = ref.watch(contactFieldsProvider);
    return PageScaffold(
      title: 'Contact fields',
      subtitle: 'Your own details on each contact',
      actions: <Widget>[
        FButton.icon(onPress: () => _edit(context, ref, null), child: const Icon(FLucideIcons.plus)),
      ],
      child: RefreshIndicator(
        onRefresh: () => ref.read(contactFieldsProvider.notifier).reload(),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.only(bottom: 24),
          children: <Widget>[
            ...switch (fields) {
              AsyncData<List<ContactField>>(:final List<ContactField> value) when value.isEmpty => <Widget>[
                  EmptyView(
                    icon: FLucideIcons.tags,
                    title: 'No fields yet',
                    text: 'Add details you keep for people, like City or Shop name. Use them in messages as {{city}}.',
                    action: () => _edit(context, ref, null),
                    actionLabel: 'New field',
                  ),
                ],
              AsyncData<List<ContactField>>(:final List<ContactField> value) => <Widget>[
                  CardSection(
                    children: <Widget>[
                      for (final ContactField f in value)
                        ListRow(
                          leading: IconBadge(_icon(f.type)),
                          title: f.name,
                          subtitle: '{{${f.key}}} · ${_typeName(f.type)} · ${f.contactCount} '
                              '${f.contactCount == 1 ? 'contact' : 'contacts'}',
                          trailing: Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
                          onTap: () => _edit(context, ref, f),
                        ),
                    ],
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
                    child: Text(
                      'Up to $maxContactFields fields. In the Excel import, a column with the field’s name fills it.',
                      style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                    ),
                  ),
                ],
              AsyncError<List<ContactField>>(:final Object error) => <Widget>[
                  EmptyView(
                    icon: FLucideIcons.wifiOff,
                    title: 'Could not load fields',
                    text: errorText(error),
                    action: () => ref.read(contactFieldsProvider.notifier).reload(),
                    actionLabel: 'Try again',
                  ),
                ],
              _ => <Widget>[const LoadingView()],
            },
          ],
        ),
      ),
    );
  }
}

IconData _icon(String type) => switch (type) {
      'number' => FLucideIcons.hash,
      'date' => FLucideIcons.calendar,
      'choice' => FLucideIcons.listChecks,
      _ => FLucideIcons.fileText,
    };

String _typeName(String type) => ContactField.types.firstWhere(((String, String) t) => t.$1 == type, orElse: () => (type, type)).$2;

/// A new field (name, type, choices) or a change to one (name and choices; the type stays).
class ContactFieldEditPage extends ConsumerStatefulWidget {
  const ContactFieldEditPage({super.key, this.field});

  final ContactField? field;

  @override
  ConsumerState<ContactFieldEditPage> createState() => _ContactFieldEditPageState();
}

class _ContactFieldEditPageState extends ConsumerState<ContactFieldEditPage> {
  late final TextEditingController _name = TextEditingController(text: widget.field?.name ?? '');
  late final TextEditingController _options = TextEditingController(text: widget.field?.options.join('\n') ?? '');
  late String _type = widget.field?.type ?? 'text';
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _options.dispose();
    super.dispose();
  }

  List<String> get _choiceList =>
      _options.text.split('\n').map((String o) => o.trim()).where((String o) => o.isNotEmpty).toSet().toList();

  Future<void> _save() async {
    final String name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _error = 'Give the field a name.');
      return;
    }

    if (_type == 'choice' && _choiceList.isEmpty) {
      setState(() => _error = 'Add the choices, one per line.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final ContactFieldsApi api = ref.read(contactFieldsApiProvider);
      final List<String> options = _type == 'choice' ? _choiceList : const <String>[];
      if (widget.field == null) {
        await api.create(name, _type, options);
      } else {
        await api.update(widget.field!.id, name, options);
      }

      if (mounted) {
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

  Future<void> _delete() async {
    final ContactField f = widget.field!;
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete ${f.name}?',
      body: f.contactCount == 0
          ? 'No contact has a value in it.'
          : 'Its value goes from ${f.contactCount} ${f.contactCount == 1 ? 'contact' : 'contacts'}. Messages with '
              '{{${f.key}}} will skip people without it.',
      confirm: 'Delete',
      destructive: true,
      icon: FLucideIcons.trash2,
    );
    // A delete also needs fingerprint, face or the password, so nothing is deleted by a slip.
    if (!yes || !mounted || !await confirmIdentity(context, ref, 'Confirm to delete.') || !mounted) {
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(contactFieldsApiProvider).remove(f.id);
      if (mounted) {
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() {
          _saving = false;
          _error = errorText(e);
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final bool isNew = widget.field == null;
    return PageScaffold(
      title: isNew ? 'New field' : widget.field!.name,
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            onPress: _saving ? null : _save,
            child: Text(isNew ? 'Add field' : 'Save'),
          ),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          FTextField(
            control: FTextFieldControl.managed(controller: _name),
            label: const Text('Name'),
            hint: 'For example: City',
            enabled: !_saving,
          ),
          const SizedBox(height: 16),
          const SectionLabel('Type'),
          if (isNew)
            ChoiceChips<String>(
              options: ContactField.types,
              selected: _type,
              onChange: _saving ? null : (String t) => setState(() => _type = t),
            )
          else
            Text(
              '${_typeName(_type)} (it cannot change: values are kept in this form)',
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            ),
          if (_type == 'choice') ...<Widget>[
            const SizedBox(height: 16),
            FTextField.multiline(
              control: FTextFieldControl.managed(controller: _options),
              label: const Text('Choices, one per line'),
              hint: 'Retail\nWholesale',
              minLines: 3,
              maxLines: 10,
              enabled: !_saving,
            ),
          ],
          if (!isNew) ...<Widget>[
            const SizedBox(height: 12),
            Text(
              'In messages: {{${widget.field!.key}}}',
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            ),
          ],
          if (_error != null) ...<Widget>[
            const SizedBox(height: 12),
            FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
          ],
          if (!isNew) ...<Widget>[
            const SizedBox(height: 24),
            FButton(
              variant: FButtonVariant.destructive,
              onPress: _saving ? null : _delete,
              prefix: const Icon(FLucideIcons.trash2),
              child: const Text('Delete field'),
            ),
          ],
        ],
      ),
    );
  }
}
