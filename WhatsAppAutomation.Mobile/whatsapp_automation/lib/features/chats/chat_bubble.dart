import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import 'chat_models.dart';

/// The bubble's own colour: white on the soft wallpaper, deep teal-grey in dark mode.
Color bubbleColor(BuildContext context) => context.theme.colors.card;

IconData fileIcon(String contentType) {
  if (contentType.startsWith('image/')) {
    return FLucideIcons.image;
  }

  if (contentType.startsWith('video/')) {
    return FLucideIcons.video;
  }

  if (contentType.startsWith('audio/')) {
    return FLucideIcons.music;
  }

  if (contentType.contains('sheet') || contentType.contains('excel') || contentType == 'text/csv') {
    return FLucideIcons.sheet;
  }

  return FLucideIcons.fileText;
}

/// Status colour of a message: teal while sending, amber when paused, grey when done or stopped.
Color statusColor(BuildContext context, String status) => switch (status) {
      'sending' => context.theme.colors.primary,
      'paused' => const Color(0xFFD97706),
      'cancelled' => context.theme.colors.destructive,
      _ => context.theme.colors.mutedForeground,
    };

/// One sent message in a chat: the text as written, the file, and how far sending got. Tap to see who got it.
class ChatBubble extends StatelessWidget {
  const ChatBubble({super.key, required this.message, required this.onTap});

  final ChatMessage message;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final BroadcastSummary s = message.summary;
    final double raw = s.counts.willGet <= 0 ? 0 : s.counts.sent / s.counts.willGet;
    final double progress = raw > 1 ? 1 : raw;
    final TextStyle chip = TextStyle(
      backgroundColor: theme.colors.secondary,
      color: theme.colors.secondaryForeground,
      fontWeight: FontWeight.w600,
    );
    return Align(
      alignment: Alignment.centerRight,
      child: GestureDetector(
        onTap: onTap,
        child: Container(
          constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.84),
          margin: const EdgeInsets.symmetric(vertical: 5),
          decoration: BoxDecoration(
            color: bubbleColor(context),
            borderRadius: const BorderRadius.only(
              topLeft: Radius.circular(16),
              topRight: Radius.circular(4),
              bottomLeft: Radius.circular(16),
              bottomRight: Radius.circular(16),
            ),
            boxShadow: cardShadow(context),
          ),
          child: ClipRRect(
            borderRadius: const BorderRadius.only(
              topLeft: Radius.circular(16),
              topRight: Radius.circular(4),
              bottomLeft: Radius.circular(16),
              bottomRight: Radius.circular(16),
            ),
            child: Container(
              decoration: BoxDecoration(border: Border(left: BorderSide(color: theme.colors.primary, width: 3))),
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  if (message.attachment case final BroadcastAttachment f) ...<Widget>[
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(color: theme.colors.secondary, borderRadius: BorderRadius.circular(10)),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: <Widget>[
                          Icon(fileIcon(f.contentType), size: 20, color: theme.colors.secondaryForeground),
                          const SizedBox(width: 8),
                          Flexible(
                            child: Text(
                              f.fileName,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.typography.body.sm.copyWith(
                                color: theme.colors.secondaryForeground,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Text(
                            fileSizeText(f.sizeBytes),
                            style: theme.typography.body.xs.copyWith(color: theme.colors.secondaryForeground),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 8),
                  ],
                  Text.rich(
                    TextSpan(children: highlightedText(message.text ?? '', chip: chip)),
                    style: theme.typography.body.md,
                  ),
                  if (s.isLive) ...<Widget>[
                    const SizedBox(height: 8),
                    FDeterminateProgress(value: progress),
                  ],
                  const SizedBox(height: 8),
                  // Totals: sent, delivered (read counts as delivered), failed.
                  Wrap(
                    spacing: 10,
                    runSpacing: 4,
                    children: <Widget>[
                      _Count(icon: FLucideIcons.check, value: s.counts.sent, label: 'sent', color: theme.colors.mutedForeground),
                      _Count(
                        icon: FLucideIcons.checkCheck,
                        value: s.counts.delivered,
                        label: 'delivered',
                        color: theme.colors.primary,
                      ),
                      if (s.counts.failed > 0)
                        _Count(
                          icon: FLucideIcons.circleX,
                          value: s.counts.failed,
                          label: 'failed',
                          color: theme.colors.destructive,
                        ),
                      if (s.counts.pending > 0)
                        _Count(
                          icon: FLucideIcons.clock,
                          value: s.counts.pending,
                          label: 'waiting',
                          color: const Color(0xFFD97706),
                        ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Row(
                    mainAxisSize: MainAxisSize.min,
                    children: <Widget>[
                      Flexible(
                        child: Text(
                          chatSubtitle(s),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.typography.body.xs.copyWith(
                            color: statusColor(context, s.status),
                            fontWeight: s.isLive ? FontWeight.w700 : FontWeight.w500,
                          ),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Text(timeText(s.createdAt), style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
                      const SizedBox(width: 4),
                      Icon(
                        s.counts.delivered > 0 ? FLucideIcons.checkCheck : FLucideIcons.check,
                        size: 14,
                        color: s.counts.delivered > 0 ? theme.colors.primary : theme.colors.mutedForeground,
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Count extends StatelessWidget {
  const _Count({required this.icon, required this.value, required this.label, required this.color});

  final IconData icon;
  final int value;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Icon(icon, size: 13, color: color),
          const SizedBox(width: 3),
          Text(
            '$value $label',
            style: context.theme.typography.body.xs.copyWith(color: color, fontWeight: FontWeight.w600),
          ),
        ],
      );
}

/// The date line between messages of different days.
class DayLine extends StatelessWidget {
  const DayLine(this.day, {super.key});

  final DateTime day;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Center(
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 10),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 5),
        decoration: BoxDecoration(
          color: theme.colors.card,
          borderRadius: BorderRadius.circular(999),
          boxShadow: cardShadow(context),
        ),
        child: Text(
          dayLineText(day),
          style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground, fontWeight: FontWeight.w600),
        ),
      ),
    );
  }
}

/// Messages with a date line before the first message of each day.
List<Widget> messagesWithDayLines(List<ChatMessage> messages, void Function(ChatMessage) open) {
  final List<Widget> out = <Widget>[];
  DateTime? lastDay;
  for (final ChatMessage m in messages) {
    final DateTime t = m.summary.createdAt;
    final DateTime day = DateTime(t.year, t.month, t.day);
    if (lastDay == null || day != lastDay) {
      out.add(DayLine(day));
      lastDay = day;
    }

    out.add(ChatBubble(message: m, onTap: () => open(m)));
  }

  return out;
}
