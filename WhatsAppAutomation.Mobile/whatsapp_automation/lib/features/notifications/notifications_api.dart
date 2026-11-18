import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../../core/realtime/live_updates.dart';
import '../auth/session_controller.dart';

/// One notification (GET /api/v1/notifications). Same as the website.
class AppNotification {
  const AppNotification({required this.id, required this.kind, required this.title, required this.body, required this.createdAt, required this.read});

  factory AppNotification.fromJson(Json json) => AppNotification(
        id: json['id'] as String,
        kind: json['kind'] as String? ?? '',
        title: json['title'] as String? ?? '',
        body: asStringOrNull(json['body']),
        createdAt: asDateOrNull(json['createdAt']) ?? DateTime.now(),
        read: asBool(json['read']),
      );

  final String id;

  /// "whatsapp" (number needs a look), "paused" (sending stopped) or "occasion" (wishes started).
  final String kind;
  final String title;
  final String? body;
  final DateTime createdAt;
  final bool read;

  AppNotification markedRead() => AppNotification(id: id, kind: kind, title: title, body: body, createdAt: createdAt, read: true);
}

class NotificationsState {
  const NotificationsState({this.items = const <AppNotification>[], this.unread = 0});

  final List<AppNotification> items;
  final int unread;
}

/// The bell: the user's notifications and the unread count. New ones arrive live.
final NotifierProvider<NotificationsController, NotificationsState> notificationsProvider =
    NotifierProvider<NotificationsController, NotificationsState>(NotificationsController.new);

class NotificationsController extends Notifier<NotificationsState> {
  ApiClient get _api => ref.read(apiClientProvider);

  @override
  NotificationsState build() {
    ref.watch(sessionProvider);
    final StreamSubscription<Json> sub = ref.watch(liveUpdatesProvider).notificationReceived.listen((Json json) {
      final AppNotification n = AppNotification.fromJson(json);
      state = NotificationsState(items: <AppNotification>[n, ...state.items].take(50).toList(), unread: state.unread + (n.read ? 0 : 1));
    });
    ref.onDispose(sub.cancel);
    Future<void>.microtask(reload);
    return const NotificationsState();
  }

  Future<void> reload() async {
    try {
      final Json json = (await _api.get('/api/v1/notifications'))! as Json;
      if (ref.mounted) {
        state = NotificationsState(items: asList(json['items']).map(AppNotification.fromJson).toList(), unread: asInt(json['unread']));
      }
    } on Object {
      // The bell keeps what it had.
    }
  }

  Future<void> markAllRead() async {
    if (state.unread == 0) {
      return;
    }

    await _api.post('/api/v1/notifications/read', <String, dynamic>{'ids': null});
    if (ref.mounted) {
      state = NotificationsState(items: state.items.map((AppNotification n) => n.markedRead()).toList());
    }
  }
}
