import 'package:local_auth/local_auth.dart';

/// Fingerprint or face, as registered on the phone. The fingerprint never leaves the phone:
/// the phone only tells the app "yes, this is the owner" or "no".
class BiometricService {
  BiometricService([LocalAuthentication? auth]) : _auth = auth ?? LocalAuthentication();

  final LocalAuthentication _auth;

  /// True when the phone has a fingerprint or face registered.
  Future<bool> isAvailable() async {
    try {
      if (!await _auth.isDeviceSupported()) {
        return false;
      }

      return (await _auth.getAvailableBiometrics()).isNotEmpty;
    } on Exception {
      return false;
    }
  }

  /// Asks for the fingerprint or face. The phone's own PIN or pattern is accepted as a fallback.
  Future<bool> confirmOwner(String reason) async {
    try {
      return await _auth.authenticate(localizedReason: reason, persistAcrossBackgrounding: true);
    } on Exception {
      return false;
    }
  }
}
