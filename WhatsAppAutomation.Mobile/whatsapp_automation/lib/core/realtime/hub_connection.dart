import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// One message the server pushed: the event name and its values.
class HubMessage {
  const HubMessage(this.target, this.arguments);

  final String target;
  final List<dynamic> arguments;
}

const String _recordSeparator = '\u001e';

/// SignalR sends several JSON messages in one piece of text, each ended by a separator character.
List<String> splitHubFrames(String data) =>
    data.split(_recordSeparator).where((String frame) => frame.isNotEmpty).toList();

/// Returns the pushed event in this frame, or null for anything else (handshake reply, ping, close).
HubMessage? parseHubFrame(String frame) {
  final Object? json = jsonDecode(frame);
  if (json is! Map<String, dynamic> || json['type'] != 1) {
    return null;
  }

  final Object? target = json['target'];
  final Object? arguments = json['arguments'];
  return target is String ? HubMessage(target, arguments is List ? arguments : const <dynamic>[]) : null;
}

/// A small client for the server's live updates (ASP.NET Core SignalR, JSON over a WebSocket).
/// The server only pushes; this client only listens, answers pings, and reconnects when dropped.
class HubConnection {
  HubConnection({required this.url, required this.accessToken});

  /// The hub's http(s) address, for example http://localhost:5163/hubs/whatsapp.
  final Uri url;

  final Future<String?> Function() accessToken;

  final StreamController<HubMessage> _messages = StreamController<HubMessage>.broadcast();
  WebSocket? _socket;
  Timer? _ping;
  Timer? _retry;
  bool _stopped = false;

  Stream<HubMessage> get messages => _messages.stream;

  bool get isConnected => _socket != null;

  /// Connects now. Throws when the server cannot be reached.
  Future<void> start() async {
    _stopped = false;
    if (_socket != null) {
      return;
    }

    final String? token = await accessToken();
    if (token == null) {
      throw const SocketException('Not signed in.');
    }

    final Uri address = url.replace(
      scheme: url.scheme == 'https' ? 'wss' : 'ws',
      queryParameters: <String, String>{'access_token': token},
    );
    final WebSocket socket =
        await WebSocket.connect(address.toString()).timeout(const Duration(seconds: 15));
    if (_stopped) {
      await socket.close();
      return;
    }

    _socket = socket;
    socket.add('{"protocol":"json","version":1}$_recordSeparator');
    socket.listen(_onData, onDone: _onClosed, onError: (Object _) => _onClosed(), cancelOnError: true);
    // The server closes connections that stay silent, so say "still here" regularly.
    _ping = Timer.periodic(
      const Duration(seconds: 15),
      (_) => _socket?.add('{"type":6}$_recordSeparator'),
    );
  }

  Future<void> stop() async {
    _stopped = true;
    _retry?.cancel();
    _ping?.cancel();
    final WebSocket? socket = _socket;
    _socket = null;
    await socket?.close();
  }

  void _onData(dynamic data) {
    if (data is! String) {
      return;
    }

    for (final String frame in splitHubFrames(data)) {
      try {
        final HubMessage? message = parseHubFrame(frame);
        if (message != null) {
          _messages.add(message);
        }
      } on FormatException {
        // A frame that is not JSON is ignored.
      }
    }
  }

  void _onClosed() {
    _ping?.cancel();
    _socket = null;
    if (_stopped) {
      return;
    }

    _retry?.cancel();
    _retry = Timer(const Duration(seconds: 3), () async {
      try {
        await start();
      } on Exception {
        _onClosed();
      }
    });
  }
}
