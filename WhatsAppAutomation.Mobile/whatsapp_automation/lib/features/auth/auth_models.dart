/// The three kinds of user, as the server sends them ("platform_admin", "tenant_admin", "member").
enum UserKind {
  platformAdmin('platform_admin', 'Platform admin'),
  tenantAdmin('tenant_admin', 'Admin'),
  member('member', 'Member');

  const UserKind(this.code, this.label);

  final String code;
  final String label;

  static UserKind parse(String? code) =>
      UserKind.values.firstWhere((UserKind k) => k.code == code, orElse: () => UserKind.member);
}

class AppUser {
  const AppUser({
    required this.id,
    required this.userName,
    required this.displayName,
    required this.kind,
    required this.tenantName,
    required this.mustChangePassword,
  });

  factory AppUser.fromJson(Map<String, dynamic> json) => AppUser(
        id: json['id'] as String,
        userName: json['userName'] as String,
        displayName: json['displayName'] as String,
        kind: UserKind.parse(json['kind'] as String?),
        tenantName: json['tenantName'] as String?,
        mustChangePassword: json['mustChangePassword'] as bool? ?? false,
      );

  final String id;
  final String userName;
  final String displayName;
  final UserKind kind;

  /// Null for the platform admin.
  final String? tenantName;

  /// An admin chose the password: nothing else works until the user chooses their own.
  final bool mustChangePassword;

  /// "Admin · Bhakti Enterprise", "Member · …" or "Platform admin".
  String get kindLine => tenantName == null ? kind.label : '${kind.label} · $tenantName';
}

/// Where the app is: starting, signed out, locked behind fingerprint/face, or signed in.
sealed class SessionState {
  const SessionState();
}

class SessionStarting extends SessionState {
  const SessionStarting();
}

/// The saved session could not be checked (no internet). The user can try again.
class SessionUnreachable extends SessionState {
  const SessionUnreachable(this.message);

  final String message;
}

class SignedOut extends SessionState {
  const SignedOut({this.message});

  final String? message;
}

class Locked extends SessionState {
  const Locked(this.userName);

  final String userName;
}

class SignedIn extends SessionState {
  const SignedIn({required this.user, required this.permissions});

  final AppUser user;
  final List<String> permissions;

  bool can(String permission) => permissions.contains(permission);
}
