import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:odinvault_mobile/odinvault/api_client.dart';

void main() {
  test('normalizes IP, port, pasted prefix, trailing slash and RTL marks', () {
    for (final value in [
      '188.213.65.140:5188',
      '://188.213.65.140:5188',
      'http://188.213.65.140:5188/',
      '\u200fhttp://188.213.65.140:5188\u200e',
    ]) {
      final base = normalizeAgentBaseUrl(value);
      expect(base, 'http://188.213.65.140:5188');
      final options = Options().compose(BaseOptions(baseUrl: base), '/api/health');
      expect(options.uri.toString(), 'http://188.213.65.140:5188/api/health');
    }
  });

  test('rejects credentials and query parameters in base URL', () {
    for (final value in ['', 'http://', 'http://user:secret@host', 'https://host?key=secret']) {
      expect(() => normalizeAgentBaseUrl(value), throwsA(isA<OdinVaultApiException>()));
    }
  });

  test('401 and transport errors are Persian and do not expose server secrets', () {
    final request = RequestOptions(path: '/api/health', baseUrl: 'http://host:5188');
    final unauthorized = DioException(
      requestOptions: request,
      type: DioExceptionType.badResponse,
      response: Response(requestOptions: request, statusCode: 401, data: {'message': 'secret'}),
    );
    expect(OdinVaultApiException.from(unauthorized).message, contains('کلید API'));
    expect(OdinVaultApiException.from(unauthorized).message, isNot(contains('secret')));
    final connection = DioException(requestOptions: request,
        type: DioExceptionType.connectionError, message: 'raw secret');
    final message = OdinVaultApiException.from(connection).message;
    expect(message, contains('connectionError'));
    expect(message, contains('http://host:5188/api/health'));
    expect(message, isNot(contains('raw secret')));
  });

  test('diagnostics omit OAuth state, URL credentials and query values', () {
    final safe = safeRequestUri(Uri.parse(
        'https://user:password@host/api/storage-targets/google-drive/pair/status/secret?key=token'));
    for (final secret in ['password', 'secret', 'token', 'user']) {
      expect(safe, isNot(contains(secret)));
    }
  });
}
