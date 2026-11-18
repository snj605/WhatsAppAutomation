import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import 'chat_models.dart';

final Provider<ChatsApi> chatsApiProvider = Provider<ChatsApi>((Ref ref) => ChatsApi(ref.watch(apiClientProvider)));

class ChatsApi {
  const ChatsApi(this._api);

  final ApiClient _api;

  Future<List<ChatListItem>> list() async => asList(await _api.get('/api/v1/chats')).map(ChatListItem.fromJson).toList();

  /// kind: group or category.
  Future<TargetChat> target(String kind, String id) async =>
      TargetChat.fromJson((await _api.get('/api/v1/chats/$kind/$id'))! as Json);

}
