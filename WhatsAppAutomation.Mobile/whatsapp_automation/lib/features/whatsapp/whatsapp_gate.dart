import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import 'link_whatsapp_page.dart';
import 'whatsapp_controller.dart';
import 'whatsapp_models.dart';

/// Earlier messages that still have people waiting: sending (waiting for WhatsApp) or paused.
Future<List<BroadcastSummary>> waitingBroadcasts(WidgetRef ref) async {
  final List<BroadcastSummary> all = await ref.read(broadcastsApiProvider).list();
  return all.where((BroadcastSummary b) => b.isLive && b.counts.pending > 0).toList();
}

int _people(List<BroadcastSummary> list) =>
    list.fold<int>(0, (int sum, BroadcastSummary b) => sum + b.counts.pending);

/// Called before anything is sent. Sending needs a connected WhatsApp number: when it is not connected the user
/// cannot send, gets a button to link it, and is asked what to do with messages that are already waiting.
/// Returns true when WhatsApp is connected and sending may go on.
Future<bool> ensureWhatsAppConnected(BuildContext context, WidgetRef ref) async {
  final WhatsAppController controller = ref.read(whatsAppProvider.notifier);
  await controller.refresh();
  WhatsAppAccount? account = ref.read(whatsAppProvider).account;
  if (account?.isConnected ?? false) {
    return true;
  }

  List<BroadcastSummary> waiting = <BroadcastSummary>[];
  try {
    waiting = await waitingBroadcasts(ref);
  } on Object {
    // Still block sending; just without the waiting count.
  }

  if (!context.mounted) {
    return false;
  }

  final int people = _people(waiting);
  final String state = account == null || !account.isLinked
      ? 'Your WhatsApp number is not linked.'
      : 'Your WhatsApp is ${account.label.toLowerCase()}.';
  final String? choice = await showChoiceDialog<String>(
    context: context,
    icon: FLucideIcons.unplug,
    title: 'WhatsApp is not connected',
    body: <String>[
      '$state Messages can be sent only while it is connected.',
      if (waiting.isNotEmpty)
        '${waiting.length} earlier ${waiting.length == 1 ? 'message is' : 'messages are'} waiting for '
            '$people ${people == 1 ? 'person' : 'people'}. You can keep them or stop them.',
    ].join('\n\n'),
    choices: <DialogChoice<String>>[
      (value: 'link', label: account?.isLinked ?? false ? 'Open WhatsApp settings' : 'Link WhatsApp', variant: FButtonVariant.primary),
      if (waiting.isNotEmpty) (value: 'stop', label: 'Stop waiting messages', variant: FButtonVariant.destructive),
      (value: 'cancel', label: 'Not now', variant: FButtonVariant.outline),
    ],
  );
  if (!context.mounted) {
    return false;
  }

  switch (choice) {
    case 'stop':
      await _stopAll(context, ref, waiting);
      return false;
    case 'link':
      await pushPage<void>(context, const LinkWhatsAppPage());
      if (!context.mounted) {
        return false;
      }

      await controller.refresh();
      account = ref.read(whatsAppProvider).account;
      if (!(account?.isConnected ?? false)) {
        return false;
      }

      if (context.mounted) {
        await offerWaitingBroadcasts(context, ref);
      }

      return context.mounted;
    default:
      return false;
  }
}

/// After WhatsApp (re)connects: messages paused while it was not linked are not sent by themselves. Asks whether
/// to resume or stop them. Does nothing when nothing is paused.
Future<void> offerWaitingBroadcasts(BuildContext context, WidgetRef ref) async {
  List<BroadcastSummary> paused;
  try {
    paused = (await waitingBroadcasts(ref)).where((BroadcastSummary b) => b.status == 'paused').toList();
  } on Object {
    return;
  }

  if (paused.isEmpty || !context.mounted) {
    return;
  }

  final int people = _people(paused);
  final String? choice = await showChoiceDialog<String>(
    context: context,
    icon: FLucideIcons.listRestart,
    title: '${paused.length} ${paused.length == 1 ? 'message is' : 'messages are'} paused',
    body: 'WhatsApp is connected again. $people ${people == 1 ? 'person is' : 'people are'} still waiting for '
        '${paused.length == 1 ? '"${paused.first.name}"' : 'these messages'}. Resume sending, or stop them?',
    choices: <DialogChoice<String>>[
      (value: 'resume', label: 'Resume sending', variant: FButtonVariant.primary),
      (value: 'stop', label: 'Stop them', variant: FButtonVariant.destructive),
      (value: 'later', label: 'Decide later', variant: FButtonVariant.outline),
    ],
  );
  if (!context.mounted) {
    return;
  }

  if (choice == 'resume') {
    await _changeAll(context, ref, paused, 'resume', 'Sending resumed');
  } else if (choice == 'stop') {
    await _stopAll(context, ref, paused);
  }
}

Future<void> _stopAll(BuildContext context, WidgetRef ref, List<BroadcastSummary> list) async {
  final bool yes = await showConfirmDialog(
    context: context,
    title: 'Stop ${list.length} ${list.length == 1 ? 'message' : 'messages'}?',
    body: 'People who did not get them yet will not get them. Messages already sent stay sent.',
    confirm: 'Stop',
    destructive: true,
  );
  if (yes && context.mounted) {
    await _changeAll(context, ref, list, 'cancel', 'Stopped');
  }
}

Future<void> _changeAll(BuildContext context, WidgetRef ref, List<BroadcastSummary> list, String action, String done) async {
  final BroadcastsApi api = ref.read(broadcastsApiProvider);
  try {
    for (final BroadcastSummary b in list) {
      await api.change(b.id, action);
    }

    if (context.mounted) {
      showInfoToast(context, done, description: '${list.length} ${list.length == 1 ? 'message' : 'messages'}');
    }
  } on Object catch (e) {
    if (context.mounted) {
      showErrorToast(context, 'Not done', e);
    }
  }
}
