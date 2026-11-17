class AppUser {
  const AppUser({required this.id, required this.userName, required this.displayName, required this.roles});

  factory AppUser.fromJson(Map<String, dynamic> json) => AppUser(
        id: json['id'] as String,
        userName: json['userName'] as String,
        displayName: json['displayName'] as String,
        roles: (json['roles'] as List<dynamic>? ?? const <dynamic>[]).cast<String>(),
      );

  final String id;
  final String userName;
  final String displayName;
  final List<String> roles;
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
