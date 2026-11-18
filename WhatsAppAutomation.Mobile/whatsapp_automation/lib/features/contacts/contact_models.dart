import '../../core/json.dart';

/// Shapes of the /api/v1 contacts endpoints (same as the website's contacts.models.ts).

const List<(String, String)> languages = <(String, String)>[
  ('en', 'English'),
  ('hi', 'Hindi · हिन्दी'),
  ('gu', 'Gujarati · ગુજરાતી'),
];

String languageLabel(String code) => switch (code) {
      'hi' => 'Hindi',
      'gu' => 'Gujarati',
      _ => 'English',
    };

/// The colours offered for a new category (same as the website).
const List<int> categoryColors = <int>[0xFF10B981, 0xFF3B82F6, 0xFFF59E0B, 0xFFEF4444, 0xFF8B5CF6, 0xFFEC4899, 0xFF14B8A6, 0xFF64748B];

/// "#10b981" to a colour value; grey when it cannot be read.
int colorFromHex(String? hex) {
  final String h = (hex ?? '').replaceFirst('#', '');
  final int? v = int.tryParse(h, radix: 16);
  return v == null || h.length != 6 ? 0xFF64748B : 0xFF000000 | v;
}

String hexFromColor(int color) => '#${(color & 0xFFFFFF).toRadixString(16).padLeft(6, '0')}';

class Category {
  const Category({required this.id, required this.name, required this.color, required this.contactCount});

  factory Category.fromJson(Json json) => Category(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        color: json['color'] as String? ?? '#64748b',
        contactCount: asInt(json['contactCount']),
      );

  final String id;
  final String name;
  final String color;
  final int contactCount;
}

class Group {
  const Group({required this.id, required this.name, required this.contactCount});

  factory Group.fromJson(Json json) =>
      Group(id: json['id'] as String, name: json['name'] as String? ?? '', contactCount: asInt(json['contactCount']));

  final String id;
  final String name;
  final int contactCount;
}

class Birthday {
  const Birthday({required this.day, required this.month, this.year});

  factory Birthday.fromJson(Json json) =>
      Birthday(day: asInt(json['day']), month: asInt(json['month']), year: asIntOrNull(json['year']));

  final int day;
  final int month;
  final int? year;

  Json toJson() => <String, dynamic>{'day': day, 'month': month, 'year': year};
}

class Contact {
  const Contact({
    required this.id,
    required this.phoneNumber,
    required this.savedName,
    required this.nameEn,
    required this.nameHi,
    required this.nameGu,
    required this.language,
    required this.categoryId,
    required this.salutation,
    required this.birthday,
    required this.notes,
    required this.groups,
    required this.needsDetails,
    required this.optedOutAt,
    this.whatsAppCheck = 'unchecked',
  });

  factory Contact.fromJson(Json json) => Contact(
        id: json['id'] as String,
        phoneNumber: json['phoneNumber'] as String? ?? '',
        savedName: json['savedName'] as String?,
        nameEn: json['nameEn'] as String?,
        nameHi: json['nameHi'] as String?,
        nameGu: json['nameGu'] as String?,
        language: json['language'] as String? ?? 'en',
        categoryId: json['categoryId'] as String?,
        salutation: json['salutation'] as String?,
        birthday: json['birthday'] == null ? null : Birthday.fromJson(json['birthday'] as Json),
        notes: json['notes'] as String?,
        groups: asList(json['groups']).map((Json g) => (id: g['id'] as String, name: g['name'] as String? ?? '')).toList(),
        needsDetails: asBool(json['needsDetails']),
        optedOutAt: asDateOrNull(json['optedOutAt']),
        whatsAppCheck: json['whatsAppCheck'] as String? ?? 'unchecked',
      );

  final String id;
  final String phoneNumber;
  final String? savedName;
  final String? nameEn;
  final String? nameHi;
  final String? nameGu;
  final String language;
  final String? categoryId;
  final String? salutation;
  final Birthday? birthday;
  final String? notes;
  final List<({String id, String name})> groups;
  final bool needsDetails;

  /// Set when the person does not want broadcasts: broadcasts skip them.
  final DateTime? optedOutAt;

  bool get optedOut => optedOutAt != null;

  /// "unchecked" (saved before the check existed), "pending" (checked automatically while WhatsApp is
  /// connected), "on_whatsapp" or "not_on_whatsapp".
  final String whatsAppCheck;

  /// Not on WhatsApp: kept in its own list, cannot be in groups and gets no messages.
  bool get notOnWhatsApp => whatsAppCheck == 'not_on_whatsapp';

  bool get waitingForCheck => whatsAppCheck == 'pending';

  bool get neverChecked => whatsAppCheck == 'unchecked';

  /// The real name in the person's own language.
  String? get realName => switch (language) {
        'hi' => nameHi,
        'gu' => nameGu,
        _ => nameEn,
      };

  /// The name a person is shown by in lists: the saved name, else the real name (same rule as the website).
  String get displayName => savedName ?? realName ?? nameEn ?? nameGu ?? nameHi ?? phoneNumber;
}

class ContactInput {
  const ContactInput({
    required this.phoneNumber,
    required this.savedName,
    required this.nameEn,
    required this.nameHi,
    required this.nameGu,
    required this.language,
    required this.categoryId,
    required this.salutation,
    required this.birthday,
    required this.notes,
    required this.groupIds,
  });

  final String phoneNumber;
  final String? savedName;
  final String? nameEn;
  final String? nameHi;
  final String? nameGu;
  final String language;
  final String? categoryId;
  final String? salutation;
  final Birthday? birthday;
  final String? notes;
  final List<String> groupIds;

  Json toJson() => <String, dynamic>{
        'phoneNumber': phoneNumber,
        'savedName': savedName,
        'nameEn': nameEn,
        'nameHi': nameHi,
        'nameGu': nameGu,
        'language': language,
        'categoryId': categoryId,
        'salutation': salutation,
        'birthday': birthday?.toJson(),
        'notes': notes,
        'groupIds': groupIds,
      };
}

class ContactPage {
  const ContactPage({required this.items, required this.total});

  factory ContactPage.fromJson(Json json) =>
      ContactPage(items: asList(json['items']).map(Contact.fromJson).toList(), total: asInt(json['total']));

  final List<Contact> items;
  final int total;
}

/// How many of the user's contacts are on WhatsApp, not on it, waiting for the check, or never checked.
class WhatsAppCheckSummary {
  const WhatsAppCheckSummary({
    required this.unchecked,
    required this.pending,
    required this.onWhatsApp,
    required this.notOnWhatsApp,
    required this.connected,
  });

  factory WhatsAppCheckSummary.fromJson(Json json) => WhatsAppCheckSummary(
        unchecked: asInt(json['unchecked']),
        pending: asInt(json['pending']),
        onWhatsApp: asInt(json['onWhatsApp']),
        notOnWhatsApp: asInt(json['notOnWhatsApp']),
        connected: asBool(json['connected']),
      );

  static const WhatsAppCheckSummary empty =
      WhatsAppCheckSummary(unchecked: 0, pending: 0, onWhatsApp: 0, notOnWhatsApp: 0, connected: false);

  /// Saved before the check existed: checked only when the user presses "Check numbers".
  final int unchecked;

  /// Being checked now (or as soon as WhatsApp connects).
  final int pending;
  final int onWhatsApp;
  final int notOnWhatsApp;

  /// The user's WhatsApp is connected, so waiting numbers are being checked.
  final bool connected;
}
