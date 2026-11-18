import 'package:material_ui/material_ui.dart';

import '../../app/theme.dart';

/// One message's state as a short text and a dot colour, the same words as the website.
(String, Color) statusLook(String? status) => switch (status) {
      'read' => ('Read', AppTheme.good),
      'delivered' => ('Delivered', AppTheme.good),
      'sent' => ('Sent', AppTheme.info),
      'pending' => ('Waiting', AppTheme.warn),
      'failed' => ('Failed', AppTheme.bad),
      'skipped' => ('Skipped', AppTheme.off),
      _ => ('Not sent', AppTheme.off),
    };

/// A small dot followed by a word: how every state is shown in the approved look.
class StatusDot extends StatelessWidget {
  const StatusDot(this.text, {super.key, required this.color, this.style});

  final String text;
  final Color color;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Container(width: 6, height: 6, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 6),
          Text(text, style: style, maxLines: 1),
        ],
      );
}
