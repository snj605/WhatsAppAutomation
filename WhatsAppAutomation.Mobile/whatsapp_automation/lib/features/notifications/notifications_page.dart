import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import 'notifications_api.dart';

/// The user's notifications, newest first. Opening the page marks them read.
class NotificationsPage extends ConsumerStatefulWidget {
  const NotificationsPage({super.key});

  @override
  ConsumerState<NotificationsPage> createState() => _NotificationsPageState();
}

class _NotificationsPageState extends ConsumerState<NotificationsPage> {
  @override
  void initState() {
    super.initState();
    Future<void>(() async {
      final NotificationsController c = ref.read(notificationsProvider.notifier);
      await c.reload();
      try {
        await c.markAllRead();
      } on Object {
        // Marking read can wait for the next visit.
      }
    });
  }

  IconData _icon(AppNotification n) => switch (n.kind) {
        'whatsapp' => FLucideIcons.smartphone,
        'paused' => FLucideIcons.pause,
        'occasion' => FLucideIcons.gift,
        _ => FLucideIcons.bell,
      };

  @override
  Widget build(BuildContext context) {
    final NotificationsState s = ref.watch(notificationsProvider);
    return PageScaffold(
      title: 'Notifications',
      subtitle: 'Your number, sending and occasion wishes',
      child: s.items.isEmpty
          ? const EmptyView(
              icon: FLucideIcons.bell,
              title: 'Nothing yet',
              text: 'You hear here when your number disconnects, sending pauses or occasion wishes go out.',
            )
          : RefreshIndicator(
              onRefresh: () => ref.read(notificationsProvider.notifier).reload(),
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.symmetric(vertical: 8),
                children: <Widget>[
                  CardSection(
                    children: <Widget>[
                      for (final AppNotification n in s.items)
                        ListRow(
                          leading: IconBadge(_icon(n)),
                          title: n.title,
                          subtitle: '${whenText(n.createdAt)}${n.body == null ? '' : ' · ${n.body}'}',
                          badge: n.read ? null : const Pill('New'),
                        ),
                    ],
                  ),
                ],
              ),
            ),
    );
  }
}
