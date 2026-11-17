import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:material_ui/material_ui.dart';

import '../core/providers.dart';

final NotifierProvider<ThemeModeController, ThemeMode> themeModeProvider =
    NotifierProvider<ThemeModeController, ThemeMode>(ThemeModeController.new);

/// Light, dark, or the phone's setting: the user's choice under Me, kept on this phone.
class ThemeModeController extends Notifier<ThemeMode> {
  static const String _key = 'theme_mode';

  @override
  ThemeMode build() {
    Future<void>.microtask(_load);
    return ThemeMode.system;
  }

  Future<void> _load() async {
    final String? saved = await ref.read(secureStoreProvider).read(_key);
    final ThemeMode mode = ThemeMode.values.where((ThemeMode m) => m.name == saved).firstOrNull ?? ThemeMode.system;
    if (ref.mounted && mode != state) {
      state = mode;
    }
  }

  Future<void> set(ThemeMode mode) async {
    state = mode;
    await ref.read(secureStoreProvider).write(_key, mode.name);
  }
}

/// Words shown for each choice.
String themeModeLabel(ThemeMode mode) => switch (mode) {
      ThemeMode.light => 'Light',
      ThemeMode.dark => 'Dark',
      ThemeMode.system => 'Same as phone',
    };

