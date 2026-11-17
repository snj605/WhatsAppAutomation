import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';

final Provider<AdminApi> adminApiProvider = Provider<AdminApi>((Ref ref) => AdminApi(ref.watch(apiClientProvider)));

/// A person of the tenant (same as the website's members.models.ts).
class Member {
  const Member({
    required this.id,
    required this.userName,
    required this.displayName,
    required this.isAdmin,
    required this.isActive,
    required this.switches,
    required this.whatsAppStatus,
    required this.whatsAppNumber,
  });

  factory Member.fromJson(Json json) => Member(
        id: json['id'] as String,
        userName: json['userName'] as String? ?? '',
        displayName: json['displayName'] as String? ?? '',
        isAdmin: json['kind'] == 'tenant_admin',
        isActive: asBool(json['isActive']),
        switches: asStrings(json['switches']),
        whatsAppStatus: json['whatsAppStatus'] as String? ?? 'not_linked',
        whatsAppNumber: json['whatsAppNumber'] as String?,
      );

  final String id;
  final String userName;
  final String displayName;
  final bool isAdmin;
  final bool isActive;
  final List<String> switches;

  /// Like GET /api/v1/whatsapp: "connected", "not_linked", …
  final String whatsAppStatus;
  final String? whatsAppNumber;
}

/// A tenant as the platform admin sees it: counts only.
class Tenant {
  const Tenant({
    required this.id,
    required this.name,
    required this.isActive,
    required this.users,
    required this.admins,
  });

  factory Tenant.fromJson(Json json) => Tenant(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        isActive: asBool(json['isActive']),
        users: asInt(json['users']),
        admins: asInt(json['admins']),
      );

  final String id;
  final String name;
  final bool isActive;
  final int users;
  final int admins;
}

class TenantAdmin {
  const TenantAdmin({required this.id, required this.userName, required this.displayName, required this.isActive});

  factory TenantAdmin.fromJson(Json json) => TenantAdmin(
        id: json['id'] as String,
        userName: json['userName'] as String? ?? '',
        displayName: json['displayName'] as String? ?? '',
        isActive: asBool(json['isActive']),
      );

  final String id;
  final String userName;
  final String displayName;
  final bool isActive;
}

/// /api/v1/members (tenant admin) and /api/v1/platform/tenants (platform admin).
class AdminApi {
  const AdminApi(this._api);

  final ApiClient _api;

  static const String _tenants = '/api/v1/platform/tenants';

  Future<List<Member>> members() async => asList(await _api.get('/api/v1/members')).map(Member.fromJson).toList();

  Future<void> createMember({
    required String userName,
    required String displayName,
    required String password,
    required bool isAdmin,
    required List<String> switches,
  }) =>
      _api.post('/api/v1/members', <String, dynamic>{
        'userName': userName,
        'displayName': displayName,
        'password': password,
        'isAdmin': isAdmin,
        'switches': switches,
      });

  Future<void> updateMember(String id, {required String displayName, required bool isAdmin, required List<String> switches}) =>
      _api.put('/api/v1/members/$id', <String, dynamic>{'displayName': displayName, 'isAdmin': isAdmin, 'switches': switches});

  Future<void> setMemberActive(String id, {required bool isActive}) =>
      _api.put('/api/v1/members/$id/active', <String, dynamic>{'isActive': isActive});

  /// They are signed out and choose their own password at the next sign-in.
  Future<void> resetMemberPassword(String id, String password) =>
      _api.put('/api/v1/members/$id/password', <String, dynamic>{'password': password});

  /// Deletes the person and everything that is theirs. Cannot be undone.
  Future<void> deleteMember(String id) => _api.delete('/api/v1/members/$id');

  Future<List<Tenant>> tenants() async => asList(await _api.get(_tenants)).map(Tenant.fromJson).toList();

  Future<void> createTenant({
    required String name,
    required String adminUserName,
    required String adminDisplayName,
    required String adminPassword,
  }) =>
      _api.post(_tenants, <String, dynamic>{
        'name': name,
        'adminUserName': adminUserName,
        'adminDisplayName': adminDisplayName,
        'adminPassword': adminPassword,
      });

  Future<void> renameTenant(String id, String name) => _api.put('$_tenants/$id', <String, dynamic>{'name': name});

  Future<void> setTenantActive(String id, {required bool isActive}) =>
      _api.put('$_tenants/$id/active', <String, dynamic>{'isActive': isActive});

  /// Only a turned-off tenant; removes all its people and their data.
  Future<void> deleteTenant(String id) => _api.delete('$_tenants/$id');

  Future<List<TenantAdmin>> tenantAdmins(String id) async =>
      asList(await _api.get('$_tenants/$id/admins')).map(TenantAdmin.fromJson).toList();

  Future<void> addTenantAdmin(String id, {required String userName, required String displayName, required String password}) =>
      _api.post('$_tenants/$id/admins', <String, dynamic>{'userName': userName, 'displayName': displayName, 'password': password});

  Future<void> setTenantAdminActive(String id, String adminId, {required bool isActive}) =>
      _api.put('$_tenants/$id/admins/$adminId/active', <String, dynamic>{'isActive': isActive});

  Future<void> resetTenantAdminPassword(String id, String adminId, String password) =>
      _api.put('$_tenants/$id/admins/$adminId/password', <String, dynamic>{'password': password});

  Future<void> deleteTenantAdmin(String id, String adminId) => _api.delete('$_tenants/$id/admins/$adminId');
}
