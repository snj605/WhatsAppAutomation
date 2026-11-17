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

  /// group, category, birthdays or broadcast.
  final String kind;

  /// Group, category or broadcast id; null for today's Birthdays.
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

    if (kind == 'birthdays') {
      return memberCount == 1 ? '1 birthday today · tap to wish' : '$memberCount birthdays today · tap to wish';
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

class BirthdayPerson {
  const BirthdayPerson({
    required this.contactId,
    required this.name,
    required this.phoneNumber,
    required this.turnsAge,
    required this.optedOut,
    required this.status,
  });

  factory BirthdayPerson.fromJson(Json json) => BirthdayPerson(
        contactId: json['contactId'] as String,
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        turnsAge: asIntOrNull(json['turnsAge']),
        optedOut: asBool(json['optedOut']),
        status: json['status'] as String? ?? 'not_sent',
      );

  final String contactId;
  final String name;
  final String phoneNumber;
  final int? turnsAge;
  final bool optedOut;

  /// not_sent, pending, sent, delivered, read, failed or skipped.
  final String status;
}

class BirthdaysChat {
  const BirthdaysChat({required this.date, required this.people, required this.messages});

  factory BirthdaysChat.fromJson(Json json) => BirthdaysChat(
        date: asDateOrNull(json['date']) ?? DateTime.now(),
        people: asList(json['people']).map(BirthdayPerson.fromJson).toList(),
        messages: asList(json['messages']).map(ChatMessage.fromJson).toList(),
      );

  final DateTime date;
  final List<BirthdayPerson> people;
  final List<ChatMessage> messages;
}
