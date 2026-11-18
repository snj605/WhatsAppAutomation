import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/format.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';
import 'wa_bubble.dart';

/// A text (and file) as WhatsApp shows it, for a person the user picks: their own name, second name, custom fields
/// and language, from the server's own preview. Without a person, an example with sample names.
/// Used by templates, occasion settings and the new-message screen, the same as the website.
class WaPersonPreview extends ConsumerStatefulWidget {
  const WaPersonPreview({super.key, required this.text, this.file, this.moduleId});

  final String text;
  final WaFile? file;

  /// Occasion wishes fill {{for_name}} from this occasion.
  final String? moduleId;

  @override
  ConsumerState<WaPersonPreview> createState() => _WaPersonPreviewState();
}

class _WaPersonPreviewState extends ConsumerState<WaPersonPreview> {
  ({String id, String name})? _person;
  PersonMessage? _message;
  Timer? _timer;
  int _seq = 0;

  @override
  void didUpdateWidget(WaPersonPreview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.text != widget.text || oldWidget.file?.id != widget.file?.id || oldWidget.moduleId != widget.moduleId) {
      _schedule();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  void _schedule() {
    _timer?.cancel();
    if (_person != null && widget.text.trim().isNotEmpty) {
      _timer = Timer(const Duration(milliseconds: 400), _load);
    }
  }

  Future<void> _load() async {
    final ({String id, String name})? person = _person;
    if (person == null) {
      return;
    }

    final int seq = ++_seq;
    try {
      final BroadcastPreview p = await ref.read(broadcastsApiProvider).preview(
            BroadcastInput(
              name: 'Preview',
              text: widget.text,
              contactIds: <String>[person.id],
              attachmentId: widget.file?.id,
              moduleId: widget.moduleId,
              sendVideoAsDocument: widget.file?.isBigVideo ?? false,
            ),
            forContactId: person.id,
          );
      if (mounted && seq == _seq) {
        setState(() => _message = p.person);
      }
    } on Object {
      if (mounted && seq == _seq) {
        setState(() => _message = null);
      }
    }
  }

  Future<void> _pick() async {
    final String? query = await showTextDialog(
      context: context,
      title: 'Preview for',
      body: 'Type a contact’s name or number to see exactly what they get.',
      confirm: 'Find',
      hint: 'Ramesh',
    );
    if (query == null || !mounted) {
      return;
    }

    final ContactPage page = await ref.read(contactsApiProvider).contacts(search: query, pageSize: 6);
    if (!mounted) {
      return;
    }

    if (page.items.isEmpty) {
      await showChoiceDialog<String>(
        context: context,
        title: 'Nobody found',
        body: 'No contact matches "$query".',
        choices: const <DialogChoice<String>>[(value: '', label: 'OK', variant: FButtonVariant.outline)],
      );
      return;
    }

    final Contact? chosen = await showChoiceDialog<Contact>(
      context: context,
      title: 'Preview for',
      body: 'Choose the person.',
      choices: <DialogChoice<Contact>>[
        for (final Contact c in page.items) (value: c, label: c.displayName, variant: FButtonVariant.outline),
      ],
    );
    if (chosen != null && mounted) {
      setState(() {
        _person = (id: chosen.id, name: chosen.displayName);
        _message = null;
      });
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final PersonMessage? m = _message;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Row(
          children: <Widget>[
            Expanded(
              child: Text(
                _person == null ? 'Example with sample names' : 'For ${_person!.name}',
                style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
              ),
            ),
            if (_person != null)
              FButton(
                variant: FButtonVariant.ghost,
                size: FButtonSizeVariant.sm,
                mainAxisSize: MainAxisSize.min,
                onPress: () => setState(() {
                  _person = null;
                  _message = null;
                }),
                child: const Text('Example'),
              ),
            FButton(
              variant: FButtonVariant.ghost,
              size: FButtonSizeVariant.sm,
              mainAxisSize: MainAxisSize.min,
              prefix: const Icon(FLucideIcons.user),
              onPress: _pick,
              child: const Text('Preview for…'),
            ),
          ],
        ),
        const SizedBox(height: 6),
        if (m != null && m.text == null)
          FAlert(
            variant: FAlertVariant.destructive,
            icon: const Icon(FLucideIcons.circleAlert),
            title: Text('${m.name} is left out: ${m.skipReason ?? 'no message'}'),
          )
        else
          WaBubble(
            text: m?.text ?? sampleText(widget.text),
            file: widget.file,
            chip: _person?.name ?? 'Example',
          ),
      ],
    );
  }
}
