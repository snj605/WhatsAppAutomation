import 'dart:convert';

/// An error the user can read. [code] is the server's stable error id, such as "whatsapp.already_linked".
class ApiException implements Exception {
  const ApiException(this.message, {this.status, this.code});

  final String message;
  final int? status;
  final String? code;

  bool get isNetwork => status == null;

  @override
  String toString() => message;
}

const ApiException networkError = ApiException(
  'Cannot reach the server. Check the internet connection and try again.',
);

/// Turns an error response from the API (a "problem details" JSON) into a message for the user.
ApiException apiErrorFrom(int status, String body) {
  String? message;
  String? code;
  try {
    final Object? json = jsonDecode(body);
    if (json is Map<String, dynamic>) {
      code = json['code'] as String?;
      final Object? errors = json['errors'];
      if (errors is Map<String, dynamic> && errors.isNotEmpty) {
        // Field errors: show the first message of each field.
        message = errors.values
            .map((Object? v) => v is List && v.isNotEmpty ? v.first.toString() : v.toString())
            .join(' ');
      } else {
        message = (json['detail'] ?? json['title']) as String?;
      }
    }
  } on FormatException {
    // Not JSON: fall through to the general message.
  }

  return ApiException(message ?? _fallback(status), status: status, code: code);
}

String _fallback(int status) => switch (status) {
      401 => 'Please sign in again.',
      403 => 'You are not allowed to do this.',
      404 => 'This was not found.',
      429 => 'Too many tries. Wait a minute and try again.',
      >= 500 => 'The server has a problem. Try again in a moment.',
      _ => 'Something went wrong. Try again.',
    };
