import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/memory_cache.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import '../whatsapp/link_whatsapp_page.dart';
import '../whatsapp/whatsapp_controller.dart';
import '../whatsapp/whatsapp_models.dart';
import 'broadcast_page.dart';
import 'chat_models.dart';
import 'chats_api.dart';
import 'new_message_page.dart';
import 'target_chat_page.dart';

/// The Chats tab: a summary card on top, then groups, categories, and sent lists,
/// like WhatsApp's chat list.
class ChatsTab extends ConsumerStatefulWidget {
  const ChatsTab({super.key});

  @override
  ConsumerState<ChatsTab> createState() => ChatsTabState();
}

class ChatsTabState extends ConsumerState<ChatsTab> {
  final TextEditingController _search = TextEditingController();
  List<ChatListItem>? _chats = MemoryCache.get<List<ChatListItem>>('chats');
  StreamSubscription<void>? _live;
  Pacing? _pacing;
  String? _error;
  String _filter = 'all';
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    reload();
    _search.addListener(() => setState(() {}));
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => reload());
    // Slow polling as a fallback when the live connection is down.
    _timer = Timer.periodic(const Duration(seconds: 20), (_) {
      if (_chats?.any((ChatListItem c) => c.last?.status == 'sending') ?? false) {
        reload();
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _live?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> reload() async {
    try {
      final List<ChatListItem> chats = await ref.read(chatsApiProvider).list();
      MemoryCache.set('chats', chats);
      if (mounted) {
        setState(() {
          _chats = chats;
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }

    try {
      final Pacing pacing = await ref.read(broadcastsApiProvider).pacing();
      if (mounted) {
        setState(() => _pacing = pacing);
      }
    } on Object {
      // The summary card then shows without today's numbers.
    }
  }

  Future<void> _open(ChatListItem c) async {
    final Widget page = switch (c.kind) {
      'group' || 'category' => TargetChatPage(kind: c.kind, id: c.id!, name: c.name),
      _ => BroadcastPage(id: c.id!),
    };
    await pushPage<void>(context, page);
    // A new message or a renamed group shows up in the list.
    await reload();
  }

  Future<void> newMessage() async {
    await pushPage<void>(context, const NewMessagePage());
    await reload();
  }

  Widget _summary(FThemeData theme, String name, WhatsAppAccount? account) {
    final Pacing? p = _pacing;
    final bool connected = account?.isConnected ?? false;
    final double raw = p == null || p.dailyLimit <= 0 ? 0 : p.sentToday / p.dailyLimit;
    final double used = raw > 1 ? 1 : raw;
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 12, 16, 8),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(20),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[theme.colors.primary, Color.lerp(theme.colors.primary, const Color(0xFF064E3B), 0.45)!],
        ),
        boxShadow: cardShadow(context),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Expanded(
                child: Text(
                  'Hello, ${name.split(' ').first}',
                  style: theme.typography.display.xl.copyWith(color: const Color(0xFFFFFFFF), fontWeight: FontWeight.w700),
                ),
              ),
              GestureDetector(
                onTap: () => pushPage<void>(context, const LinkWhatsAppPage()),
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                  decoration: BoxDecoration(color: const Color(0x33FFFFFF), borderRadius: BorderRadius.circular(999)),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: <Widget>[
                      Container(
                        width: 8,
                        height: 8,
                        decoration: BoxDecoration(
                          color: connected ? const Color(0xFF86EFAC) : const Color(0xFFFCA5A5),
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 6),
                      Text(
                        connected ? 'WhatsApp connected' : (account?.isLinked ?? false) ? account!.label : 'Not linked',
                        style: theme.typography.body.xs.copyWith(color: const Color(0xFFFFFFFF), fontWeight: FontWeight.w600),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            p == null || p.dailyLimit <= 0
                ? 'Send personal messages to your contacts, one by one.'
                : '${p.sentToday} of ${p.dailyLimit} messages sent today',
            style: theme.typography.body.sm.copyWith(color: const Color(0xE6FFFFFF)),
          ),
          if (p != null && p.dailyLimit > 0) ...<Widget>[
            const SizedBox(height: 10),
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: Stack(
                children: <Widget>[
                  Container(height: 6, color: const Color(0x33FFFFFF)),
                  FractionallySizedBox(
                    widthFactor: used,
                    child: Container(height: 6, color: const Color(0xFFFFFFFF)),
                  ),
                ],
              ),
            ),
          ],
          const SizedBox(height: 14),
          GestureDetector(
            onTap: account != null && !account.isLinked ? () => pushPage<void>(context, const LinkWhatsAppPage()) : newMessage,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(color: const Color(0xFFFFFFFF), borderRadius: BorderRadius.circular(12)),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Icon(
                    account != null && !account.isLinked ? FLucideIcons.link : FLucideIcons.squarePen,
                    size: 16,
                    color: theme.colors.primary,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    account != null && !account.isLinked ? 'Link your WhatsApp' : 'New message',
                    style: theme.typography.body.sm.copyWith(color: theme.colors.primary, fontWeight: FontWeight.w700),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final SessionState session = ref.watch(sessionProvider);
    final String name = session is SignedIn ? session.user.displayName : '';
    final WhatsAppAccount? account = ref.watch(whatsAppProvider).account;
    final List<ChatListItem> all = _chats ?? const <ChatListItem>[];
    final String q = _search.text.trim().toLowerCase();
    final List<ChatListItem> shown = all
        .where(
          (ChatListItem c) =>
              (_filter == 'all' ||
                  (_filter == 'other' ? c.kind == 'broadcast' : c.kind == _filter)) &&
              (q.isEmpty || c.name.toLowerCase().contains(q)),
        )
        .toList();

    return RefreshIndicator(
      onRefresh: reload,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          _summary(theme, name, account),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
            child: FTextField(
              control: FTextFieldControl.managed(controller: _search),
              hint: 'Search chats',
              prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
                  FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.search)),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 4),
            child: ChoiceChips<String>(
              options: const <(String, String)>[
                ('all', 'All'),
                ('group', 'Groups'),
                ('category', 'Categories'),
                ('other', 'Sent'),
              ],
              selected: _filter,
              onChange: (String f) => setState(() => _filter = f),
            ),
          ),
          if (_chats == null && _error == null) const LoadingView(),
          if (_error != null && _chats == null)
            EmptyView(icon: FLucideIcons.wifiOff, title: 'Cannot load chats', text: _error!, action: reload, actionLabel: 'Try again'),
          if (_chats != null && shown.isEmpty)
            EmptyView(
              icon: FLucideIcons.messageCircle,
              title: all.isEmpty ? 'No chats yet' : null,
              text: all.isEmpty
                  ? 'Add groups or categories under Contacts, or start a new message.'
                  : 'Nothing matches.',
              action: all.isEmpty ? newMessage : null,
              actionLabel: 'New message',
            ),
          if (shown.isNotEmpty)
            CardSection(
              children: <Widget>[
                for (final ChatListItem c in shown)
                  ListRow(
                    leading: PersonAvatar(
                      name: c.name,
                      size: 50,
                      icon: switch (c.kind) {
                        'group' => FLucideIcons.users,
                        'category' => FLucideIcons.tag,
                        _ => null,
                      },
                    ),
                    title: c.name,
                    subtitle: c.subtitle,
                    badge: c.last?.status == 'sending'
                        ? const Pill('Sending')
                        : c.last?.status == 'paused'
                            ? const Pill('Paused', color: Color(0xFFD97706))
                            : null,
                    trailing: c.lastAt == null
                        ? null
                        : Text(
                            listTimeText(c.lastAt!),
                            style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                          ),
                    onTap: () => _open(c),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}
