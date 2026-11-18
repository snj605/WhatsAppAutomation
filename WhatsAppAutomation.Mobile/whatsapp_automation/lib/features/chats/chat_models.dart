import '../../core/json.dart';
import '../broadcasts/broadcast_models.dart';

/// Shapes of /api/v1/chats (same as the website's chats.api.ts).

class ChatListItem {
  const ChatListItem({
    required this.kind,
    required this.id,
    required this.name,
    required this.memberCount,
    required this.lastText,
    required this.lastAt,
    required this.last,
  });

  factory ChatListItem.fromJson(Json json) => ChatListItem(
        kind: json['kind'] as String,
        id: json['id'] as String?,
        name: json['name'] as String? ?? '',
        memberCount: asInt(json['memberCount']),
        lastText: json['lastText'] as String?,
        lastAt: asDateOrNull(json['lastAt']),
        last: json['last'] == null ? null : BroadcastSummary.fromJson(json['last'] as Json),
      );

  /// group, category or broadcast.
  final String kind;

  /// Group, category or broadcast id.
  final String? id;
  final String name;
  final int memberCount;
  final String? lastText;
  final DateTime? lastAt;
  final BroadcastSummary? last;

  /// The grey line under the name (same rules as the website).
  String get subtitle {
    final BroadcastSummary? l = last;
    if (l != null && l.isLive) {
      return chatSubtitle(l);
    }

    if (lastText != null) {
      return lastText!.replaceAll(RegExp(r'\s+'), ' ');
    }

    if (kind == 'group' || kind == 'category') {
      return '$memberCount ${memberCount == 1 ? 'member' : 'members'} · tap to send a message';
    }

    return l == null ? '' : chatSubtitle(l);
  }
}

/// One message in a chat: a broadcast with its text, file and progress.
class ChatMessage {
  const ChatMessage({required this.summary, required this.text, required this.attachment});

  factory ChatMessage.fromJson(Json json) => ChatMessage(
        summary: BroadcastSummary.fromJson(json['summary'] as Json),
        text: json['text'] as String?,
        attachment: json['attachment'] == null ? null : BroadcastAttachment.fromJson(json['attachment'] as Json),
      );

  final BroadcastSummary summary;
  final String? text;
  final BroadcastAttachment? attachment;
}

class TargetChat {
  const TargetChat({
    required this.kind,
    required this.id,
    required this.name,
    required this.memberCount,
    required this.messages,
  });

  factory TargetChat.fromJson(Json json) => TargetChat(
        kind: json['kind'] as String,
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        memberCount: asInt(json['memberCount']),
        messages: asList(json['messages']).map(ChatMessage.fromJson).toList(),
      );

  /// group or category.
  final String kind;
  final String id;
  final String name;
  final int memberCount;
  final List<ChatMessage> messages;
}

