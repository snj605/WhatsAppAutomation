import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import '../whatsapp/whatsapp_gate.dart';

/// Sending from a chat: check who would get it (and who is skipped and why), ask once, then start sending.
/// Returns the new broadcast, or null when nothing was sent. Same steps and words as the website's send-flow.ts.
/// Nothing is sent while WhatsApp is not connected (the user is offered to link it).
Future<BroadcastSummary?> confirmAndSend(BuildContext context, WidgetRef ref, BroadcastInput input) async {
  if (!await ensureWhatsAppConnected(context, ref) || !context.mounted) {
    return null;
  }

  final BroadcastsApi api = ref.read(broadcastsApiProvider);
  final BroadcastPreview preview;
  try {
    preview = await api.preview(input);
  } on Object catch (e) {
    if (context.mounted) {
      showErrorToast(context, 'Could not check the message', e);
    }

    return null;
  }

  if (!context.mounted) {
    return null;
  }

  final int skipped = preview.total - preview.willSend;
  if (preview.willSend == 0) {
    showFToast(
      context: context,
      variant: FToastVariant.destructive,
      icon: const Icon(FLucideIcons.circleAlert),
      title: const Text('Nobody can get this message'),
      description: Text(preview.skipped.isEmpty ? 'There is nobody in this chat yet.' : preview.skipped.first.reason),
    );
    return null;
  }

  final PreviewSample? example = preview.samples.isEmpty ? null : preview.samples.first;
  final String body = <String>[
    if (example != null) '${example.contactName} will get: "${example.text}"',
    if (skipped > 0) '$skipped will be skipped (${preview.skipped.isEmpty ? 'missing details' : preview.skipped.first.reason}).',
    if (preview.estimatedDays > 1) 'Sending takes about ${preview.estimatedDays} days.' else 'They go out one by one.',
    'You can pause or stop any time.',
  ].join('\n\n');

  final bool yes = await showConfirmDialog(
    context: context,
    icon: FLucideIcons.send,
    title: 'Send to ${preview.willSend} ${preview.willSend == 1 ? 'person' : 'people'}?',
    body: body,
    confirm: 'Send',
    cancel: 'Not yet',
  );
  if (!yes || !context.mounted) {
    return null;
  }

  try {
    final BroadcastSummary created = await api.create(input);
    if (context.mounted) {
      showInfoToast(context, 'Sending started', description: '${created.counts.pending} will get it one by one.');
    }

    return created;
  } on Object catch (e) {
    if (context.mounted) {
      showErrorToast(context, 'Not sent', e);
    }

    return null;
  }
}
