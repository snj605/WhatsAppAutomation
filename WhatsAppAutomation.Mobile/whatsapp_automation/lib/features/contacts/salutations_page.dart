import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import 'contact_models.dart';
import 'contacts_api.dart';

const int maxSalutations = 30;

/// The user's salutations, each in English and Gujarati (Shri / શ્રી). A contact picks one; {{salutation}} in a
/// message uses the contact's language. Excel offers only these. Same as the website's Salutations tab.
class SalutationsPage extends ConsumerWidget {
  const SalutationsPage({super.key});

  Future<void> _edit(BuildContext context, WidgetRef ref, Salutation? s) async {
    final List<Salutation> current = ref.read(salutationsProvider).value ?? const <Salutation>[];
    if (s == null && current.length >= maxSalutations) {
      showInfoToast(context, 'You have $maxSalutations salutations', description: 'Delete one to add another.');
      return;
    }

    final bool? changed = await pushPage<bool>(context, SalutationEditPage(salutation: s));
    if (changed ?? false) {
      await ref.read(salutationsProvider.notifier).reload();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    final AsyncValue<List<Salutation>> list = ref.watch(salutationsProvider);
    return PageScaffold(
      title: 'Salutations',
      subtitle: 'In English and Gujarati',
      actions: <Widget>[
        FButton.icon(onPress: () => _edit(context, ref, null), child: const Icon(FLucideIcons.plus)),
      ],
      child: RefreshIndicator(
        onRefresh: () => ref.read(salutationsProvider.notifier).reload(),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.only(bottom: 24),
          children: <Widget>[
            ...switch (list) {
              AsyncData<List<Salutation>>(:final List<Salutation> value) when value.isEmpty => <Widget>[
                  EmptyView(
                    icon: FLucideIcons.user,
                    title: 'No salutations yet',
                    text: 'Add the ones you use, each in both languages: Shri / શ્રી, Smt. / શ્રીમતી, Pujya / પૂજ્ય.',
                    action: () => _edit(context, ref, null),
                    actionLabel: 'New salutation',
                  ),
                ],
              AsyncData<List<Salutation>>(:final List<Salutation> value) => <Widget>[
                  CardSection(
                    children: <Widget>[
                      for (final Salutation s in value)
                        ListRow(
                          leading: const IconBadge(FLucideIcons.user),
                          title: s.label,
                          subtitle: '${s.contactCount} ${s.contactCount == 1 ? 'contact' : 'contacts'}',
                          trailing: Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
                          onTap: () => _edit(context, ref, s),
                        ),
                    ],
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
                    child: Text(
                      'Salutations saved before were added here with the same text in both languages: change the Gujarati text where needed.',
                      style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                    ),
                  ),
                ],
              AsyncError<List<Salutation>>(:final Object error) => <Widget>[
                  EmptyView(
                    icon: FLucideIcons.wifiOff,
                    title: 'Could not load salutations',
                    text: errorText(error),
                    action: () => ref.read(salutationsProvider.notifier).reload(),
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

/// A new salutation or a change: the English and the Gujarati text, both needed.
class SalutationEditPage extends ConsumerStatefulWidget {
  const SalutationEditPage({super.key, this.salutation});

  final Salutation? salutation;

  @override
  ConsumerState<SalutationEditPage> createState() => _SalutationEditPageState();
}

class _SalutationEditPageState extends ConsumerState<SalutationEditPage> {
  late final TextEditingController _en = TextEditingController(text: widget.salutation?.nameEn ?? '');
  late final TextEditingController _gu = TextEditingController(text: widget.salutation?.nameGu ?? '');
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _en.dispose();
    _gu.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_en.text.trim().isEmpty || _gu.text.trim().isEmpty) {
      setState(() => _error = 'Type both: the English text (Shri) and the Gujarati text (શ્રી).');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(salutationsApiProvider).save(widget.salutation?.id, _en.text.trim(), _gu.text.trim());
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
    final Salutation s = widget.salutation!;
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete ${s.label}?',
      body: s.contactCount == 0
          ? 'No contact has it yet.'
          : '${s.contactCount} ${s.contactCount == 1 ? 'contact has' : 'contacts have'} it; they are left without a salutation.',
      confirm: 'Delete',
      destructive: true,
      icon: FLucideIcons.trash2,
    );
    if (!yes || !mounted) {
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(salutationsApiProvider).remove(s.id);
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
    final bool isNew = widget.salutation == null;
    return PageScaffold(
      title: isNew ? 'New salutation' : widget.salutation!.label,
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            onPress: _saving ? null : _save,
            child: Text(isNew ? 'Add salutation' : 'Save'),
          ),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          FTextField(
            control: FTextFieldControl.managed(controller: _en),
            label: const Text('English'),
            hint: 'For example: Shri',
            enabled: !_saving,
          ),
          const SizedBox(height: 16),
          FTextField(
            control: FTextFieldControl.managed(controller: _gu),
            label: const Text('Gujarati'),
            hint: 'ઉદાહરણ: શ્રી',
            enabled: !_saving,
          ),
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
              child: const Text('Delete salutation'),
            ),
          ],
        ],
      ),
    );
  }
}
