import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';

final Provider<AdminApi> adminApiProvider = Provider<AdminApi>((Ref ref) => AdminApi(ref.watch(apiClientProvider)));

/// Shapes of /api/v1/users, /roles and /permissions (same as the website's settings.models.ts).

class Permission {
  const Permission({required this.name, required this.group, required this.description});

  factory Permission.fromJson(Json json) => Permission(
        name: json['name'] as String,
        group: json['group'] as String? ?? '',
        description: json['description'] as String? ?? '',
      );

  final String name;
  final String group;
  final String description;
}

class Role {
  const Role({
    required this.id,
    required this.name,
    required this.description,
    required this.isSystem,
    required this.permissions,
    required this.userCount,
  });

  factory Role.fromJson(Json json) => Role(
        id: json['id'] as String,
        name: json['name'] as String? ?? '',
        description: json['description'] as String?,
        isSystem: asBool(json['isSystem']),
        permissions: asStrings(json['permissions']),
        userCount: asInt(json['userCount']),
      );

  final String id;
  final String name;
  final String? description;
  final bool isSystem;
  final List<String> permissions;
  final int userCount;
}

class UserDetails {
  const UserDetails({
    required this.id,
    required this.userName,
    required this.displayName,
    required this.isActive,
    required this.roles,
  });

  factory UserDetails.fromJson(Json json) => UserDetails(
        id: json['id'] as String,
        userName: json['userName'] as String? ?? '',
        displayName: json['displayName'] as String? ?? '',
        isActive: asBool(json['isActive']),
        roles: asList(json['roles']).map((Json r) => (id: r['id'] as String, name: r['name'] as String? ?? '')).toList(),
      );

  final String id;
  final String userName;
  final String displayName;
  final bool isActive;
  final List<({String id, String name})> roles;
}

class AdminApi {
  const AdminApi(this._api);

  final ApiClient _api;

  Future<List<Permission>> permissions() async =>
      asList(await _api.get('/api/v1/permissions')).map(Permission.fromJson).toList();

  Future<List<Role>> roles() async => asList(await _api.get('/api/v1/roles')).map(Role.fromJson).toList();

  Future<void> saveRole(String? id, {required String name, String? description, required List<String> permissions}) {
    final Json body = <String, dynamic>{'name': name, 'description': description, 'permissions': permissions};
    return id == null ? _api.post('/api/v1/roles', body) : _api.put('/api/v1/roles/$id', body);
  }

  Future<void> deleteRole(String id) => _api.delete('/api/v1/roles/$id');

  Future<List<UserDetails>> users() async => asList(await _api.get('/api/v1/users')).map(UserDetails.fromJson).toList();

  Future<void> createUser({
    required String userName,
    required String displayName,
    required String password,
    required List<String> roleIds,
  }) =>
      _api.post('/api/v1/users', <String, dynamic>{
        'userName': userName,
        'displayName': displayName,
        'password': password,
        'roleIds': roleIds,
      });

  Future<void> setUserRoles(String id, List<String> roleIds) =>
      _api.put('/api/v1/users/$id/roles', <String, dynamic>{'roleIds': roleIds});

  /// Deletes the account and everything that is theirs (contacts, messages, linked WhatsApp). Cannot be undone.
  Future<void> deleteUser(String id) => _api.delete('/api/v1/users/$id');

  Future<void> setUserActive(String id, {required bool isActive}) =>
      _api.put('/api/v1/users/$id/active', <String, dynamic>{'isActive': isActive});
}
