import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/memory_cache.dart';
import '../../core/providers.dart';
import '../../core/storage/secure_store.dart';
import 'auth_models.dart';
import 'auth_service.dart';
import 'biometric_service.dart';

final NotifierProvider<SessionController, SessionState> sessionProvider =
    NotifierProvider<SessionController, SessionState>(SessionController.new);

/// Decides which screen the app shows: sign-in, unlock, or the app itself.
class SessionController extends Notifier<SessionState> {
  AuthService get _auth => ref.read(authServiceProvider);

  ApiClient get _api => ref.read(apiClientProvider);

  SecureStore get _store => ref.read(secureStoreProvider);

  BiometricService get _biometrics => ref.read(biometricServiceProvider);

  @override
  SessionState build() {
    _auth.onSessionLost = () => state = const SignedOut(message: 'Please sign in again.');
    Future<void>.microtask(start);
    return const SessionStarting();
  }

  /// Runs when the app opens (and on "Try again").
  Future<void> start() async {
    state = const SessionStarting();
    if (!await _auth.hasSavedSession()) {
      state = const SignedOut();
      return;
    }

    if (await isBiometricsOn()) {
      state = Locked(await _auth.savedUserName() ?? '');
      return;
    }

    await _resume();
  }

  /// Throws [ApiException] with a message for the sign-in screen.
  Future<void> signIn(String userName, String password) async {
    await _auth.signIn(userName.trim(), password);
    state = await _loadMe();
  }

  /// Asks for fingerprint/face and opens the app when the owner is confirmed.
  Future<bool> unlock() async {
    if (!await _biometrics.confirmOwner('Unlock the app')) {
      return false;
    }

    await _resume();
    return true;
  }

  /// Locks the app (after a long time in the background) when unlocking by fingerprint/face is on.
  Future<void> lock() async {
    final SessionState current = state;
    if (current is SignedIn && await isBiometricsOn()) {
      state = Locked(current.user.userName);
    }
  }

  Future<void> signOut() async {
    await _auth.signOut();
    // Nothing of this user's data stays on screen for the next person.
    MemoryCache.clear();
    state = const SignedOut();
  }

  Future<bool> isBiometricsOn() async => await _store.read(SecureStore.biometrics) == '1';

  /// Turning it on needs one successful fingerprint/face check, so nobody else can switch it on.
  Future<bool> setBiometrics({required bool on}) async {
    if (!on) {
      await _store.delete(SecureStore.biometrics);
      return true;
    }

    if (!await _biometrics.confirmOwner('Confirm to turn on fingerprint or face unlock')) {
      return false;
    }

    await _store.write(SecureStore.biometrics, '1');
    return true;
  }

  Future<void> _resume() async {
    try {
      state = await _auth.refresh()
          ? await _loadMe()
          : const SignedOut(message: 'Please sign in again.');
    } on ApiException catch (error) {
      state = error.status == 401
          ? const SignedOut(message: 'Please sign in again.')
          : SessionUnreachable(error.message);
    }
  }

  Future<SignedIn> _loadMe() async {
    final Map<String, dynamic> me = (await _api.get('/api/v1/auth/me'))! as Map<String, dynamic>;
    return SignedIn(
      user: AppUser.fromJson(me['user'] as Map<String, dynamic>),
      permissions: (me['permissions'] as List<dynamic>? ?? const <dynamic>[]).cast<String>(),
    );
  }
}
