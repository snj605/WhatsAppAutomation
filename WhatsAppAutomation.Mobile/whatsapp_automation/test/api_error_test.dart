import 'package:flutter_test/flutter_test.dart';
import 'package:whatsapp_automation/core/api/api_error.dart';

void main() {
  test('the server message and code of a problem response are kept', () {
    final ApiException error = apiErrorFrom(
      409,
      '{"title":"Conflict","status":409,"detail":"A WhatsApp number is already linked. Unlink it first.","code":"whatsapp.already_linked"}',
    );

    expect(error.message, 'A WhatsApp number is already linked. Unlink it first.');
    expect(error.code, 'whatsapp.already_linked');
    expect(error.status, 409);
  });

  test('field errors show the first message of each field', () {
    final ApiException error = apiErrorFrom(
      400,
      '{"title":"One or more validation errors occurred.","errors":{"UserName":["User name is required.","x"]}}',
    );

    expect(error.message, 'User name is required.');
  });

  test('a response that is not JSON gets a general message', () {
    expect(apiErrorFrom(502, '<html>Bad gateway</html>').message, contains('server has a problem'));
    expect(apiErrorFrom(429, '').message, contains('Too many tries'));
  });
}
