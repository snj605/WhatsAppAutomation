import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Small values kept in the phone's protected storage (Android Keystore, iOS Keychain).
class SecureStore {
  const SecureStore([this._storage = const FlutterSecureStorage()]);

  static const String refreshToken = 'refresh_token';
  static const String deviceId = 'device_id';
  static const String userName = 'user_name';
  static const String biometrics = 'biometrics';
  static const String biometricsAsked = 'biometrics_asked';
  static const String updateCheckedOn = 'update_checked_on';

  final FlutterSecureStorage _storage;

  /// Returns null when the value is missing or can no longer be read (for example after the phone
  /// was restored from a backup): the user then simply signs in again.
  Future<String?> read(String key) async {
    try {
      return await _storage.read(key: key);
    } on Exception {
      return null;
    }
  }

  Future<void> write(String key, String value) => _storage.write(key: key, value: value);

  Future<void> delete(String key) async {
    try {
      await _storage.delete(key: key);
    } on Exception {
      // Nothing to delete.
    }
  }
}
