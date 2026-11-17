import 'package:flutter_test/flutter_test.dart';
import 'package:whatsapp_automation/features/whatsapp/whatsapp_models.dart';

void main() {
  test('phone numbers need the country code', () {
    expect(normalizePhone('+91 98250 12345'), '+919825012345');
    expect(normalizePhone('9825012345'), isNull);
    expect(normalizePhone('+91'), isNull);
  });

  test('the code is shown in two groups of four', () {
    expect(formatPairingCode('ABCD1234'), 'ABCD-1234');
  });

  test('a linked number counts as linked while it reconnects', () {
    expect(WhatsAppAccount.fromJson(<String, dynamic>{'status': 'reconnecting'}).isLinked, isTrue);
    expect(WhatsAppAccount.fromJson(<String, dynamic>{'status': 'linking'}).isLinked, isFalse);
  });
}
