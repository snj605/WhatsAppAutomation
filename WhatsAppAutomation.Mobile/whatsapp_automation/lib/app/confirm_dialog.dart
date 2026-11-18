import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

/// A yes / no question in a Forui dialog. Returns true only when the user presses [confirm].
///
/// Layout follows Forui's vertical dialog template: title, body, then full-width buttons stacked,
/// which suits phones and large touch targets.
Future<bool> showConfirmDialog({
  required BuildContext context,
  required String title,
  required String body,
  required String confirm,
  String cancel = 'Cancel',
  bool destructive = false,
  IconData? icon,
}) async {
  final bool? yes = await showFDialog<bool>(
    context: context,
    builder: (BuildContext context, style, Animation<double> animation) => FDialog(
      style: style,
      animation: animation,
      builder: (BuildContext context, style) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            if (icon != null)
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
                child: Icon(icon, size: 40),
              ),
            Padding(
              padding: const EdgeInsets.only(left: 8, right: 8, bottom: 9),
              child: DefaultTextStyle.merge(style: style.titleTextStyle, child: Text(title)),
            ),
            Flexible(
              child: Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 20),
                child: DefaultTextStyle.merge(style: style.bodyTextStyle, child: Text(body)),
              ),
            ),
            Column(
              mainAxisSize: MainAxisSize.min,
              spacing: 10,
              children: <Widget>[
                FButton(
                  variant: destructive ? FButtonVariant.destructive : FButtonVariant.primary,
                  onPress: () => Navigator.of(context).pop(true),
                  child: Text(confirm),
                ),
                FButton(
                  variant: FButtonVariant.outline,
                  onPress: () => Navigator.of(context).pop(false),
                  child: Text(cancel),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
  return yes ?? false;
}

/// One button of [showChoiceDialog].
typedef DialogChoice<T> = ({T value, String label, FButtonVariant variant});

/// A question with several answers, as full-width buttons stacked in the given order.
/// Returns the chosen value, or null when the dialog was closed without choosing.
Future<T?> showChoiceDialog<T>({
  required BuildContext context,
  required String title,
  required String body,
  required List<DialogChoice<T>> choices,
  IconData? icon,
}) =>
    showFDialog<T>(
      context: context,
      builder: (BuildContext context, style, Animation<double> animation) => FDialog(
        style: style,
        animation: animation,
        builder: (BuildContext context, style) => Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              if (icon != null)
                Padding(
                  padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
                  child: Icon(icon, size: 40),
                ),
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 9),
                child: DefaultTextStyle.merge(style: style.titleTextStyle, child: Text(title)),
              ),
              Flexible(
                child: Padding(
                  padding: const EdgeInsets.only(left: 8, right: 8, bottom: 20),
                  child: DefaultTextStyle.merge(style: style.bodyTextStyle, child: Text(body)),
                ),
              ),
              Column(
                mainAxisSize: MainAxisSize.min,
                spacing: 10,
                children: <Widget>[
                  for (final DialogChoice<T> c in choices)
                    FButton(
                      variant: c.variant,
                      onPress: () => Navigator.of(context).pop(c.value),
                      child: Text(c.label),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );

/// Asks for one line of text (for example a Google Drive link). Returns the text, or null when cancelled.
Future<String?> showTextDialog({
  required BuildContext context,
  required String title,
  required String body,
  required String confirm,
  String? hint,
  String cancel = 'Cancel',
  IconData? icon,
}) async {
  final TextEditingController controller = TextEditingController();
  try {
    return await showFDialog<String>(
      context: context,
      builder: (BuildContext context, style, Animation<double> animation) => FDialog(
        style: style,
        animation: animation,
        builder: (BuildContext context, style) => Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              if (icon != null)
                Padding(
                  padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
                  child: Icon(icon, size: 40),
                ),
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 9),
                child: DefaultTextStyle.merge(style: style.titleTextStyle, child: Text(title)),
              ),
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 12),
                child: DefaultTextStyle.merge(style: style.bodyTextStyle, child: Text(body)),
              ),
              Padding(
                padding: const EdgeInsets.only(left: 8, right: 8, bottom: 20),
                child: FTextField(control: FTextFieldControl.managed(controller: controller), hint: hint, autofocus: true),
              ),
              Column(
                mainAxisSize: MainAxisSize.min,
                spacing: 10,
                children: <Widget>[
                  FButton(
                    onPress: () => Navigator.of(context).pop(controller.text.trim().isEmpty ? null : controller.text.trim()),
                    child: Text(confirm),
                  ),
                  FButton(
                    variant: FButtonVariant.outline,
                    onPress: () => Navigator.of(context).pop(),
                    child: Text(cancel),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  } finally {
    controller.dispose();
  }
}
