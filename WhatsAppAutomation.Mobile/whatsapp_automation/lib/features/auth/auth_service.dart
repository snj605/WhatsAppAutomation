import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:http/http.dart' as http;

import '../../core/api/api_error.dart';
import '../../core/config.dart';
import '../../core/storage/secure_store.dart';
import 'auth_models.dart';

/// Signs in and keeps the session alive. The short-lived access token lives only in memory; the
/// refresh token lives in the phone's protected storage and is replaced on every use.
class AuthService {
  AuthService(this._store, this._http, {this.baseUrl = AppConfig.apiBaseUrl});

  static const Duration _timeout = Duration(seconds: 20);

  final SecureStore _store;
  final http.Client _http;
  final String baseUrl;

  String? _accessToken;
  DateTime? _expiresAt;
  Future<bool>? _refreshing;

  /// Called when the server no longer accepts the session (signed out elsewhere, disabled, expired).
  void Function()? onSessionLost;

  Future<bool> hasSavedSession() async => await _store.read(SecureStore.refreshToken) != null;

  Future<String?> savedUserName() => _store.read(SecureStore.userName);

  Future<AppUser> signIn(String userName, String password) async {
    final http.Response response = await _post('/api/v1/auth/login', <String, Object?>{
      'userName': userName,
      'password': password,
      'deviceId': await _deviceId(),
      'deviceName': Platform.isIOS ? 'iPhone' : 'Android phone',
    });
    if (response.statusCode != 200) {
      throw apiErrorFrom(response.statusCode, response.body);
    }

    return _accept(response.body);
  }

  /// A token that is still good, refreshed first when needed. Null when the session has ended.
  Future<String?> accessToken() async {
    final DateTime? expiresAt = _expiresAt;
    if (_accessToken != null &&
        expiresAt != null &&
        expiresAt.isAfter(DateTime.now().add(const Duration(seconds: 30)))) {
      return _accessToken;
    }

    return await refresh() ? _accessToken : null;
  }

  /// True when a new access token was obtained; false when the server refused the saved session.
  /// Throws [networkError] when the server cannot be reached (the saved session is kept).
  Future<bool> refresh() {
    // Several screens may ask at once: they all wait for the same request, because each
    // refresh token works only once.
    return _refreshing ??= _refreshOnce().whenComplete(() => _refreshing = null);
  }

  Future<void> signOut() async {
    final String? token = await _store.read(SecureStore.refreshToken);
    await _forget();
    if (token != null) {
      try {
        await _post('/api/v1/auth/logout', <String, Object?>{'refreshToken': token});
      } on ApiException {
        // Offline: the session is gone from this phone, and it expires on the server by itself.
      }
    }
  }

  Future<bool> _refreshOnce() async {
    final String? token = await _store.read(SecureStore.refreshToken);
    if (token == null) {
      return false;
    }

    final http.Response response = await _post('/api/v1/auth/refresh', <String, Object?>{
      'refreshToken': token,
      'deviceId': await _deviceId(),
    });
    if (response.statusCode == 200) {
      await _accept(response.body);
      return true;
    }

    if (response.statusCode == 400 || response.statusCode == 401 || response.statusCode == 403) {
      await _forget();
      onSessionLost?.call();
      return false;
    }

    throw apiErrorFrom(response.statusCode, response.body);
  }

  Future<AppUser> _accept(String body) async {
    final Map<String, dynamic> json = jsonDecode(body) as Map<String, dynamic>;
    final AppUser user = AppUser.fromJson(json['user'] as Map<String, dynamic>);
    _accessToken = json['accessToken'] as String;
    _expiresAt = DateTime.parse(json['accessTokenExpiresAt'] as String);
    await _store.write(SecureStore.refreshToken, json['refreshToken'] as String);
    await _store.write(SecureStore.userName, user.userName);
    return user;
  }

  Future<void> _forget() async {
    _accessToken = null;
    _expiresAt = null;
    await _store.delete(SecureStore.refreshToken);
  }

  /// A random id made once per install. The server ties each session to it.
  Future<String> _deviceId() async {
    final String? saved = await _store.read(SecureStore.deviceId);
    if (saved != null) {
      return saved;
    }

    final Random random = Random.secure();
    final String id = List<String>.generate(
      32,
      (_) => random.nextInt(16).toRadixString(16),
    ).join();
    await _store.write(SecureStore.deviceId, id);
    return id;
  }

  Future<http.Response> _post(String path, Map<String, Object?> body) async {
    try {
      return await _http
          .post(
            Uri.parse('$baseUrl$path'),
            headers: const <String, String>{'Content-Type': 'application/json'},
            body: jsonEncode(body),
          )
          .timeout(_timeout);
    } on SocketException {
      throw networkError;
    } on TimeoutException {
      throw networkError;
    } on http.ClientException {
      throw networkError;
    }
  }
}
