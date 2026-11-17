import 'package:flutter_test/flutter_test.dart';
import 'package:whatsapp_automation/core/realtime/hub_connection.dart';

void main() {
  test('several messages in one piece of text are split', () {
    expect(splitHubFrames('{}\u001e{"type":6}\u001e'), <String>['{}', '{"type":6}']);
  });

  test('a pushed event gives its name and values', () {
    final HubMessage? message =
        parseHubFrame('{"type":1,"target":"pairingCode","arguments":[{"code":"ABCD1234"}]}');

    expect(message?.target, 'pairingCode');
    expect((message?.arguments.first as Map<String, dynamic>)['code'], 'ABCD1234');
  });

  test('handshake replies and pings are not events', () {
    expect(parseHubFrame('{}'), isNull);
    expect(parseHubFrame('{"type":6}'), isNull);
  });
}
