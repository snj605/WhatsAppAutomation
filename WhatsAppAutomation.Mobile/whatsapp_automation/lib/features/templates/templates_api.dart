import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';

final Provider<TemplatesApi> templatesApiProvider =
    Provider<TemplatesApi>((Ref ref) => TemplatesApi(ref.watch(apiClientProvider)));

/// One of the user's saved messages (GET /api/v1/templates).
class MessageTemplate {
  const MessageTemplate({required this.id, required this.name, required this.text});

  factory MessageTemplate.fromJson(Json json) =>
      MessageTemplate(id: json['id'] as String, name: json['name'] as String? ?? '', text: json['text'] as String? ?? '');

  final String id;
  final String name;
  final String text;
}

class TemplatesApi {
  const TemplatesApi(this._api);

  final ApiClient _api;

  Future<List<MessageTemplate>> list() async =>
      asList(await _api.get('/api/v1/templates')).map(MessageTemplate.fromJson).toList();

  Future<MessageTemplate> save(String? id, String name, String text) async => MessageTemplate.fromJson(
        (id == null
            ? await _api.post('/api/v1/templates', <String, dynamic>{'name': name, 'text': text})
            : await _api.put('/api/v1/templates/$id', <String, dynamic>{'name': name, 'text': text}))! as Json,
      );

  Future<void> remove(String id) => _api.delete('/api/v1/templates/$id');
}

/// Ready messages offered next to the user's own templates (same as the website's send-templates.ts).
/// {{name}} is the person's name; {a|b} picks one word per person.
const List<(String, String)> readyMessages = <(String, String)>[
  ('Diwali', '{Dear|Hello} {{name}}, wishing you and your family a very happy and prosperous Diwali 🪔'),
  ('Birthday', '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.'),
  ('New Year', 'Happy New Year {{name}}! 🎉 Wishing you health, happiness and success in the year ahead.'),
  ('Thank you', 'Thank you {{name}} for being with us. 🙏'),
];

/// Extra pieces the user can put into a message (same as the website's "More").
const List<(String, String, String)> moreInserts = <(String, String, String)>[
  ('Name in Gujarati', 'નામ', '{{name_gu}}'),
  ('Name in Hindi', 'नाम', '{{name_hi}}'),
  ('Name in English', 'Name', '{{name_en}}'),
  ('Salutation', 'Pujya, Dear…', '{{salutation}}'),
  ('Different greeting per person', 'Hi / Hello / Namaste', '{Hi|Hello|Namaste}'),
];

const String namePlaceholder = '{{name}}';

/// The ready-made birthday wish (same as the website).
const String birthdayText = '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.';
