/// Small helpers for reading the API's JSON safely.
typedef Json = Map<String, dynamic>;

int asInt(Object? value) => (value as num?)?.toInt() ?? 0;

int? asIntOrNull(Object? value) => (value as num?)?.toInt();

bool asBool(Object? value) => value as bool? ?? false;

String? asStringOrNull(Object? value) => value as String?;

DateTime? asDateOrNull(Object? value) => value == null ? null : DateTime.parse(value as String).toLocal();

List<Json> asList(Object? value) => (value as List<dynamic>? ?? const <dynamic>[]).cast<Json>();

List<String> asStrings(Object? value) => (value as List<dynamic>? ?? const <dynamic>[]).cast<String>();
