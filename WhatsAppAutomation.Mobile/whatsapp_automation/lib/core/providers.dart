import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http/http.dart' as http;

import '../features/auth/auth_service.dart';
import '../features/auth/biometric_service.dart';
import 'api/api_client.dart';
import 'storage/secure_store.dart';

final Provider<SecureStore> secureStoreProvider = Provider<SecureStore>((Ref ref) => const SecureStore());

final Provider<http.Client> httpClientProvider = Provider<http.Client>((Ref ref) {
  final http.Client client = http.Client();
  ref.onDispose(client.close);
  return client;
});

final Provider<AuthService> authServiceProvider = Provider<AuthService>(
  (Ref ref) => AuthService(ref.watch(secureStoreProvider), ref.watch(httpClientProvider)),
);

final Provider<ApiClient> apiClientProvider = Provider<ApiClient>(
  (Ref ref) => ApiClient(ref.watch(authServiceProvider), ref.watch(httpClientProvider)),
);

final Provider<BiometricService> biometricServiceProvider =
    Provider<BiometricService>((Ref ref) => BiometricService());
