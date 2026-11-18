import 'package:flutter/painting.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:whatsapp_automation/core/format.dart';
import 'package:whatsapp_automation/features/contacts/contact_models.dart';
import 'package:whatsapp_automation/features/preview/wa_bubble.dart';
import 'package:whatsapp_automation/features/whatsapp/whatsapp_controller.dart';

String plain(List<InlineSpan> spans) => spans.map((InlineSpan s) => (s as TextSpan).text).join();

void main() {
  test('WhatsApp formatting: bold, italic, strike, monospace; marks inside words stay', () {
    final List<InlineSpan> spans = waSpans('Hi *Ramesh*, _happy_ ~old~ `x` 2*3*4');
    expect(plain(spans), 'Hi Ramesh, happy old x 2*3*4');
    expect((spans[1] as TextSpan).style?.fontWeight, FontWeight.w700);
    expect((spans[3] as TextSpan).style?.fontStyle, FontStyle.italic);
  });

  test('what a file shows as', () {
    expect(waKind('image/jpeg'), 'image');
    expect(waKind('image/svg+xml'), 'document');
    expect(waKind('video/mp4'), 'video');
    expect(waKind('video/mp4', isBigVideo: true), 'document');
    expect(docType('Invite.pdf'), 'PDF');
    expect(docType('noext'), 'FILE');
  });

  test('sample text fills the second name and shows custom fields by key', () {
    expect(sampleText('{{name}} {{for_name}} {{city}}'), 'Ramesh Patel Late Shri Mohanbhai [city]');
  });

  test('contact reads occasions and fields; input sends them only when given', () {
    final Contact c = Contact.fromJson(<String, dynamic>{
      'id': 'c1',
      'phoneNumber': '+919825011111',
      'occasions': <dynamic>[
        <String, dynamic>{'moduleId': 'm1', 'day': 5, 'month': 3, 'year': 2019, 'forName': 'Shantaben'},
      ],
      'fields': <String, dynamic>{'f1': '2019-03-05'},
    });
    expect(c.occasions.single.forName, 'Shantaben');
    const ContactField date = ContactField(id: 'f1', name: 'Joined', key: 'joined', type: 'date', options: <String>[], contactCount: 1);
    expect(date.display(c.fields['f1']), '5 Mar 2019');

    const ContactInput unchanged = ContactInput(
      phoneNumber: '+919825011111',
      savedName: null,
      nameEn: 'Ramesh',
      nameHi: null,
      nameGu: null,
      language: 'en',
      categoryId: null,
      salutationId: null,
      notes: null,
      groupIds: <String>[],
    );
    expect(unchanged.toJson().containsKey('occasions'), isFalse);
    expect(unchanged.toJson().containsKey('fields'), isFalse);
  });

  test('the QR picture from the server is read only when it is a PNG data URL', () {
    expect(qrPng('data:image/png;base64,iVBORw0KGgo='), isNotNull);
    expect(qrPng('javascript:alert(1)'), isNull);
    expect(qrPng(null), isNull);
  });
}
