import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../files/files_api.dart';

final Provider<TemplatesApi> templatesApiProvider =
    Provider<TemplatesApi>((Ref ref) => TemplatesApi(ref.watch(apiClientProvider)));

/// A saved message (GET /api/v1/templates): the user's own, and the shared ones of their organisation.
class MessageTemplate {
  const MessageTemplate({
    required this.id,
    required this.name,
    required this.text,
    this.shared = false,
    this.canEdit = true,
    this.attachment,
  });

  factory MessageTemplate.fromJson(Json json) => MessageTemplate(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        text: json['text'] as String? ?? '',
        shared: asBool(json['shared']),
        canEdit: json['canEdit'] as bool? ?? true,
        attachment: json['attachment'] == null ? null : UploadedFile.fromJson(json['attachment'] as Json),
      );

  final String id;
  final String name;
  final String text;

  /// In the organisation's library: every member can use it.
  final bool shared;

  /// False for the admin's shared templates, when a member sees them.
  final bool canEdit;

  /// A photo, video or document that goes with the text.
  final UploadedFile? attachment;
}

class TemplatesApi {
  const TemplatesApi(this._api);

  final ApiClient _api;

  Future<List<MessageTemplate>> list() async =>
      asList(await _api.get('/api/v1/templates')).map(MessageTemplate.fromJson).toList();

  Future<MessageTemplate> save(String? id, String name, String text, {bool shared = false, String? attachmentId}) async {
    final Json body = <String, dynamic>{'name': name, 'text': text, 'shared': shared, 'attachmentId': attachmentId};
    return MessageTemplate.fromJson(
      (id == null ? await _api.post('/api/v1/templates', body) : await _api.put('/api/v1/templates/$id', body))! as Json,
    );
  }

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

