import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/theme.dart';
import '../../core/memory_cache.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import 'broadcast_page.dart';
import 'chat_bubble.dart';
import 'chat_info_page.dart';
import 'chat_models.dart';
import 'chats_api.dart';
import 'message_bar.dart';
import 'send_flow.dart';

/// A group's or category's chat, like a WhatsApp group: the messages sent to it, and a box to send the next one.
/// Each member still gets their own personal message. Tap the name for the group info.
class TargetChatPage extends ConsumerStatefulWidget {
  const TargetChatPage({super.key, required this.kind, required this.id, required this.name});

  /// group or category.
  final String kind;
  final String id;
  final String name;

  @override
  ConsumerState<TargetChatPage> createState() => _TargetChatPageState();
}

class _TargetChatPageState extends ConsumerState<TargetChatPage> {
  final ScrollController _scroll = ScrollController();
  late TargetChat? _chat = MemoryCache.get<TargetChat>('chat:${widget.kind}:${widget.id}');
  String? _error;
  bool _sending = false;
  Timer? _timer;
  StreamSubscription<void>? _live;

  @override
  void initState() {
    super.initState();
    _load(showErrors: true);
    if (_chat != null) {
      _toBottom();
    }

    // Ticks change at once through the live connection; slow polling is the fallback.
    _live = ref.read(liveUpdatesProvider).messageChanged.listen((_) => _load(showErrors: false));
    _timer = Timer.periodic(const Duration(seconds: 15), (_) {
      if (_chat?.messages.any((ChatMessage m) => m.summary.status == 'sending') ?? false) {
        _load(showErrors: false);
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _live?.cancel();
    _scroll.dispose();
    super.dispose();
  }

  void _toBottom({bool animate = false}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) {
        return;
      }

      if (animate) {
        _scroll.animateTo(_scroll.position.maxScrollExtent, duration: const Duration(milliseconds: 250), curve: Curves.easeOut);
      } else {
        _scroll.jumpTo(_scroll.position.maxScrollExtent);
      }
    });
  }

  Future<void> _load({required bool showErrors}) async {
    try {
      final TargetChat chat = await ref.read(chatsApiProvider).target(widget.kind, widget.id);
      if (!mounted) {
        return;
      }

      MemoryCache.set('chat:${widget.kind}:${widget.id}', chat);
      final bool first = _chat == null;
      setState(() {
        _chat = chat;
        _error = null;
      });
      if (first) {
        _toBottom();
      }
    } on Object catch (e) {
      if (mounted && _chat == null) {
        setState(() => _error = errorText(e));
      } else if (showErrors && mounted) {
        showErrorToast(context, 'Could not refresh the chat', e);
      }
    }
  }

  Future<bool> _send(String text, String? attachmentId) async {
    final TargetChat? chat = _chat;
    if (chat == null) {
      return false;
    }

    setState(() => _sending = true);
    try {
      final BroadcastInput input = BroadcastInput(
        name: chat.name,
        text: text,
        attachmentId: attachmentId,
        groupIds: chat.kind == 'group' ? <String>[chat.id] : const <String>[],
        categoryIds: chat.kind == 'category' ? <String>[chat.id] : const <String>[],
      );
      final BroadcastSummary? created = await confirmAndSend(context, ref, input);
      if (created != null) {
        await _load(showErrors: false);
        _toBottom(animate: true);
      }

      return created != null;
    } finally {
      if (mounted) {
        setState(() => _sending = false);
      }
    }
  }

  Future<void> _openInfo() async {
    final bool? deleted = await pushPage<bool>(context, ChatInfoPage(kind: widget.kind, id: widget.id));
    if (!mounted) {
      return;
    }

    if (deleted ?? false) {
      Navigator.of(context).pop();
    } else {
      await _load(showErrors: false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final TargetChat? chat = _chat;
    final String kindWord = widget.kind == 'group' ? 'Group' : 'Category';
    final String name = chat?.name ?? widget.name;
    return PageScaffold(
      title: name,
      leading: PersonAvatar(name: name, size: 36, icon: widget.kind == 'group' ? FLucideIcons.users : FLucideIcons.tag),
      onTitleTap: _openInfo,
      subtitle: chat == null
          ? kindWord
          : '$kindWord · ${chat.memberCount} ${chat.memberCount == 1 ? 'member' : 'members'} · tap for info',
      actions: <Widget>[FHeaderAction(icon: const Icon(FLucideIcons.info), onPress: _openInfo)],
      child: Column(
        children: <Widget>[
          Expanded(
            child: ColoredBox(
              color: chatWallpaper(context),
              child: chat == null
                  ? (_error == null
                      ? const LoadingView()
                      : EmptyView(
                          icon: FLucideIcons.wifiOff,
                          title: 'Could not open the chat',
                          text: _error!,
                          action: () {
                            setState(() => _error = null);
                            _load(showErrors: true);
                          },
                          actionLabel: 'Try again',
                        ))
                  : chat.messages.isEmpty
                      ? EmptyView(
                          icon: FLucideIcons.messageCircle,
                          title: 'No messages yet',
                          text: 'Write one below. Each member gets it personally, with their own name.',
                        )
                      : ListView(
                          controller: _scroll,
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                          children: messagesWithDayLines(
                            chat.messages,
                            (ChatMessage m) => pushPage<void>(context, BroadcastPage(id: m.summary.id)),
                          ),
                        ),
            ),
          ),
          if (chat != null) MessageBar(busy: _sending, onSend: _send),
        ],
      ),
    );
  }
}
