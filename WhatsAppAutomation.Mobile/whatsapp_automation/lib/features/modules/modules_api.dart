import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../auth/session_controller.dart';

final Provider<ModulesApi> modulesApiProvider = Provider<ModulesApi>((Ref ref) => ModulesApi(ref.watch(apiClientProvider)));

/// One occasion module of the user (GET /api/v1/modules). Same as the website's modules.api.ts.
class OccasionModule {
  const OccasionModule({
    required this.id,
    required this.name,
    required this.kind,
    required this.forNameLabel,
    required this.autoSend,
    required this.time,
    required this.templateId,
    required this.templateName,
    required this.nextRunAt,
    required this.todayCount,
    required this.timeZone,
  });

  factory OccasionModule.fromJson(Json json) => OccasionModule(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        kind: json['kind'] as String? ?? 'yearly',
        forNameLabel: asStringOrNull(json['forNameLabel']),
        autoSend: asBool(json['autoSend']),
        time: asStringOrNull(json['time']),
        templateId: asStringOrNull(json['templateId']),
        templateName: asStringOrNull(json['templateName']),
        nextRunAt: asDateOrNull(json['nextRunAt']),
        todayCount: asInt(json['todayCount']),
        timeZone: json['timeZone'] as String? ?? '',
      );

  final String id;
  final String name;

  /// "birthday", "yearly" or "once".
  final String kind;

  /// When set, each date has a second name with this label, used in messages as {{for_name}}.
  final String? forNameLabel;
  final bool autoSend;

  /// "HH:mm".
  final String? time;
  final String? templateId;
  final String? templateName;
  final DateTime? nextRunAt;
  final int todayCount;
  final String timeZone;

  bool get ready => autoSend && nextRunAt != null;

  String get kindText => switch (kind) {
        'birthday' => 'From each contact’s birthday',
        'yearly' => 'Every year on the same date',
        _ => 'One time, on a date with a year',
      };
}

/// One person with the module's date in the coming days.
class ModuleDayPerson {
  const ModuleDayPerson({
    required this.contactId,
    required this.name,
    required this.phoneNumber,
    required this.daysUntil,
    required this.years,
    required this.forName,
    required this.optedOut,
    required this.status,
    required this.sentAt,
    required this.deliveredAt,
  });

  factory ModuleDayPerson.fromJson(Json json) => ModuleDayPerson(
        contactId: json['contactId'] as String,
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        daysUntil: asInt(json['daysUntil']),
        years: asIntOrNull(json['years']),
        forName: asStringOrNull(json['forName']),
        optedOut: asBool(json['optedOut']),
        status: asStringOrNull(json['status']),
        sentAt: asDateOrNull(json['sentAt']),
        deliveredAt: asDateOrNull(json['deliveredAt']),
      );

  final String contactId;
  final String name;
  final String phoneNumber;
  final int daysUntil;
  final int? years;
  final String? forName;
  final bool optedOut;

  /// Today only: not_sent, pending, sent, delivered, read, failed or skipped.
  final String? status;
  final DateTime? sentAt;
  final DateTime? deliveredAt;
}

/// Who has which date in a Yearly or Once module.
class ModuleEntry {
  const ModuleEntry({
    required this.contactId,
    required this.name,
    required this.phoneNumber,
    required this.day,
    required this.month,
    required this.year,
    required this.forName,
  });

  factory ModuleEntry.fromJson(Json json) => ModuleEntry(
        contactId: json['contactId'] as String,
        name: json['name'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        day: asInt(json['day']),
        month: asInt(json['month']),
        year: asIntOrNull(json['year']),
        forName: asStringOrNull(json['forName']),
      );

  final String contactId;
  final String name;
  final String phoneNumber;
  final int day;
  final int month;
  final int? year;
  final String? forName;
}

class ModulesApi {
  const ModulesApi(this._api);

  final ApiClient _api;

  Future<List<OccasionModule>> list() async => asList(await _api.get('/api/v1/modules')).map(OccasionModule.fromJson).toList();

  Future<OccasionModule> create(String name, String kind, String? forNameLabel) async => OccasionModule.fromJson(
        (await _api.post('/api/v1/modules', <String, dynamic>{'name': name, 'kind': kind, 'forNameLabel': forNameLabel}))! as Json,
      );

  Future<OccasionModule> update(String id, String name, String? forNameLabel) async => OccasionModule.fromJson(
        (await _api.put('/api/v1/modules/$id', <String, dynamic>{'name': name, 'forNameLabel': forNameLabel}))! as Json,
      );

  Future<void> remove(String id) => _api.delete('/api/v1/modules/$id');

  Future<OccasionModule> setSending(String id, {required bool enabled, String? time, String? templateId}) async =>
      OccasionModule.fromJson((await _api.put('/api/v1/modules/$id/sending', <String, dynamic>{
        'enabled': enabled,
        'time': time,
        'templateId': templateId,
      }))! as Json);

  Future<List<ModuleDayPerson>> days(String id, {int days = 7}) async =>
      asList(await _api.get('/api/v1/modules/$id/days', query: <String, String>{'days': '$days'}))
          .map(ModuleDayPerson.fromJson)
          .toList();

  Future<({List<ModuleEntry> items, int total})> entries(String id, {String search = '', int page = 1, int pageSize = 50}) async {
    final Json json = (await _api.get('/api/v1/modules/$id/entries',
        query: <String, String>{'search': search, 'page': '$page', 'pageSize': '$pageSize'}))! as Json;
    return (items: asList(json['items']).map(ModuleEntry.fromJson).toList(), total: asInt(json['total']));
  }

  Future<void> setEntry(String id, String contactId, {required int day, required int month, int? year, String? forName}) =>
      _api.put('/api/v1/modules/$id/entries/$contactId', <String, dynamic>{
        'day': day,
        'month': month,
        'year': year,
        'forName': forName,
      });

  Future<void> removeEntry(String id, String contactId) => _api.delete('/api/v1/modules/$id/entries/$contactId');
}

/// The signed-in user's modules for the menu, reloaded after each change.
final AsyncNotifierProvider<ModulesController, List<OccasionModule>> modulesProvider =
    AsyncNotifierProvider<ModulesController, List<OccasionModule>>(ModulesController.new);

class ModulesController extends AsyncNotifier<List<OccasionModule>> {
  @override
  Future<List<OccasionModule>> build() {
    // A new sign-in (another user) starts again.
    ref.watch(sessionProvider);
    return ref.read(modulesApiProvider).list();
  }

  Future<void> reload() async {
    state = await AsyncValue.guard(() => ref.read(modulesApiProvider).list());
  }
}

/// An icon for a module, by its kind or name (same rule as the website).
IconData moduleIcon(OccasionModule m) {
  if (m.kind == 'birthday') {
    return FLucideIcons.cake;
  }

  final String name = m.name.toLowerCase();
  if (RegExp('asthi|shraddh|punya|memor|death|remember').hasMatch(name)) {
    return FLucideIcons.sun;
  }

  if (RegExp('anniv|marri|wedding').hasMatch(name)) {
    return FLucideIcons.heart;
  }

  return m.kind == 'once' ? FLucideIcons.calendar : FLucideIcons.gift;
}

/// Ready-made wish when the module has no template (same as the website).
String defaultWish(OccasionModule m) {
  if (m.kind == 'birthday') {
    return '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.';
  }

  return m.forNameLabel != null
      ? 'Dear {{name}}, remembering {{for_name}} today. 🙏'
      : 'Dear {{name}}, warm wishes on your ${m.name.toLowerCase()}! 🎉';
}
