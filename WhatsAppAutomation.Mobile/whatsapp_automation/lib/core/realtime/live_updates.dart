import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/auth/auth_models.dart';
import '../../features/auth/session_controller.dart';
import '../config.dart';
import '../json.dart';
import '../providers.dart';
import 'hub_connection.dart';

/// Live updates from the server while signed in: the server pushes a "message" event whenever a message is sent,
/// delivered or fails. Screens listen to [LiveUpdates.messageChanged] and reload, so ticks change at once
/// instead of waiting for the next poll. If the live connection drops, the screens' slow polling still works.
final Provider<LiveUpdates> liveUpdatesProvider = Provider<LiveUpdates>((Ref ref) {
  final LiveUpdates live = LiveUpdates();
  if (ref.watch(sessionProvider) is SignedIn) {
    final HubConnection hub = HubConnection(
      url: Uri.parse('${AppConfig.apiBaseUrl}/hubs/whatsapp'),
      accessToken: ref.read(authServiceProvider).accessToken,
    );
    live._attach(hub);
  }

  ref.onDispose(live._close);
  return live;
});

class LiveUpdates {
  final StreamController<void> _changed = StreamController<void>.broadcast();
  final StreamController<void> _contacts = StreamController<void>.broadcast();
  final StreamController<Json> _notifications = StreamController<Json>.broadcast();
  HubConnection? _hub;
  StreamSubscription<HubMessage>? _events;
  Timer? _debounce;

  /// Fires (at most about twice a second) after messages changed on the server.
  Stream<void> get messageChanged => _changed.stream;

  /// Fires after the server checked numbers on WhatsApp (contacts moved to "Not on WhatsApp" or were confirmed).
  Stream<void> get contactsChanged => _contacts.stream;

  /// A new notification (number logged out, sending paused, occasion wishes started), as the server's JSON.
  Stream<Json> get notificationReceived => _notifications.stream;

  void _attach(HubConnection hub) {
    _hub = hub;
    _events = hub.messages.listen((HubMessage m) {
      if (m.target == 'contacts' && !_contacts.isClosed) {
        _contacts.add(null);
      }

      if (m.target == 'notification' && m.arguments.isNotEmpty && m.arguments.first is Map && !_notifications.isClosed) {
        _notifications.add((m.arguments.first as Map<dynamic, dynamic>).cast<String, dynamic>());
      }

      if (m.target == 'message') {
        // Many ticks can arrive at once while a broadcast is sending: reload once for the batch.
        _debounce ??= Timer(const Duration(milliseconds: 500), () {
          _debounce = null;
          if (!_changed.isClosed) {
            _changed.add(null);
          }
        });
      }
    });
    // A failed first connect is fine: the screens keep polling slowly.
    unawaited(hub.start().catchError((Object _) {}));
  }

  void _close() {
    _debounce?.cancel();
    unawaited(_events?.cancel());
    unawaited(_hub?.stop());
    unawaited(_changed.close());
    unawaited(_contacts.close());
    unawaited(_notifications.close());
  }
}
