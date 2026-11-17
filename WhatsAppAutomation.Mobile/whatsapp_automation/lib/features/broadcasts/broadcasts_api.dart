import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import 'broadcast_models.dart';

final Provider<BroadcastsApi> broadcastsApiProvider =
    Provider<BroadcastsApi>((Ref ref) => BroadcastsApi(ref.watch(apiClientProvider)));

/// Calls to the signed-in user's own broadcasts (same as the website's broadcasts.api.ts).
class BroadcastsApi {
  const BroadcastsApi(this._api);

  final ApiClient _api;

  Future<List<BroadcastSummary>> list() async =>
      asList(await _api.get('/api/v1/broadcasts')).map(BroadcastSummary.fromJson).toList();

  Future<BroadcastDetails> get(String id) async =>
      BroadcastDetails.fromJson((await _api.get('/api/v1/broadcasts/$id'))! as Json);

  Future<BroadcastPreview> preview(BroadcastInput input) async =>
      BroadcastPreview.fromJson((await _api.post('/api/v1/broadcasts/preview', input.toJson()))! as Json);

  Future<BroadcastSummary> create(BroadcastInput input) async =>
      BroadcastSummary.fromJson((await _api.post('/api/v1/broadcasts', input.toJson()))! as Json);

  /// action: pause, resume or cancel.
  Future<void> change(String id, String action) => _api.post('/api/v1/broadcasts/$id/$action');

  Future<Pacing> pacing() async => Pacing.fromJson((await _api.get('/api/v1/broadcasts/pacing'))! as Json);
}
