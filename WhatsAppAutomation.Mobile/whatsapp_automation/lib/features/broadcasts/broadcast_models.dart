import '../../core/json.dart';

/// Shapes of /api/v1/broadcasts. They mirror the website's broadcasts.models.ts.

class BroadcastCounts {
  const BroadcastCounts({
    required this.total,
    required this.pending,
    required this.sent,
    required this.delivered,
    required this.read,
    required this.failed,
    required this.skipped,
  });

  factory BroadcastCounts.fromJson(Json json) => BroadcastCounts(
        total: asInt(json['total']),
        pending: asInt(json['pending']),
        sent: asInt(json['sent']),
        delivered: asInt(json['delivered']),
        read: asInt(json['read']),
        failed: asInt(json['failed']),
        skipped: asInt(json['skipped']),
      );

  final int total;
  final int pending;
  final int sent;
  final int delivered;
  final int read;
  final int failed;
  final int skipped;

  /// The people who will get it (everyone except the skipped).
  int get willGet => total - skipped;
}

class BroadcastSummary {
  const BroadcastSummary({
    required this.id,
    required this.name,
    required this.status,
    required this.pauseReason,
    required this.counts,
    required this.createdAt,
    required this.finishedAt,
  });

  factory BroadcastSummary.fromJson(Json json) => BroadcastSummary(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        status: json['status'] as String? ?? 'completed',
        pauseReason: json['pauseReason'] as String?,
        counts: BroadcastCounts.fromJson(json['counts'] as Json),
        createdAt: asDateOrNull(json['createdAt']) ?? DateTime.now(),
        finishedAt: asDateOrNull(json['finishedAt']),
      );

  final String id;
  final String name;

  /// sending, paused, completed or cancelled.
  final String status;
  final String? pauseReason;
  final BroadcastCounts counts;
  final DateTime createdAt;
  final DateTime? finishedAt;

  bool get isLive => status == 'sending' || status == 'paused';
}

class BroadcastAttachment {
  const BroadcastAttachment({
    required this.id,
    required this.fileName,
    required this.contentType,
    required this.sizeBytes,
    this.kept = true,
  });

  factory BroadcastAttachment.fromJson(Json json) => BroadcastAttachment(
        id: json['id'] as String,
        fileName: json['fileName'] as String? ?? '',
        contentType: json['contentType'] as String? ?? '',
        sizeBytes: asInt(json['sizeBytes']),
        kept: json['kept'] as bool? ?? true,
      );

  /// False once the server freed the file after sending: it cannot be sent again.
  final bool kept;

  final String id;
  final String fileName;
  final String contentType;
  final int sizeBytes;
}

class Recipient {
  const Recipient({
    required this.id,
    required this.contactId,
    required this.name,
    required this.phoneNumber,
    required this.text,
    required this.status,
    required this.reason,
  });

  factory Recipient.fromJson(Json json) => Recipient(
        id: json['id'] as String,
        contactId: json['contactId'] as String?,
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        text: json['text'] as String?,
        status: json['status'] as String? ?? 'pending',
        reason: json['reason'] as String?,
      );

  final String id;

  /// Null when the contact was deleted after sending.
  final String? contactId;
  final String name;
  final String phoneNumber;
  final String? text;

  /// The status the app shows. "read" is shown as delivered: the app shows only sent / delivered / failed.
  String get shownStatus => status == 'read' ? 'delivered' : status;

  /// pending, sent, delivered, read, failed or skipped.
  final String status;
  final String? reason;
}

class BroadcastDetails {
  const BroadcastDetails({
    required this.summary,
    required this.text,
    required this.textEn,
    required this.textHi,
    required this.textGu,
    required this.recipients,
    required this.attachment,
  });

  factory BroadcastDetails.fromJson(Json json) => BroadcastDetails(
        summary: BroadcastSummary.fromJson(json['summary'] as Json),
        text: json['text'] as String?,
        textEn: json['textEn'] as String?,
        textHi: json['textHi'] as String?,
        textGu: json['textGu'] as String?,
        recipients: asList(json['recipients']).map(Recipient.fromJson).toList(),
        attachment: json['attachment'] == null ? null : BroadcastAttachment.fromJson(json['attachment'] as Json),
      );

  final BroadcastSummary summary;
  final String? text;
  final String? textEn;
  final String? textHi;
  final String? textGu;
  final List<Recipient> recipients;
  final BroadcastAttachment? attachment;
}

class SkippedPerson {
  const SkippedPerson({required this.name, required this.phoneNumber, required this.reason});

  factory SkippedPerson.fromJson(Json json) => SkippedPerson(
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        reason: json['reason'] as String? ?? '',
      );

  final String name;
  final String phoneNumber;
  final String reason;
}

class PreviewSample {
  const PreviewSample({required this.contactName, required this.text, this.language = 'all'});

  factory PreviewSample.fromJson(Json json) => PreviewSample(
        contactName: json['contactName'] as String? ?? '',
        text: json['text'] as String? ?? '',
        language: json['language'] as String? ?? 'all',
      );

  /// en, hi, gu, or all for one message for everyone.
  final String language;
  final String contactName;
  final String text;
}

class BroadcastPreview {
  const BroadcastPreview({
    required this.total,
    required this.willSend,
    required this.skipped,
    required this.samples,
    required this.estimatedDays,
  });

  factory BroadcastPreview.fromJson(Json json) => BroadcastPreview(
        total: asInt(json['total']),
        willSend: asInt(json['willSend']),
        skipped: asList(json['skipped']).map(SkippedPerson.fromJson).toList(),
        samples: asList(json['samples']).map(PreviewSample.fromJson).toList(),
        estimatedDays: asInt(json['estimatedDays']),
      );

  final int total;
  final int willSend;
  final List<SkippedPerson> skipped;
  final List<PreviewSample> samples;
  final int estimatedDays;
}

class Pacing {
  const Pacing({
    required this.waiting,
    required this.nextSendAt,
    this.connected = false,
    this.sentToday = 0,
    this.dailyCap = 0,
    this.sendingHours,
  });

  factory Pacing.fromJson(Json json) => Pacing(
        waiting: json['waiting'] as String?,
        nextSendAt: asDateOrNull(json['nextSendAt']),
        connected: asBool(json['connected']),
        sentToday: asInt(json['sentToday']),
        dailyCap: asInt(json['dailyCap']),
        sendingHours: json['sendingHours'] as String?,
      );

  final bool connected;
  final int sentToday;
  final int dailyCap;

  /// For example "9:00–21:00".
  final String? sendingHours;

  /// Why sending waits right now, in words (for example outside sending hours).
  final String? waiting;
  final DateTime? nextSendAt;
}

/// What a send needs. "text" is one message for everyone; placeholders are filled per person by the server.
class BroadcastInput {
  BroadcastInput({
    required this.name,
    required this.text,
    this.attachmentId,
    this.categoryIds = const <String>[],
    this.groupIds = const <String>[],
    this.contactIds = const <String>[],
    this.allContacts = false,
    this.forBirthday = false,
  });

  final String name;
  final String text;
  final String? attachmentId;
  final List<String> categoryIds;
  final List<String> groupIds;
  final List<String> contactIds;
  final bool allContacts;
  final bool forBirthday;

  Json toJson() => <String, dynamic>{
        'name': name,
        'text': text,
        'textEn': null,
        'textHi': null,
        'textGu': null,
        'categoryIds': categoryIds,
        'groupIds': groupIds,
        'contactIds': contactIds,
        'allContacts': allContacts,
        'attachmentId': attachmentId,
        'forBirthday': forBirthday,
      };
}

/// The grey line under a chat's name: where it stands, in a few words (same words as the website).
String chatSubtitle(BroadcastSummary s) {
  final int sent = s.counts.sent;
  final int willGet = s.counts.willGet;
  return switch (s.status) {
    'sending' => 'Sending · $sent of $willGet',
    'paused' => 'Paused · ${s.pauseReason ?? '$sent of $willGet sent'}',
    'cancelled' => 'Stopped · $sent sent',
    _ => 'Done · $sent sent · ${s.counts.delivered} delivered${s.counts.failed > 0 ? ' · ${s.counts.failed} failed' : ''}',
  };
}

/// A name for a new message: its first words without placeholders (same rule as the website).
String chatName(String text) {
  final String plain = text
      .replaceAll(RegExp(r'\{\{[^}]*\}\}|\{[^}]*\}'), ' ')
      .replaceAll(RegExp(r'\s+'), ' ')
      .replaceFirst(RegExp(r'^[\s,.!:;-]+'), '')
      .trim();
  final String short = plain.length > 40 ? '${plain.substring(0, 40).trimRight()}…' : plain;
  if (short.isNotEmpty) {
    return short;
  }

  final DateTime now = DateTime.now();
  const List<String> months = <String>['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return 'Message ${now.day} ${months[now.month - 1]}';
}
