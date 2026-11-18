import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../broadcasts/broadcast_models.dart';

final Provider<SendingApi> sendingApiProvider = Provider<SendingApi>((Ref ref) => SendingApi(ref.watch(apiClientProvider)));

/// How the user's number sends (GET/PUT /api/v1/sending/settings). Same as the website's sending.api.ts.
class SendingSettings {
  const SendingSettings({
    required this.dailyLimit,
    required this.sendFrom,
    required this.sendUntil,
    required this.timeZone,
    required this.fitsPerDay,
    required this.averageSeconds,
  });

  factory SendingSettings.fromJson(Json json) => SendingSettings(
        dailyLimit: asInt(json['dailyLimit']),
        sendFrom: json['sendFrom'] as String? ?? '09:00',
        sendUntil: json['sendUntil'] as String? ?? '21:00',
        timeZone: json['timeZone'] as String? ?? '',
        fitsPerDay: asInt(json['fitsPerDay']),
        averageSeconds: asInt(json['averageSeconds']),
      );

  final int dailyLimit;
  final String sendFrom;
  final String sendUntil;
  final String timeZone;
  final int fitsPerDay;

  /// About how long one message takes on average, pauses and rests included (the system's own rhythm).
  final int averageSeconds;

  /// The same start and end time means any time of day.
  bool get anyTime => sendFrom == sendUntil;
}

/// One message waiting in today's queue, in sending order.
class QueueItem {
  const QueueItem({
    required this.id,
    required this.name,
    required this.kind,
    required this.status,
    required this.pauseReason,
    required this.counts,
    required this.expectedStart,
    required this.expectedFinish,
    required this.canMoveUp,
    required this.canMoveDown,
  });

  factory QueueItem.fromJson(Json json) => QueueItem(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        kind: json['kind'] as String? ?? 'broadcast',
        status: json['status'] as String? ?? 'sending',
        pauseReason: asStringOrNull(json['pauseReason']),
        counts: BroadcastCounts.fromJson(json['counts'] as Json),
        expectedStart: asDateOrNull(json['expectedStart']),
        expectedFinish: asDateOrNull(json['expectedFinish']),
        canMoveUp: asBool(json['canMoveUp']),
        canMoveDown: asBool(json['canMoveDown']),
      );

  final String id;
  final String name;

  /// "occasion" (always first) or "broadcast".
  final String kind;
  final String status;
  final String? pauseReason;
  final BroadcastCounts counts;
  final DateTime? expectedStart;
  final DateTime? expectedFinish;
  final bool canMoveUp;
  final bool canMoveDown;
}

class SendQueue {
  const SendQueue({
    required this.connected,
    required this.dailyLimit,
    required this.sentToday,
    required this.leftToday,
    required this.sendingHours,
    required this.inSendingHours,
    required this.nextSendAt,
    required this.waiting,
    required this.items,
  });

  factory SendQueue.fromJson(Json json) => SendQueue(
        connected: asBool(json['connected']),
        dailyLimit: asInt(json['dailyLimit']),
        sentToday: asInt(json['sentToday']),
        leftToday: asInt(json['leftToday']),
        sendingHours: json['sendingHours'] as String? ?? '',
        inSendingHours: asBool(json['inSendingHours']),
        nextSendAt: asDateOrNull(json['nextSendAt']),
        waiting: asStringOrNull(json['waiting']),
        items: asList(json['items']).map(QueueItem.fromJson).toList(),
      );

  final bool connected;
  final int dailyLimit;
  final int sentToday;
  final int leftToday;
  final String sendingHours;
  final bool inSendingHours;
  final DateTime? nextSendAt;
  final String? waiting;
  final List<QueueItem> items;
}

/// One person in a message's results.
class RecipientRow {
  const RecipientRow({
    required this.id,
    required this.contactId,
    required this.name,
    required this.phoneNumber,
    required this.status,
    required this.reason,
    required this.attempts,
    required this.sentAt,
    required this.deliveredAt,
    required this.readAt,
  });

  factory RecipientRow.fromJson(Json json) => RecipientRow(
        id: json['id'] as String,
        contactId: asStringOrNull(json['contactId']),
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        status: json['status'] as String? ?? 'pending',
        reason: asStringOrNull(json['reason']),
        attempts: asInt(json['attempts']),
        sentAt: asDateOrNull(json['sentAt']),
        deliveredAt: asDateOrNull(json['deliveredAt']),
        readAt: asDateOrNull(json['readAt']),
      );

  final String id;
  final String? contactId;
  final String name;
  final String phoneNumber;
  final String status;
  final String? reason;
  final int attempts;
  final DateTime? sentAt;
  final DateTime? deliveredAt;
  final DateTime? readAt;
}

class RecipientsPage {
  const RecipientsPage({required this.items, required this.total, required this.summary});

  factory RecipientsPage.fromJson(Json json) => RecipientsPage(
        items: asList(json['items']).map(RecipientRow.fromJson).toList(),
        total: asInt(json['total']),
        summary: BroadcastSummary.fromJson(json['summary'] as Json),
      );

  final List<RecipientRow> items;
  final int total;
  final BroadcastSummary summary;
}

class SendingApi {
  const SendingApi(this._api);

  final ApiClient _api;

  Future<SendingSettings> settings() async => SendingSettings.fromJson((await _api.get('/api/v1/sending/settings'))! as Json);

  Future<SendingSettings> saveSettings({
    required int dailyLimit,
    required String sendFrom,
    required String sendUntil,
  }) async =>
      SendingSettings.fromJson((await _api.put('/api/v1/sending/settings', <String, dynamic>{
        'dailyLimit': dailyLimit,
        'sendFrom': sendFrom,
        'sendUntil': sendUntil,
      }))! as Json);

  Future<SendQueue> queue() async => SendQueue.fromJson((await _api.get('/api/v1/broadcasts/queue'))! as Json);

  /// direction: "up" or "down".
  Future<void> move(String id, String direction) => _api.post('/api/v1/broadcasts/$id/move', <String, dynamic>{'direction': direction});

  /// Puts failed people back in the queue: all of them, or only [recipientIds].
  Future<void> retry(String id, List<String>? recipientIds) =>
      _api.post('/api/v1/broadcasts/$id/retry', <String, dynamic>{'recipientIds': recipientIds});

  /// list: all, pending, sent, delivered, read, failed or skipped.
  Future<RecipientsPage> recipients(String id, String list, {int page = 1, int pageSize = 50}) async => RecipientsPage.fromJson(
        (await _api.get('/api/v1/broadcasts/$id/recipients',
            query: <String, String>{'status': list, 'page': '$page', 'pageSize': '$pageSize'}))! as Json,
      );
}

/// Hours between two "HH:mm" times; past midnight counts (21:00 to 06:00 = 9 hours). Same as the website.
double windowHours(String from, String until) {
  int toMin(String t) {
    final List<String> p = t.split(':');
    return (int.tryParse(p.first) ?? 0) * 60 + (p.length > 1 ? int.tryParse(p[1]) ?? 0 : 0);
  }

  final int diff = (toMin(until) - toMin(from) + 1440) % 1440;
  return (diff == 0 ? 1440 : diff) / 60;
}

// Same as the server's HumanPace: 85% of the hours, never faster than 20 s a message on average.
const double _windowShare = 0.85;
const double _minAverageSeconds = 20;
const double _maxAverageSeconds = 300;

/// How many messages fit in a day with these hours (at most the daily limit). Same as the server and the website.
int fitsPerDay({required int limit, required double hours}) {
  if (limit <= 0 || hours <= 0) {
    return 0;
  }

  final int fit = (hours * 3600 * _windowShare / _minAverageSeconds).floor();
  return fit < limit ? fit : limit;
}

/// The average time per message, pauses and rests included. Same as the server and the website.
double averageSeconds({required int limit, required double hours}) =>
    (hours * 3600 * _windowShare / (limit < 1 ? 1 : limit)).clamp(_minAverageSeconds, _maxAverageSeconds);
