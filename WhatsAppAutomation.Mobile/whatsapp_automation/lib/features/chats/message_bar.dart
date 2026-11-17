import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import '../files/files_api.dart';
import '../templates/templates_api.dart';
import 'chat_bubble.dart';

/// Puts [value] where the cursor is, with a space before it when needed (same rule as the website).
void insertAtCursor(TextEditingController controller, String value) {
  final String current = controller.text;
  final TextSelection sel = controller.selection;
  final int start = sel.isValid ? sel.start : current.length;
  final int end = sel.isValid ? sel.end : current.length;
  final bool needsSpace = start > 0 && !RegExp(r'\s$').hasMatch(current.substring(0, start));
  final String piece = '${needsSpace ? ' ' : ''}$value';
  controller.value = TextEditingValue(
    text: current.replaceRange(start, end, piece),
    selection: TextSelection.collapsed(offset: start + piece.length),
  );
}

/// Choose a file on the phone and upload it. Shows what went wrong as a toast; returns null then.
Future<UploadedFile?> pickAndUpload(BuildContext context, WidgetRef ref) async {
  final PickedFile? picked;
  try {
    picked = await pickFile();
  } on Object catch (e) {
    if (context.mounted) {
      showErrorToast(context, 'Could not open your files', e);
    }

    return null;
  }

  if (picked == null || !context.mounted) {
    return null;
  }

  final String? problem = fileProblem(picked);
  if (problem != null) {
    showFToast(
      context: context,
      variant: FToastVariant.destructive,
      icon: const Icon(FLucideIcons.circleAlert),
      title: const Text('Cannot attach this file'),
      description: Text(problem),
    );
    return null;
  }

  try {
    return await ref.read(filesApiProvider).upload(picked);
  } on Object catch (e) {
    if (context.mounted) {
      showErrorToast(context, 'Not attached', e);
    }

    return null;
  }
}

/// The small pill buttons above a message box: Name, More, Templates and (optionally) File.
class ComposerTools extends StatelessWidget {
  const ComposerTools({super.key, required this.controller, this.enabled = true, this.onAttach, this.attaching = false});

  final TextEditingController controller;
  final bool enabled;

  /// Shows a File button when set.
  final VoidCallback? onAttach;
  final bool attaching;

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: <Widget>[
            _ToolChip(
              icon: FLucideIcons.user,
              label: 'Name',
              highlighted: true,
              onTap: enabled ? () => insertAtCursor(controller, namePlaceholder) : null,
            ),
            _ToolChip(
              icon: FLucideIcons.plus,
              label: 'More',
              onTap: enabled
                  ? () async {
                      final String? value = await pushPage<String>(context, const _InsertsPage());
                      if (value != null) {
                        insertAtCursor(controller, value);
                      }
                    }
                  : null,
            ),
            _ToolChip(
              icon: FLucideIcons.fileText,
              label: 'Templates',
              onTap: enabled
                  ? () async {
                      final String? text = await pushPage<String>(context, const TemplatePickerPage());
                      if (text != null) {
                        controller.value = TextEditingValue(text: text, selection: TextSelection.collapsed(offset: text.length));
                      }
                    }
                  : null,
            ),
            if (onAttach != null)
              _ToolChip(
                icon: FLucideIcons.paperclip,
                label: attaching ? 'Uploading…' : 'File',
                onTap: enabled && !attaching ? onAttach : null,
              ),
          ],
        ),
      );
}

class _ToolChip extends StatelessWidget {
  const _ToolChip({required this.icon, required this.label, required this.onTap, this.highlighted = false});

  final IconData icon;
  final String label;
  final VoidCallback? onTap;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final FColors colors = context.theme.colors;
    final Color fg = highlighted ? colors.secondaryForeground : colors.foreground;
    return Padding(
      padding: const EdgeInsets.only(right: 8),
      child: GestureDetector(
        onTap: onTap,
        child: Opacity(
          opacity: onTap == null ? 0.5 : 1,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
            decoration: BoxDecoration(
              color: highlighted ? colors.secondary : colors.card,
              borderRadius: BorderRadius.circular(999),
              border: Border.all(color: highlighted ? colors.secondary : colors.border),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Icon(icon, size: 15, color: fg),
                const SizedBox(width: 6),
                Text(label, style: context.theme.typography.body.sm.copyWith(color: fg, fontWeight: FontWeight.w600)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The attached file above the message box, with a button to remove it.
class AttachmentChip extends StatelessWidget {
  const AttachmentChip({super.key, required this.file, required this.onRemove});

  final UploadedFile file;
  final VoidCallback? onRemove;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Container(
      padding: const EdgeInsets.fromLTRB(10, 8, 4, 8),
      decoration: BoxDecoration(
        color: theme.colors.secondary,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: <Widget>[
          Icon(fileIcon(file.contentType), size: 20, color: theme.colors.secondaryForeground),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              file.fileName,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.typography.body.sm.copyWith(color: theme.colors.secondaryForeground, fontWeight: FontWeight.w600),
            ),
          ),
          Text(
            fileSizeText(file.sizeBytes),
            style: theme.typography.body.xs.copyWith(color: theme.colors.secondaryForeground),
          ),
          FButton.icon(
            variant: FButtonVariant.ghost,
            size: FButtonSizeVariant.sm,
            onPress: onRemove,
            child: const Icon(FLucideIcons.x),
          ),
        ],
      ),
    );
  }
}

/// The message box at the bottom of a group, category or Birthdays chat, like WhatsApp.
/// [onSend] gets the text and the uploaded file's id, and returns true when the message went out;
/// the box is then cleared.
class MessageBar extends ConsumerStatefulWidget {
  const MessageBar({
    super.key,
    required this.onSend,
    this.initialText = '',
    this.hint = 'Type a message',
    this.disabled = false,
    this.busy = false,
    this.disabledNote,
  });

  final Future<bool> Function(String text, String? attachmentId) onSend;
  final String initialText;
  final String hint;
  final bool disabled;
  final bool busy;

  /// Shown above the box while sending is not possible (for example "Tick who should get it").
  final String? disabledNote;

  @override
  ConsumerState<MessageBar> createState() => _MessageBarState();
}

class _MessageBarState extends ConsumerState<MessageBar> {
  late final TextEditingController _text = TextEditingController(text: widget.initialText);
  bool _hasText = false;
  UploadedFile? _attachment;
  bool _uploading = false;

  @override
  void initState() {
    super.initState();
    _hasText = _text.text.trim().isNotEmpty;
    _text.addListener(() {
      final bool has = _text.text.trim().isNotEmpty;
      if (has != _hasText) {
        setState(() => _hasText = has);
      }
    });
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _attach() async {
    setState(() => _uploading = true);
    final UploadedFile? file = await pickAndUpload(context, ref);
    if (mounted) {
      setState(() {
        _uploading = false;
        _attachment = file ?? _attachment;
      });
    }
  }

  Future<void> _send() async {
    final String text = _text.text.trim();
    if (text.isEmpty || widget.disabled || widget.busy || _uploading) {
      return;
    }

    FocusScope.of(context).unfocus();
    if (await widget.onSend(text, _attachment?.id) && mounted) {
      _text.clear();
      setState(() => _attachment = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final bool canSend = _hasText && !widget.disabled && !widget.busy && !_uploading;
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
      decoration: BoxDecoration(
        color: theme.colors.card,
        border: Border(top: BorderSide(color: theme.colors.border, width: 0.6)),
        boxShadow: cardShadow(context),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            if (widget.disabled && widget.disabledNote != null) ...<Widget>[
              Text(widget.disabledNote!, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
              const SizedBox(height: 6),
            ],
            ComposerTools(controller: _text, enabled: !widget.busy, onAttach: _attach, attaching: _uploading),
            if (_attachment != null) ...<Widget>[
              const SizedBox(height: 8),
              AttachmentChip(file: _attachment!, onRemove: widget.busy ? null : () => setState(() => _attachment = null)),
            ],
            const SizedBox(height: 8),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: <Widget>[
                Expanded(
                  child: FTextField.multiline(
                    control: FTextFieldControl.managed(controller: _text),
                    hint: _attachment != null ? 'Caption for the file' : widget.hint,
                    minLines: 1,
                    maxLines: 5,
                    enabled: !widget.busy,
                  ),
                ),
                const SizedBox(width: 8),
                GestureDetector(
                  onTap: canSend ? _send : null,
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 150),
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(
                      color: canSend ? theme.colors.primary : theme.colors.muted,
                      shape: BoxShape.circle,
                    ),
                    child: Center(
                      child: widget.busy
                          ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
                          : Icon(
                              FLucideIcons.send,
                              size: 20,
                              color: canSend ? theme.colors.primaryForeground : theme.colors.mutedForeground,
                            ),
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// "More": the other pieces that can go into a message.
class _InsertsPage extends StatelessWidget {
  const _InsertsPage();

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Put into the message',
        child: ListView(
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: <Widget>[
            CardSection(
              title: 'Each person gets their own value',
              children: <Widget>[
                for (final (String label, String hint, String value) in moreInserts)
                  ListRow(
                    leading: const IconBadge(FLucideIcons.sparkles),
                    title: label,
                    subtitle: hint,
                    trailing: const Icon(FLucideIcons.plus, size: 18),
                    onTap: () => Navigator.of(context).pop(value),
                  ),
              ],
            ),
          ],
        ),
      );
}

/// The user's own templates and the ready messages. Returns the chosen text.
class TemplatePickerPage extends ConsumerStatefulWidget {
  const TemplatePickerPage({super.key});

  @override
  ConsumerState<TemplatePickerPage> createState() => _TemplatePickerPageState();
}

class _TemplatePickerPageState extends ConsumerState<TemplatePickerPage> {
  List<MessageTemplate>? _mine;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    List<MessageTemplate> list = const <MessageTemplate>[];
    try {
      list = await ref.read(templatesApiProvider).list();
    } on Object {
      // The ready messages still work.
    }

    if (mounted) {
      setState(() => _mine = list);
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Choose a message',
        child: ListView(
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: <Widget>[
            if (_mine == null)
              const LoadingView()
            else if (_mine!.isNotEmpty)
              CardSection(
                title: 'My templates',
                children: <Widget>[
                  for (final MessageTemplate t in _mine!)
                    ListRow(
                      leading: const IconBadge(FLucideIcons.fileText),
                      title: t.name,
                      subtitle: friendlyPlaceholders(t.text),
                      onTap: () => Navigator.of(context).pop(t.text),
                    ),
                ],
              ),
            CardSection(
              title: 'Ready messages',
              children: <Widget>[
                for (final (String label, String text) in readyMessages)
                  ListRow(
                    leading: const IconBadge(FLucideIcons.sparkles, color: Color(0xFFF59E0B)),
                    title: label,
                    subtitle: friendlyPlaceholders(text),
                    onTap: () => Navigator.of(context).pop(text),
                  ),
              ],
            ),
          ],
        ),
      );
}
