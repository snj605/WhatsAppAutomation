import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http/http.dart' as http;
import 'package:ota_update/ota_update.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../../core/config.dart';
import '../../core/providers.dart';
import '../../core/storage/secure_store.dart';

/// A newer app version published on GitHub Releases.
class AppRelease {
  const AppRelease({
    required this.version,
    required this.notes,
    required this.apkUrl,
    required this.required,
    this.sha256Url,
  });

  final String version;
  final String notes;
  final String apkUrl;
  final String? sha256Url;

  /// "[required]" in the release notes: the app cannot be used until it is updated.
  final bool required;
}

class UpdateState {
  const UpdateState({
    this.currentVersion = '',
    this.release,
    this.checking = false,
    this.progress,
    this.installing = false,
    this.error,
    this.dismissed = false,
  });

  final String currentVersion;

  /// Set when a newer version is available.
  final AppRelease? release;
  final bool checking;

  /// 0–100 while downloading.
  final int? progress;
  final bool installing;
  final String? error;

  /// The user chose "Later" for an optional update (until the next check).
  final bool dismissed;

  bool get busy => progress != null || installing;

  UpdateState copyWith({
    String? currentVersion,
    AppRelease? release,
    bool clearRelease = false,
    bool? checking,
    int? progress,
    bool clearProgress = false,
    bool? installing,
    String? error,
    bool clearError = false,
    bool? dismissed,
  }) =>
      UpdateState(
        currentVersion: currentVersion ?? this.currentVersion,
        release: clearRelease ? null : (release ?? this.release),
        checking: checking ?? this.checking,
        progress: clearProgress ? null : (progress ?? this.progress),
        installing: installing ?? this.installing,
        error: clearError ? null : (error ?? this.error),
        dismissed: dismissed ?? this.dismissed,
      );
}

final NotifierProvider<UpdateController, UpdateState> updateProvider =
    NotifierProvider<UpdateController, UpdateState>(UpdateController.new);

/// In-app updates for the Android app: once a day (and on "Check for updates") it asks GitHub for the newest
/// release; when it is newer, the app offers it, downloads the APK with a progress bar and opens Android's
/// installer. Android always asks the user to confirm the install; the app cannot skip that.
class UpdateController extends Notifier<UpdateState> {
  StreamSubscription<OtaEvent>? _download;

  @override
  UpdateState build() {
    ref.onDispose(() => _download?.cancel());
    Future<void>.microtask(_loadVersion);
    return const UpdateState();
  }

  static bool get supported => Platform.isAndroid;

  Future<void> _loadVersion() async {
    try {
      final PackageInfo info = await PackageInfo.fromPlatform();
      if (ref.mounted) {
        state = state.copyWith(currentVersion: info.version);
      }
    } on Object {
      // The version is only shown on the Me tab.
    }
  }

  /// The automatic check: at most once a day.
  Future<void> checkDaily() async {
    if (!supported) {
      return;
    }

    final SecureStore store = ref.read(secureStoreProvider);
    final String today = DateTime.now().toIso8601String().substring(0, 10);
    if (await store.read(SecureStore.updateCheckedOn) == today) {
      return;
    }

    if (await check(manual: false)) {
      await store.write(SecureStore.updateCheckedOn, today);
    }
  }

  /// Asks GitHub for the newest release. Returns true when the check itself worked.
  Future<bool> check({required bool manual}) async {
    if (!supported || state.checking || state.busy) {
      return false;
    }

    state = state.copyWith(checking: true, clearError: true);
    try {
      final PackageInfo info = await PackageInfo.fromPlatform();
      final http.Response response = await ref
          .read(httpClientProvider)
          .get(
            Uri.parse('https://api.github.com/repos/${AppConfig.updateRepository}/releases/latest'),
            headers: const <String, String>{
              'Accept': 'application/vnd.github+json',
              'User-Agent': 'WhatsAppAutomation-app',
            },
          )
          .timeout(const Duration(seconds: 20));
      if (!ref.mounted) {
        return false;
      }

      if (response.statusCode == 404) {
        // No release published yet.
        state = state.copyWith(currentVersion: info.version, checking: false, clearRelease: true);
        return true;
      }

      if (response.statusCode != 200) {
        throw HttpException('GitHub answered ${response.statusCode}');
      }

      final AppRelease? release = _parse(jsonDecode(response.body) as Map<String, dynamic>);
      final bool newer = release != null && isNewer(release.version, info.version);
      state = state.copyWith(
        currentVersion: info.version,
        checking: false,
        release: newer ? release : null,
        clearRelease: !newer,
        dismissed: false,
      );
      return true;
    } on Object {
      if (ref.mounted) {
        state = state.copyWith(
          checking: false,
          error: manual ? 'Could not check for updates. Check the internet connection and try again.' : null,
        );
      }

      return false;
    }
  }

  void dismiss() => state = state.copyWith(dismissed: true);

  /// Downloads the APK (checked against its SHA-256 when the release has one) and opens the installer.
  Future<void> install() async {
    final AppRelease? release = state.release;
    if (release == null || state.busy) {
      return;
    }

    String? checksum;
    if (release.sha256Url != null) {
      try {
        final http.Response r = await ref
            .read(httpClientProvider)
            .get(Uri.parse(release.sha256Url!), headers: const <String, String>{'User-Agent': 'WhatsAppAutomation-app'})
            .timeout(const Duration(seconds: 20));
        if (r.statusCode == 200) {
          checksum = r.body.trim().split(RegExp(r'\s+')).first.toLowerCase();
        }
      } on Object {
        checksum = null;
      }
    }

    state = state.copyWith(progress: 0, clearError: true);
    await _download?.cancel();
    try {
      _download = OtaUpdate()
          .execute(release.apkUrl, destinationFilename: 'whatsapp-automation.apk', sha256checksum: checksum)
          .listen(_onEvent, onError: (Object _) => _fail('The update could not be downloaded. Try again.'));
    } on Object {
      _fail('The update could not be started. Try again.');
    }
  }

  void _onEvent(OtaEvent event) {
    if (!ref.mounted) {
      return;
    }

    switch (event.status) {
      case OtaStatus.DOWNLOADING:
        state = state.copyWith(progress: int.tryParse(event.value ?? '') ?? state.progress ?? 0);
      case OtaStatus.INSTALLING:
      case OtaStatus.INSTALLATION_DONE:
        // Android's installer is now on screen; the app restarts after the install.
        state = state.copyWith(clearProgress: true, installing: true);
      case OtaStatus.PERMISSION_NOT_GRANTED_ERROR:
        _fail('Allow "Install unknown apps" for WA Automation in the phone settings, then press Update again.');
      case OtaStatus.CHECKSUM_ERROR:
        _fail('The downloaded file was damaged. Press Update to download it again.');
      case OtaStatus.DOWNLOAD_ERROR:
        _fail('The update could not be downloaded. Check the internet connection and try again.');
      case OtaStatus.CANCELED:
        state = state.copyWith(clearProgress: true, installing: false);
      default:
        _fail('The update could not be installed. Try again.');
    }
  }

  void _fail(String message) {
    if (ref.mounted) {
      state = state.copyWith(clearProgress: true, installing: false, error: message);
    }
  }

  static AppRelease? _parse(Map<String, dynamic> json) {
    final String tag = (json['tag_name'] as String? ?? '').trim();
    final String version = tag.startsWith('v') ? tag.substring(1) : tag;
    final List<Map<String, dynamic>> assets =
        (json['assets'] as List<dynamic>? ?? const <dynamic>[]).cast<Map<String, dynamic>>();
    String? url(bool Function(String name) match) =>
        assets.where((Map<String, dynamic> a) => match(a['name'] as String? ?? '')).firstOrNull?['browser_download_url']
            as String?;
    final String? apk = url((String n) => n.toLowerCase().endsWith('.apk'));
    if (version.isEmpty || apk == null) {
      return null;
    }

    final String notes = json['body'] as String? ?? '';
    return AppRelease(
      version: version,
      notes: notes.replaceAll(RegExp(r'\[required\]', caseSensitive: false), '').trim(),
      apkUrl: apk,
      sha256Url: url((String n) => n.toLowerCase().endsWith('.sha256')),
      required: RegExp(r'\[required\]', caseSensitive: false).hasMatch(notes),
    );
  }
}

/// True when version [a] (like 1.2.0) is newer than [b]. Missing parts count as 0; anything after "+" or "-" is ignored.
bool isNewer(String a, String b) {
  List<int> parts(String v) =>
      v.split(RegExp(r'[+-]')).first.split('.').map((String p) => int.tryParse(p) ?? 0).toList();
  final List<int> x = parts(a);
  final List<int> y = parts(b);
  for (int i = 0; i < 3; i++) {
    final int xi = i < x.length ? x[i] : 0;
    final int yi = i < y.length ? y[i] : 0;
    if (xi != yi) {
      return xi > yi;
    }
  }

  return false;
}
