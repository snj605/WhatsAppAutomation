import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../auth/session_controller.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../files/files_api.dart';
import 'contact_models.dart';

final Provider<ContactsApi> contactsApiProvider =
    Provider<ContactsApi>((Ref ref) => ContactsApi(ref.watch(apiClientProvider)));

/// One row of an import preview or result.
class ImportRow {
  const ImportRow({
    required this.row,
    required this.phoneNumber,
    required this.name,
    required this.category,
    required this.action,
    required this.errors,
  });

  factory ImportRow.fromJson(Json json) => ImportRow(
        row: asInt(json['row']),
        phoneNumber: json['phoneNumber'] as String?,
        name: json['name'] as String?,
        category: json['category'] as String?,
        action: json['action'] as String? ?? 'error',
        errors: asStrings(json['errors']),
      );

  final int row;
  final String? phoneNumber;
  final String? name;
  final String? category;

  /// create, update or error.
  final String action;
  final List<String> errors;
}

class ImportResult {
  const ImportResult({
    required this.applied,
    required this.created,
    required this.updated,
    required this.failed,
    required this.newCategories,
    required this.newGroups,
    required this.rows,
  });

  factory ImportResult.fromJson(Json json) => ImportResult(
        applied: asBool(json['applied']),
        created: asInt(json['created']),
        updated: asInt(json['updated']),
        failed: asInt(json['failed']),
        newCategories: asStrings(json['newCategories']),
        newGroups: asStrings(json['newGroups']),
        rows: asList(json['rows']).map(ImportRow.fromJson).toList(),
      );

  final bool applied;
  final int created;
  final int updated;
  final int failed;
  final List<String> newCategories;
  final List<String> newGroups;
  final List<ImportRow> rows;
}

/// Calls to the signed-in user's own contacts, categories and groups (same as the website's contacts.api.ts).
class ContactsApi {
  const ContactsApi(this._api);

  final ApiClient _api;

  Future<List<Category>> categories() async =>
      asList(await _api.get('/api/v1/categories')).map(Category.fromJson).toList();

  Future<void> saveCategory(String? id, String name, String color) => id == null
      ? _api.post('/api/v1/categories', <String, dynamic>{'name': name, 'color': color})
      : _api.put('/api/v1/categories/$id', <String, dynamic>{'name': name, 'color': color});

  Future<void> deleteCategory(String id) => _api.delete('/api/v1/categories/$id');

  Future<List<Group>> groups() async => asList(await _api.get('/api/v1/groups')).map(Group.fromJson).toList();

  Future<void> saveGroup(String? id, String name) => id == null
      ? _api.post('/api/v1/groups', <String, dynamic>{'name': name})
      : _api.put('/api/v1/groups/$id', <String, dynamic>{'name': name});

  Future<void> deleteGroup(String id) => _api.delete('/api/v1/groups/$id');

  Future<void> changeGroupMembers(String groupId, List<String> contactIds, {required bool add}) =>
      _api.post('/api/v1/groups/$groupId/members', <String, dynamic>{'contactIds': contactIds, 'add': add});

  Future<void> changeCategoryMembers(String categoryId, List<String> contactIds, {required bool add}) =>
      _api.post('/api/v1/categories/$categoryId/members', <String, dynamic>{'contactIds': contactIds, 'add': add});

  Future<ContactPage> contacts({
    String? search,
    String? categoryId,
    String? groupId,
    bool needsDetails = false,
    int page = 1,
    int pageSize = 25,
    String? whatsApp,
    String? fieldId,
    String? fieldValue,
  }) async {
    final Map<String, String> query = <String, String>{'page': '$page', 'pageSize': '$pageSize'};
    // Without a filter the server leaves out numbers that are not on WhatsApp (they have their own list).
    if (whatsApp != null) {
      query['whatsApp'] = whatsApp;
    }

    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }

    if (categoryId != null) {
      query['categoryId'] = categoryId;
    }

    if (groupId != null) {
      query['groupId'] = groupId;
    }

    if (needsDetails) {
      query['needsDetails'] = 'true';
    }

    // Only people with a value in this field (text: containing fieldValue; other types: equal to it).
    if (fieldId != null) {
      query['fieldId'] = fieldId;
      if (fieldValue != null && fieldValue.trim().isNotEmpty) {
        query['fieldValue'] = fieldValue.trim();
      }
    }

    return ContactPage.fromJson((await _api.get('/api/v1/contacts', query: query))! as Json);
  }

  Future<Contact> saveContact(String? id, ContactInput input) async => Contact.fromJson(
        (id == null
            ? await _api.post('/api/v1/contacts', input.toJson())
            : await _api.put('/api/v1/contacts/$id', input.toJson()))! as Json,
      );

  Future<WhatsAppCheckSummary> whatsAppCheck() async =>
      WhatsAppCheckSummary.fromJson((await _api.get('/api/v1/contacts/whatsapp-check'))! as Json);

  /// Queues numbers for the WhatsApp check: all never-checked contacts, or these contacts (check again).
  Future<WhatsAppCheckSummary> requestWhatsAppCheck([List<String>? contactIds]) async => WhatsAppCheckSummary.fromJson(
        (await _api.post('/api/v1/contacts/whatsapp-check', <String, dynamic>{'contactIds': contactIds}))! as Json,
      );

  Future<Contact> setOptOut(String id, {required bool optedOut}) async =>
      Contact.fromJson((await _api.put('/api/v1/contacts/$id/opt-out', <String, dynamic>{'optedOut': optedOut}))! as Json);

  Future<int> deleteContacts(List<String> ids) async =>
      asInt(((await _api.post('/api/v1/contacts/delete', <String, dynamic>{'ids': ids}))! as Json)['deleted']);

  /// Reads an Excel or CSV file. With [apply] false nothing is saved: the result is a preview.
  Future<ImportResult> importFile(PickedFile file, {required bool apply}) async => ImportResult.fromJson(
        (await _api.upload(
          '/api/v1/contacts/import',
          bytes: file.bytes,
          filePath: file.path,
          fileName: file.name,
          contentType: contentTypeFor(file.extension),
          query: <String, String>{'apply': '$apply'},
        ))! as Json,
      );

  Future<String> defaultLanguage() async =>
      ((await _api.get('/api/v1/preferences'))! as Json)['defaultLanguage'] as String? ?? 'en';

  Future<void> setDefaultLanguage(String language) =>
      _api.put('/api/v1/preferences', <String, dynamic>{'defaultLanguage': language});
}

/// The user's own contact fields (/api/v1/contact-fields): the contact form, the field filter and {{key}} in messages.
class ContactFieldsApi {
  ContactFieldsApi(this._api);

  final ApiClient _api;

  Future<List<ContactField>> list() async => asList(await _api.get('/api/v1/contact-fields')).map(ContactField.fromJson).toList();

  Future<ContactField> create(String name, String type, List<String> options) async => ContactField.fromJson(
        (await _api.post('/api/v1/contact-fields', <String, dynamic>{'name': name, 'type': type, 'options': options}))! as Json,
      );

  /// The type cannot change (values are stored in its form); the name and choices can.
  Future<ContactField> update(String id, String name, List<String> options) async => ContactField.fromJson(
        (await _api.put('/api/v1/contact-fields/$id', <String, dynamic>{'name': name, 'options': options}))! as Json,
      );

  Future<void> remove(String id) => _api.delete('/api/v1/contact-fields/$id');
}

final Provider<ContactFieldsApi> contactFieldsApiProvider =
    Provider<ContactFieldsApi>((Ref ref) => ContactFieldsApi(ref.watch(apiClientProvider)));

/// The signed-in user's contact fields, reloaded after each change.
final AsyncNotifierProvider<ContactFieldsController, List<ContactField>> contactFieldsProvider =
    AsyncNotifierProvider<ContactFieldsController, List<ContactField>>(ContactFieldsController.new);

class ContactFieldsController extends AsyncNotifier<List<ContactField>> {
  @override
  Future<List<ContactField>> build() {
    ref.watch(sessionProvider);
    return ref.read(contactFieldsApiProvider).list();
  }

  Future<void> reload() async {
    final AsyncValue<List<ContactField>> next = await AsyncValue.guard(() => ref.read(contactFieldsApiProvider).list());
    if (ref.mounted) {
      state = next;
    }
  }
}
