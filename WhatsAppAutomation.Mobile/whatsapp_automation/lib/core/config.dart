/// Build-time settings. Change the server address with
/// `flutter run --dart-define=API_BASE_URL=https://your-server`.
class AppConfig {
  const AppConfig._();

  /// During development the phone reaches the PC's API through `adb reverse tcp:5163 tcp:5163`,
  /// which makes localhost on the phone point to the PC.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5163',
  );

  /// Where new versions of the Android app are published (GitHub Releases of this public repository).
  static const String updateRepository = String.fromEnvironment(
    'UPDATE_REPOSITORY',
    defaultValue: 'snj605/WhatsApp_Automation',
  );

  /// The app locks itself after this long in the background (when unlocking by fingerprint or face is on).
  static const Duration lockAfter = Duration(minutes: 5);
}
