import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

/// Modern teal & white: soft grey-white background, white cards, one teal accent (#0E8F6E).
/// Dark mode: near-black with a lighter teal. Touch variants, because the app runs only on phones.
/// Light or dark follows the phone's setting (see app.dart).
abstract final class AppTheme {
  static const Color teal = Color(0xFF0E8F6E);
  static const Color tealLight = Color(0xFF2BB38E);

  static final FColors _lightColors = FColors.neutralLight.copyWith(
    background: const Color(0xFFF4F7F6),
    foreground: const Color(0xFF10201B),
    primary: teal,
    primaryForeground: const Color(0xFFFFFFFF),
    secondary: const Color(0xFFE3F3EE),
    secondaryForeground: const Color(0xFF0B5E49),
    muted: const Color(0xFFEDF2F0),
    mutedForeground: const Color(0xFF5E6F69),
    card: const Color(0xFFFFFFFF),
    border: const Color(0xFFE0E8E4),
  );

  static final FColors _darkColors = FColors.neutralDark.copyWith(
    background: const Color(0xFF0B1311),
    foreground: const Color(0xFFE7F0EC),
    primary: tealLight,
    primaryForeground: const Color(0xFF03241A),
    secondary: const Color(0xFF15302A),
    secondaryForeground: const Color(0xFFBFEBDD),
    muted: const Color(0xFF16211E),
    mutedForeground: const Color(0xFF8FA39C),
    card: const Color(0xFF111D1A),
    border: const Color(0xFF223330),
  );

  static final FThemeData light = FThemeData(touch: true, debugLabel: 'Teal Light Touch', colors: _lightColors);
  static final FThemeData dark = FThemeData(touch: true, debugLabel: 'Teal Dark Touch', colors: _darkColors);

  /// For the few Material pieces still in use (routes, pull to refresh, text selection), so they match.
  static final ThemeData materialLight = light.toApproximateMaterialTheme();
  static final ThemeData materialDark = dark.toApproximateMaterialTheme();
}

/// The soft background behind chat messages.
Color chatWallpaper(BuildContext context) =>
    context.theme.colors.brightness == Brightness.dark ? const Color(0xFF0E1A17) : const Color(0xFFE9F1EE);
