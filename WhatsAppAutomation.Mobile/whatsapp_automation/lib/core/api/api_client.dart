import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart' show MediaType;

import '../../features/auth/auth_service.dart';
import '../config.dart';
import 'api_error.dart';

/// Every call to the API after sign-in goes through here: it adds the access token, renews it
/// when needed, and turns error responses into messages for the user.
class ApiClient {
  ApiClient(this._auth, this._http, {this.baseUrl = AppConfig.apiBaseUrl});

  static const Duration _timeout = Duration(seconds: 30);

  final AuthService _auth;
  final http.Client _http;
  final String baseUrl;

  Future<Object?> get(String path, {Map<String, String>? query}) => _send('GET', path, query: query);

  Future<Object?> post(String path, [Object? body]) => _send('POST', path, body: body);

  Future<Object?> put(String path, [Object? body]) => _send('PUT', path, body: body);

  Future<Object?> delete(String path) => _send('DELETE', path);

  /// Downloads a file (for example the import template) as raw bytes.
  Future<List<int>> getBytes(String path) async {
    final Uri uri = Uri.parse('$baseUrl$path');
    const ApiException signedOut = ApiException('Please sign in again.', status: 401);
    final String? token = await _guarded<String?>(_auth.accessToken);
    if (token == null) {
      throw signedOut;
    }

    http.Response response = await _once('GET', uri, token, null);
    if (response.statusCode == 401) {
      final String? renewed =
          await _guarded<bool>(_auth.refresh) ? await _guarded<String?>(_auth.accessToken) : null;
      if (renewed == null) {
        throw signedOut;
      }

      response = await _once('GET', uri, renewed, null);
    }

    if (response.statusCode >= 400) {
      throw apiErrorFrom(response.statusCode, response.body);
    }

    return response.bodyBytes;
  }

  /// Sends one file as multipart form data in the field "file", like the website's uploads. With [filePath] the
  /// file is streamed from the phone (big videos are not read into memory); otherwise [bytes] are sent.
  /// Uploads get much more time than other calls: a big file on a slow phone connection takes a while.
  Future<Object?> upload(
    String path, {
    required List<int> bytes,
    String? filePath,
    required String fileName,
    String? contentType,
    Map<String, String>? query,
  }) async {
    final Uri uri = Uri.parse('$baseUrl$path').replace(queryParameters: query);
    const ApiException signedOut = ApiException('Please sign in again.', status: 401);
    final String? token = await _guarded<String?>(_auth.accessToken);
    if (token == null) {
      throw signedOut;
    }

    http.Response response = await _uploadOnce(uri, token, bytes, filePath, fileName, contentType);
    if (response.statusCode == 401) {
      final String? renewed =
          await _guarded<bool>(_auth.refresh) ? await _guarded<String?>(_auth.accessToken) : null;
      if (renewed == null) {
        throw signedOut;
      }

      response = await _uploadOnce(uri, renewed, bytes, filePath, fileName, contentType);
    }

    if (response.statusCode >= 400) {
      throw apiErrorFrom(response.statusCode, response.body);
    }

    return response.body.isEmpty ? null : jsonDecode(response.body);
  }

  Future<http.Response> _uploadOnce(
    Uri uri,
    String token,
    List<int> bytes,
    String? filePath,
    String fileName,
    String? contentType,
  ) {
    final MediaType? type = contentType == null ? null : MediaType.parse(contentType);
    return _guarded(() async {
      final http.MultipartRequest request = http.MultipartRequest('POST', uri)
        ..headers['Authorization'] = 'Bearer $token'
        ..headers['Accept'] = 'application/json'
        ..files.add(
          filePath != null
              ? await http.MultipartFile.fromPath('file', filePath, filename: fileName, contentType: type)
              : http.MultipartFile.fromBytes('file', bytes, filename: fileName, contentType: type),
        );
      return http.Response.fromStream(await _http.send(request).timeout(const Duration(minutes: 30)));
    });
  }

  Future<Object?> _send(String method, String path, {Object? body, Map<String, String>? query}) async {
    final Uri uri = Uri.parse('$baseUrl$path').replace(queryParameters: query);
    const ApiException signedOut = ApiException('Please sign in again.', status: 401);
    final String? token = await _guarded<String?>(_auth.accessToken);
    if (token == null) {
      throw signedOut;
    }

    http.Response response = await _once(method, uri, token, body);
    if (response.statusCode == 401) {
      // The token may have been ended on the server: renew once and try again.
      final String? renewed =
          await _guarded<bool>(_auth.refresh) ? await _guarded<String?>(_auth.accessToken) : null;
      if (renewed == null) {
        throw signedOut;
      }

      response = await _once(method, uri, renewed, body);
    }

    if (response.statusCode >= 400) {
      throw apiErrorFrom(response.statusCode, response.body);
    }

    return response.body.isEmpty ? null : jsonDecode(response.body);
  }

  Future<http.Response> _once(String method, Uri uri, String token, Object? body) {
    final http.Request request = http.Request(method, uri)
      ..headers['Authorization'] = 'Bearer $token'
      ..headers['Accept'] = 'application/json';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }

    return _guarded(() async => http.Response.fromStream(await _http.send(request).timeout(_timeout)));
  }

  Future<T> _guarded<T>(Future<T> Function() action) async {
    try {
      return await action();
    } on SocketException {
      throw networkError;
    } on TimeoutException {
      throw networkError;
    } on http.ClientException {
      throw networkError;
    }
  }
}
