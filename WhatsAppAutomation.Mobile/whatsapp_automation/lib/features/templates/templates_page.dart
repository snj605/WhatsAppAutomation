import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../../core/ui.dart';
import '../chats/message_bar.dart';
import 'templates_api.dart';

/// The user's saved messages: add, change, delete. Same as the website's Templates page.
class TemplatesPage extends ConsumerStatefulWidget {
  const TemplatesPage({super.key});

  @override
  ConsumerState<TemplatesPage> createState() => _TemplatesPageState();
}

class _TemplatesPageState extends ConsumerState<TemplatesPage> {
  List<MessageTemplate>? _list;
  String? _loadError;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final List<MessageTemplate> list = await ref.read(templatesApiProvider).list();
      if (mounted) {
        setState(() {
          _list = list;
          _loadError = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_list == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load templates', e);
        }
      }
    }
  }

  Future<void> _edit(MessageTemplate? t) async {
    final bool? saved = await pushPage<bool>(context, _TemplateEditPage(template: t));
    if (saved ?? false) {
      await _load();
    }
  }

  Future<void> _delete(MessageTemplate t) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete template?',
      body: '"${t.name}" will be removed. Messages already sent stay as they are.',
      confirm: 'Delete',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      await ref.read(templatesApiProvider).remove(t.id);
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Templates',
        actions: <Widget>[FHeaderAction(icon: const Icon(FLucideIcons.plus), onPress: () => _edit(null))],
        child: _list == null
            ? (_loadError == null
                ? const LoadingView()
                : EmptyView(
                    icon: FLucideIcons.wifiOff,
                    title: 'Could not load templates',
                    text: _loadError!,
                    action: () {
                      setState(() => _loadError = null);
                      _load();
                    },
                    actionLabel: 'Try again',
                  ))
            : _list!.isEmpty
                ? EmptyView(
                    icon: FLucideIcons.fileText,
                    text: 'No templates yet. Save messages you send often.',
                    action: () => _edit(null),
                    actionLabel: 'Add template',
                  )
                : ListView(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    children: <Widget>[
                      CardSection(
                        children: <Widget>[
                      for (final MessageTemplate t in _list!)
                        ListRow(
                          leading: const IconBadge(FLucideIcons.fileText),
                          title: t.name,
                          subtitle: friendlyPlaceholders(t.text),
                          trailing: FButton.icon(
                            variant: FButtonVariant.ghost,
                            onPress: () => _delete(t),
                            child: const Icon(FLucideIcons.trash2),
                          ),
                          onTap: () => _edit(t),
                        ),
                        ],
                      ),
                    ],
                  ),
      );
}

class _TemplateEditPage extends ConsumerStatefulWidget {
  const _TemplateEditPage({this.template});

  final MessageTemplate? template;

  @override
  ConsumerState<_TemplateEditPage> createState() => _TemplateEditPageState();
}

class _TemplateEditPageState extends ConsumerState<_TemplateEditPage> {
  late final TextEditingController _name = TextEditingController(text: widget.template?.name ?? '');
  late final TextEditingController _text = TextEditingController(text: widget.template?.text ?? '');
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    // The preview follows the text while typing.
    _text.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _name.dispose();
    _text.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty || _text.text.trim().isEmpty) {
      setState(() => _error = 'Enter a name and the message.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(templatesApiProvider).save(widget.template?.id, _name.text.trim(), _text.text.trim());
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

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: widget.template == null ? 'New template' : 'Edit template',
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
            FTextField(
              control: FTextFieldControl.managed(controller: _name),
              label: const Text('Name'),
              hint: 'Diwali wishes',
              enabled: !_saving,
            ),
            const SizedBox(height: 16),
            ComposerTools(controller: _text, enabled: !_saving),
            const SizedBox(height: 8),
            FTextField.multiline(
              control: FTextFieldControl.managed(controller: _text),
              label: const Text('Message'),
              minLines: 5,
              maxLines: 12,
            ),
            if (_text.text.trim().isNotEmpty) ...<Widget>[
              const SizedBox(height: 16),
              const SectionLabel('Preview · for example Ramesh Patel'),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: context.theme.colors.card,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: context.theme.colors.border),
                  boxShadow: cardShadow(context),
                ),
                child: Text(sampleText(_text.text.trim()), style: context.theme.typography.body.md),
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
