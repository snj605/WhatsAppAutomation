/// The signed-in user's WhatsApp number, as the server reports it.
class WhatsAppAccount {
  const WhatsAppAccount({required this.status, this.statusReason, this.phoneNumber, this.profileName});

  factory WhatsAppAccount.fromJson(Map<String, dynamic> json) => WhatsAppAccount(
        status: json['status'] as String? ?? 'not_linked',
        statusReason: json['statusReason'] as String?,
        phoneNumber: json['phoneNumber'] as String?,
        profileName: json['profileName'] as String?,
      );

  /// not_linked, linking, connecting, connected, reconnecting, logged_out, restricted, disconnected.
  final String status;
  final String? statusReason;
  final String? phoneNumber;
  final String? profileName;

  bool get isConnected => status == 'connected';

  /// A number is linked, whether or not the connection is open right now.
  bool get isLinked =>
      status == 'connected' || status == 'connecting' || status == 'reconnecting' || status == 'disconnected';

  /// Still changing by itself: worth asking the server again soon.
  bool get isSettling => status == 'linking' || status == 'connecting' || status == 'reconnecting';

  String get label => switch (status) {
        'connected' => 'Connected',
        'linking' => 'Waiting for you to enter the code in WhatsApp',
        'connecting' => 'Connecting…',
        'reconnecting' => 'Reconnecting…',
        'disconnected' => 'Stopped: another copy took this login. Tap Reconnect.',
        'logged_out' => 'Signed out from WhatsApp. Link again.',
        'restricted' => 'WhatsApp restricted this number',
        _ => 'Not linked',
      };
}

const Object _keep = Object();

class WhatsAppState {
  const WhatsAppState({this.account, this.pairingCode, this.loading = false, this.busy = false, this.error});

  final WhatsAppAccount? account;

  /// The 8-character code to type into WhatsApp, while linking.
  final String? pairingCode;

  /// First load of the status.
  final bool loading;

  /// A link or unlink request is running.
  final bool busy;
  final String? error;

  WhatsAppState copyWith({
    Object? account = _keep,
    Object? pairingCode = _keep,
    bool? loading,
    bool? busy,
    Object? error = _keep,
  }) =>
      WhatsAppState(
        account: identical(account, _keep) ? this.account : account as WhatsAppAccount?,
        pairingCode: identical(pairingCode, _keep) ? this.pairingCode : pairingCode as String?,
        loading: loading ?? this.loading,
        busy: busy ?? this.busy,
        error: identical(error, _keep) ? this.error : error as String?,
      );
}

/// Returns the number in international form (+919825012345), or null when it is not usable.
String? normalizePhone(String input) {
  final String digits = input.replaceAll(RegExp(r'[\s\-()]'), '');
  return RegExp(r'^\+[1-9]\d{7,14}$').hasMatch(digits) ? digits : null;
}

/// "ABCD1234" is easier to read and type as "ABCD-1234".
String formatPairingCode(String code) =>
    code.length == 8 ? '${code.substring(0, 4)}-${code.substring(4)}' : code;
