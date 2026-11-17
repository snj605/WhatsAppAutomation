import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import 'api/api_error.dart';
import 'format.dart';

/// The message to show for any error from a call to the API.
String errorText(Object error) => error is ApiException ? error.message : 'Something went wrong. Try again.';

void showErrorToast(BuildContext context, String title, Object error) {
  showFToast(
    context: context,
    variant: FToastVariant.destructive,
    icon: const Icon(FLucideIcons.circleAlert),
    title: Text(title),
    description: Text(errorText(error)),
  );
}

void showInfoToast(BuildContext context, String title, {String? description}) {
  showFToast(
    context: context,
    icon: Icon(FLucideIcons.circleCheck, color: context.theme.colors.primary),
    title: Text(title),
    description: description == null ? null : Text(description),
  );
}

/// Opens a screen on top of the current one.
Future<T?> pushPage<T>(BuildContext context, Widget page) =>
    Navigator.of(context).push<T>(MaterialPageRoute<T>(builder: (_) => page));

/// A soft shadow under cards, the same everywhere.
List<BoxShadow> cardShadow(BuildContext context) => context.theme.colors.brightness == Brightness.dark
    ? const <BoxShadow>[]
    : const <BoxShadow>[BoxShadow(color: Color(0x140B3D2E), blurRadius: 14, offset: Offset(0, 4))];

/// A screen with a back button and a title, used by every screen opened from a tab.
class PageScaffold extends StatelessWidget {
  const PageScaffold({
    super.key,
    required this.title,
    required this.child,
    this.actions = const <Widget>[],
    this.footer,
    this.subtitle,
    this.leading,
    this.onTitleTap,
  });

  final String title;
  final String? subtitle;
  final Widget child;
  final List<Widget> actions;
  final Widget? footer;

  /// Shown before the title (for example the chat's avatar).
  final Widget? leading;
  final VoidCallback? onTitleTap;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final Widget text = subtitle == null
        ? Text(title, maxLines: 1, overflow: TextOverflow.ellipsis)
        : Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Text(title, maxLines: 1, overflow: TextOverflow.ellipsis),
              const SizedBox(height: 2),
              Text(
                subtitle!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground, fontWeight: FontWeight.w400),
              ),
            ],
          );
    final Widget titleRow = leading == null
        ? text
        : Row(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[leading!, const SizedBox(width: 10), Flexible(child: text)],
          );
    return FScaffold(
      childPad: false,
      header: FHeader.nested(
        titleAlignment: AlignmentDirectional.centerStart,
        title: onTitleTap == null ? titleRow : GestureDetector(onTap: onTitleTap, child: titleRow),
        prefixes: <Widget>[FHeaderAction.back(onPress: () => Navigator.of(context).maybePop())],
        suffixes: actions,
      ),
      footer: footer,
      child: SafeArea(top: false, bottom: footer == null, child: child),
    );
  }
}

/// A round avatar with initials, coloured by name.
class PersonAvatar extends StatelessWidget {
  const PersonAvatar({super.key, required this.name, this.size = 40, this.icon});

  final String name;
  final double size;

  /// Shown instead of initials (for example a cake for the Birthdays chat).
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final ({Color background, Color foreground}) colors = avatarColors(name);
    final bool dark = context.theme.colors.brightness == Brightness.dark;
    final Color bg = dark ? colors.foreground : colors.background;
    final Color fg = dark ? colors.background : colors.foreground;
    return FAvatar.raw(
      size: size,
      style: .delta(backgroundColor: bg),
      child: icon != null
          ? Icon(icon, size: size * 0.48, color: fg)
          : Text(initialsOf(name), style: TextStyle(color: fg, fontSize: size * 0.36, fontWeight: FontWeight.w700)),
    );
  }
}

/// An icon in a soft tinted circle, used for settings rows and empty screens.
class IconBadge extends StatelessWidget {
  const IconBadge(this.icon, {super.key, this.size = 36, this.color});

  final IconData icon;
  final double size;

  /// The icon colour; the circle is a light tint of it. Defaults to the teal accent.
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final Color c = color ?? context.theme.colors.primary;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(color: c.withValues(alpha: 0.12), shape: BoxShape.circle),
      child: Icon(icon, size: size * 0.5, color: c),
    );
  }
}

/// A small coloured label, for example "Paused" or "No broadcasts".
class Pill extends StatelessWidget {
  const Pill(this.text, {super.key, this.color});

  final String text;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final Color c = color ?? context.theme.colors.primary;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(color: c.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(999)),
      child: Text(text, style: context.theme.typography.body.xs.copyWith(color: c, fontWeight: FontWeight.w600)),
    );
  }
}

class LoadingView extends StatelessWidget {
  const LoadingView({super.key});

  @override
  Widget build(BuildContext context) =>
      const Center(child: Padding(padding: EdgeInsets.all(32), child: FCircularProgress.loader(semanticsLabel: 'Loading')));
}

/// A calm message in the middle of an empty list, with an optional button.
class EmptyView extends StatelessWidget {
  const EmptyView({super.key, required this.icon, required this.text, this.action, this.actionLabel, this.title});

  final IconData icon;
  final String? title;
  final String text;
  final VoidCallback? action;
  final String? actionLabel;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 40),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            IconBadge(icon, size: 72),
            const SizedBox(height: 16),
            if (title != null) ...<Widget>[
              Text(
                title!,
                textAlign: TextAlign.center,
                style: theme.typography.display.lg.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 6),
            ],
            Text(
              text,
              textAlign: TextAlign.center,
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            ),
            if (action != null && actionLabel != null) ...<Widget>[
              const SizedBox(height: 20),
              FButton(mainAxisSize: MainAxisSize.min, onPress: action, child: Text(actionLabel!)),
            ],
          ],
        ),
      ),
    );
  }
}

/// A white rounded card holding rows, with thin lines between them (like iOS / WhatsApp settings).
class CardSection extends StatelessWidget {
  const CardSection({
    super.key,
    required this.children,
    this.title,
    this.trailing,
    this.margin = const EdgeInsets.fromLTRB(16, 8, 16, 8),
    this.padding = EdgeInsets.zero,
  });

  final List<Widget> children;
  final String? title;

  /// Shown on the right of the title (for example an "Add" button).
  final Widget? trailing;
  final EdgeInsetsGeometry margin;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final List<Widget> rows = <Widget>[];
    for (int i = 0; i < children.length; i++) {
      if (i > 0) {
        rows.add(Padding(
          padding: const EdgeInsets.only(left: 16),
          child: Container(height: 0.6, color: theme.colors.border),
        ));
      }

      rows.add(children[i]);
    }

    return Padding(
      padding: margin,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          if (title != null || trailing != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(4, 4, 4, 8),
              child: Row(
                children: <Widget>[
                  Expanded(child: SectionLabel(title ?? '')),
                  ?trailing,
                ],
              ),
            ),
          if (rows.isNotEmpty)
            Container(
              padding: padding,
              decoration: BoxDecoration(
                color: theme.colors.card,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: theme.colors.border, width: 0.6),
                boxShadow: cardShadow(context),
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(16),
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: rows),
              ),
            ),
        ],
      ),
    );
  }
}

/// One tappable row of a list, like a WhatsApp chat row: avatar, title, subtitle, and something on the right.
/// Put rows inside a [CardSection].
class ListRow extends StatelessWidget {
  const ListRow({
    super.key,
    required this.leading,
    required this.title,
    this.subtitle,
    this.trailing,
    this.onTap,
    this.onLongPress,
    this.dimmed = false,
    this.badge,
  });

  final Widget leading;
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;
  final bool dimmed;

  /// A small label after the title (for example a [Pill]).
  final Widget? badge;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      onLongPress: onLongPress,
      child: Opacity(
        opacity: dimmed ? 0.55 : 1,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          child: Row(
            children: <Widget>[
              leading,
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Row(
                      children: <Widget>[
                        Flexible(
                          child: Text(
                            title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600),
                          ),
                        ),
                        if (badge != null) ...<Widget>[const SizedBox(width: 6), badge!],
                      ],
                    ),
                    if (subtitle != null && subtitle!.isNotEmpty) ...<Widget>[
                      const SizedBox(height: 3),
                      Text(
                        subtitle!,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                      ),
                    ],
                  ],
                ),
              ),
              if (trailing != null) ...<Widget>[const SizedBox(width: 8), trailing!],
            ],
          ),
        ),
      ),
    );
  }
}

/// A row of pill buttons where one is chosen (like a segmented control).
class ChoiceChips<T> extends StatelessWidget {
  const ChoiceChips({super.key, required this.options, required this.selected, required this.onChange});

  final List<(T, String)> options;
  final T? selected;
  final ValueChanged<T>? onChange;

  @override
  Widget build(BuildContext context) => Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          for (final (T value, String label) in options)
            _Chip(
              label: label,
              selected: value == selected,
              onTap: onChange == null ? null : () => onChange!(value),
            ),
        ],
      );
}

/// A row of pill buttons where several can be chosen.
class MultiChoiceChips extends StatelessWidget {
  const MultiChoiceChips({super.key, required this.options, required this.selected, required this.onToggle});

  final List<(String, String)> options;
  final Set<String> selected;
  final ValueChanged<String>? onToggle;

  @override
  Widget build(BuildContext context) => Wrap(
        spacing: 8,
        runSpacing: 8,
        children: <Widget>[
          for (final (String id, String label) in options)
            _Chip(
              label: label,
              selected: selected.contains(id),
              check: true,
              onTap: onToggle == null ? null : () => onToggle!(id),
            ),
        ],
      );
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.selected, required this.onTap, this.check = false});

  final String label;
  final bool selected;
  final bool check;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final FColors colors = context.theme.colors;
    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 150),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          color: selected ? colors.primary : colors.card,
          borderRadius: BorderRadius.circular(999),
          border: Border.all(color: selected ? colors.primary : colors.border),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            if (check && selected) ...<Widget>[
              Icon(FLucideIcons.check, size: 14, color: colors.primaryForeground),
              const SizedBox(width: 4),
            ],
            Text(
              label,
              style: context.theme.typography.body.sm.copyWith(
                color: selected ? colors.primaryForeground : colors.foreground,
                fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A small heading above a part of a screen.
class SectionLabel extends StatelessWidget {
  const SectionLabel(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Padding(
      padding: const EdgeInsets.only(top: 4, bottom: 4),
      child: Text(
        text.toUpperCase(),
        style: theme.typography.body.xs.copyWith(
          color: theme.colors.mutedForeground,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.8,
        ),
      ),
    );
  }
}
