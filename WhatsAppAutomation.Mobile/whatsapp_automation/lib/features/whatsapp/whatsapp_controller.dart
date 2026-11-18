import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/config.dart';
import '../../core/providers.dart';
import '../../core/realtime/hub_connection.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import 'whatsapp_models.dart';

final NotifierProvider<WhatsAppController, WhatsAppState> whatsAppProvider =
    NotifierProvider<WhatsAppController, WhatsAppState>(WhatsAppController.new);

/// The user's WhatsApp link: status, linking with a code, unlinking.
class WhatsAppController extends Notifier<WhatsAppState> {
  HubConnection? _hub;
  StreamSubscription<HubMessage>? _events;
  Timer? _poll;

  ApiClient get _api => ref.read(apiClientProvider);

  @override
  WhatsAppState build() {
    ref.onDispose(_close);
    if (ref.watch(sessionProvider) is! SignedIn) {
      return const WhatsAppState();
    }

    Future<void>.microtask(refresh);
    return const WhatsAppState(loading: true);
  }

  Future<void> refresh() async {
    try {
      final Object? json = await _api.get('/api/v1/whatsapp');
      if (ref.mounted) {
        _apply(WhatsAppAccount.fromJson(json! as Map<String, dynamic>));
      }
    } on ApiException catch (error) {
      if (ref.mounted) {
        state = state.copyWith(loading: false, error: error.message);
      }
    }
  }

  /// Starts linking with a code. The code arrives a moment later over the live connection.
  Future<void> linkWithCode(String phoneNumber) => _link('code', phoneNumber);

  /// Starts linking with a QR, for when WhatsApp is on another phone or tablet: the QR shows here and the other
  /// device scans it. New QRs arrive over the live connection about every 20 seconds.
  Future<void> linkWithQr() => _link('qr', null);

  /// Back to choosing how to link (the linking on the server simply runs out).
  void chooseAgain() => state = state.copyWith(method: null, qrImage: null, pairingCode: null);

  Future<void> _link(String method, String? phoneNumber) async {
    state = state.copyWith(busy: true, error: null, pairingCode: null, qrImage: null, method: method);
    try {
      // Listen first: the code (or the first QR) is pushed only once.
      await _listen();
      final Object? json = await _api.post('/api/v1/whatsapp/link', <String, Object?>{
        'method': method,
        'phoneNumber': phoneNumber,
      });
      if (ref.mounted) {
        _apply(WhatsAppAccount.fromJson(json! as Map<String, dynamic>));
      }
    } on ApiException catch (error) {
      _fail(error.message);
    } on Exception {
      _fail(networkError.message);
    } finally {
      if (ref.mounted) {
        state = state.copyWith(busy: false);
      }
    }
  }

  /// Connects again with the saved login, without a new QR.
  Future<void> reconnect() async {
    state = state.copyWith(busy: true, error: null);
    try {
      final Object? json = await _api.post('/api/v1/whatsapp/reconnect');
      if (ref.mounted && json is Map<String, dynamic>) {
        _apply(WhatsAppAccount.fromJson(json));
      }
    } on ApiException catch (error) {
      _fail(error.message);
    } finally {
      if (ref.mounted) {
        state = state.copyWith(busy: false);
      }
    }
  }

  Future<void> unlink() async {
    state = state.copyWith(busy: true, error: null);
    try {
      final Object? json = await _api.post('/api/v1/whatsapp/unlink');
      if (ref.mounted) {
        _apply(json is Map<String, dynamic> ? WhatsAppAccount.fromJson(json) : const WhatsAppAccount(status: 'not_linked'));
      }
    } on ApiException catch (error) {
      _fail(error.message);
    } finally {
      if (ref.mounted) {
        state = state.copyWith(busy: false);
      }
    }
  }

  Future<void> _listen() async {
    HubConnection? hub = _hub;
    if (hub == null) {
      hub = HubConnection(
        url: Uri.parse('${AppConfig.apiBaseUrl}/hubs/whatsapp'),
        accessToken: ref.read(authServiceProvider).accessToken,
      );
      _hub = hub;
      _events = hub.messages.listen(_onMessage);
    }

    await hub.start();
  }

  void _onMessage(HubMessage message) {
    if (!ref.mounted || message.arguments.isEmpty) {
      return;
    }

    final Object? value = message.arguments.first;
    if (value is! Map<String, dynamic>) {
      return;
    }

    if (message.target == 'pairingCode') {
      state = state.copyWith(pairingCode: value['code'] as String?);
    } else if (message.target == 'qr' && state.method == 'qr') {
      final Uint8List? png = qrPng(value['image']);
      if (png != null) {
        state = state.copyWith(qrImage: png);
      }
    } else if (message.target == 'account') {
      _apply(WhatsAppAccount.fromJson(value));
    }
  }

  void _apply(WhatsAppAccount account) {
    state = state.copyWith(
      account: account,
      loading: false,
      error: null,
      pairingCode: account.status == 'linking' ? state.pairingCode : null,
      qrImage: account.status == 'linking' ? state.qrImage : null,
      method: account.status == 'linking' || account.status == 'not_linked' || account.status == 'logged_out' ? state.method : null,
    );

    // While the status is still changing, also ask the server now and then: a live update can be
    // missed when the app was in the background.
    if (account.isSettling) {
      _poll ??= Timer.periodic(const Duration(seconds: 4), (_) => refresh());
    } else {
      _poll?.cancel();
      _poll = null;
    }
  }

  void _fail(String message) {
    if (ref.mounted) {
      state = state.copyWith(error: message);
    }
  }

  void _close() {
    _poll?.cancel();
    _poll = null;
    unawaited(_events?.cancel());
    _events = null;
    unawaited(_hub?.stop());
    _hub = null;
  }
}

/// The PNG bytes of a "data:image/png;base64,…" QR picture, or null when it is not one.
Uint8List? qrPng(Object? image) {
  const String prefix = 'data:image/png;base64,';
  if (image is! String || !image.startsWith(prefix) || image.length > 30000) {
    return null;
  }

  try {
    return base64Decode(image.substring(prefix.length));
  } on FormatException {
    return null;
  }
}
