import 'package:flutter_test/flutter_test.dart';
import 'package:whatsapp_automation/features/modules/modules_api.dart';
import 'package:whatsapp_automation/features/sending/sending_api.dart';
import 'package:whatsapp_automation/features/sending/status_look.dart';

void main() {
  test('sending hours count past midnight, and the same time means all day', () {
    expect(windowHours('09:00', '21:00'), 12);
    expect(windowHours('21:00', '06:00'), 9);
    expect(windowHours('00:00', '00:00'), 24);
  });

  test('fits per day matches the server and the website', () {
    expect(fitsPerDay(limit: 300, chunk: 50, restMinutes: 15, minGap: 30, maxGap: 90, hours: 12), 300);
    expect(fitsPerDay(limit: 500, chunk: 100, restMinutes: 5, minGap: 10, maxGap: 20, hours: 15), 500);
    expect(fitsPerDay(limit: 500, chunk: 50, restMinutes: 15, minGap: 30, maxGap: 90, hours: 1), 50);
  });

  test('every message state has the same words as the website', () {
    expect(statusLook('read').$1, 'Read');
    expect(statusLook('failed').$1, 'Failed');
    expect(statusLook(null).$1, 'Not sent');
  });

  test('the queue reads occasions and expected times', () {
    final SendQueue q = SendQueue.fromJson(<String, dynamic>{
      'connected': true,
      'dailyLimit': 300,
      'sentToday': 12,
      'leftToday': 288,
      'sendingHours': '09:00–21:00',
      'inSendingHours': true,
      'nextSendAt': '2026-11-18T10:00:00Z',
      'waiting': null,
      'items': <dynamic>[
        <String, dynamic>{
          'id': 'a',
          'name': 'Birthday 18 Nov',
          'kind': 'occasion',
          'status': 'sending',
          'pauseReason': null,
          'counts': <String, dynamic>{'total': 3, 'pending': 2, 'sent': 1, 'delivered': 1, 'read': 0, 'failed': 0, 'skipped': 0},
          'expectedStart': '2026-11-18T10:00:00Z',
          'expectedFinish': '2026-11-18T10:03:00Z',
          'canMoveUp': false,
          'canMoveDown': false,
        },
      ],
    });
    expect(q.dailyLimit, 300);
    expect(q.items.single.kind, 'occasion');
    expect(q.items.single.counts.willGet, 3);
    expect(q.items.single.expectedFinish, isNotNull);
  });

  test('an occasion with a second name gets a wish that uses it', () {
    final OccasionModule m = OccasionModule.fromJson(<String, dynamic>{
      'id': 'm',
      'name': 'Asthi',
      'kind': 'yearly',
      'forNameLabel': 'In memory of',
      'autoSend': true,
      'time': '07:15',
      'templateId': null,
      'templateName': null,
      'nextRunAt': '2026-11-19T01:45:00Z',
      'todayCount': 2,
      'timeZone': 'Asia/Kolkata',
    });
    expect(m.ready, isTrue);
    expect(defaultWish(m), contains('{{for_name}}'));
  });
}
