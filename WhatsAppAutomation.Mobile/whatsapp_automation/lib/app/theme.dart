import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

/// The approved look (design canvas), the same as the website: light grey page (#F7F8FA), white cards with a 1px
/// #EAECF0 border, deep teal (#0F766E) only for the main action, dark text. Dark mode: the same on dark grey.
/// Touch variants, because the app runs on phones and tablets. Light or dark: the user's choice under Me.
abstract final class AppTheme {
  static const Color teal = Color(0xFF0F766E);
  static const Color tealLight = Color(0xFF2DD4B6);

  static final FColors _lightColors = FColors.neutralLight.copyWith(
    background: const Color(0xFFF7F8FA),
    foreground: const Color(0xFF101828),
    primary: teal,
    primaryForeground: const Color(0xFFFFFFFF),
    secondary: const Color(0xFFF0FDF9),
    secondaryForeground: const Color(0xFF115E59),
    muted: const Color(0xFFF2F4F7),
    mutedForeground: const Color(0xFF667085),
    card: const Color(0xFFFFFFFF),
    border: const Color(0xFFEAECF0),
  );

  static final FColors _darkColors = FColors.neutralDark.copyWith(
    background: const Color(0xFF0C111D),
    foreground: const Color(0xFFF5F5F6),
    primary: tealLight,
    primaryForeground: const Color(0xFF042F2E),
    secondary: const Color(0xFF0F2F2B),
    secondaryForeground: const Color(0xFF99F6DF),
    muted: const Color(0xFF1F242F),
    mutedForeground: const Color(0xFF94969C),
    card: const Color(0xFF161B26),
    border: const Color(0xFF2A303C),
  );

  /// Status dots, the same as the website.
  static const Color good = Color(0xFF17B26A);
  static const Color warn = Color(0xFFF79009);
  static const Color bad = Color(0xFFD92D20);
  static const Color info = Color(0xFF2E90FA);
  static const Color off = Color(0xFF98A2B3);

  static final FThemeData light = FThemeData(touch: true, debugLabel: 'Approved Light Touch', colors: _lightColors);
  static final FThemeData dark = FThemeData(touch: true, debugLabel: 'Approved Dark Touch', colors: _darkColors);

  /// For the few Material pieces still in use (routes, pull to refresh, text selection), so they match.
  static final ThemeData materialLight = light.toApproximateMaterialTheme();
  static final ThemeData materialDark = dark.toApproximateMaterialTheme();
}

/// The soft background behind chat messages.
Color chatWallpaper(BuildContext context) =>
    context.theme.colors.brightness == Brightness.dark ? const Color(0xFF0E1A17) : const Color(0xFFEFF2F5);
