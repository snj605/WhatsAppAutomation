import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';

/// A group's or category's info: name, colour, members (add and remove), delete. Same as the website.
/// Pops with true when the group or category was deleted.
class ChatInfoPage extends ConsumerStatefulWidget {
  const ChatInfoPage({super.key, required this.kind, required this.id});

  /// group or category.
  final String kind;
  final String id;

  @override
  ConsumerState<ChatInfoPage> createState() => _ChatInfoPageState();
}

class _ChatInfoPageState extends ConsumerState<ChatInfoPage> {
  static const int _page = 200;

  String _name = '';
  String? _color;
  List<Contact>? _members;
  int _memberTotal = 0;
  bool _busy = false;
  String? _loadError;

  bool get _isGroup => widget.kind == 'group';
  String get _label => _isGroup ? 'group' : 'category';

  bool get _canManage {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.can('contacts.manage');
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      if (_isGroup) {
        final Group? g = (await api.groups()).where((Group x) => x.id == widget.id).firstOrNull;
        _name = g?.name ?? '';
        _color = null;
      } else {
        final Category? c = (await api.categories()).where((Category x) => x.id == widget.id).firstOrNull;
        _name = c?.name ?? '';
        _color = c?.color;
      }

      await _loadMembers();
    } on Object catch (e) {
      if (mounted) {
        if (_members == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load', e);
        }
      }
    }
  }

  Future<void> _loadMembers() async {
    final ContactPage page = await ref.read(contactsApiProvider).contacts(
          groupId: _isGroup ? widget.id : null,
          categoryId: _isGroup ? null : widget.id,
          pageSize: _page,
        );
    if (mounted) {
      setState(() {
        _members = page.items;
        _memberTotal = page.total;
      });
    }
  }

  Future<void> _run(Future<void> Function() action, String done) async {
    setState(() => _busy = true);
    try {
      await action();
      if (mounted) {
        showInfoToast(context, done);
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not done', e);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _change(List<String> ids, {required bool add}) {
    final ContactsApi api = ref.read(contactsApiProvider);
    return _isGroup ? api.changeGroupMembers(widget.id, ids, add: add) : api.changeCategoryMembers(widget.id, ids, add: add);
  }

  Future<void> _rename() async {
    final String? name = await pushPage<String>(context, _RenamePage(label: _label, name: _name));
    if (name == null || name == _name) {
      return;
    }

    await _run(() async {
      final ContactsApi api = ref.read(contactsApiProvider);
      if (_isGroup) {
        await api.saveGroup(widget.id, name);
      } else {
        await api.saveCategory(widget.id, name, _color ?? hexFromColor(categoryColors.first));
      }

      setState(() => _name = name);
    }, 'Name saved');
  }

  Future<void> _setColor(int color) => _run(() async {
        final String hex = hexFromColor(color);
        await ref.read(contactsApiProvider).saveCategory(widget.id, _name, hex);
        setState(() => _color = hex);
      }, 'Colour saved');

  Future<void> _remove(Contact c) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Remove ${c.displayName}?',
      body: 'They stay in your contacts. They will not get this $_label’s next messages.',
      confirm: 'Remove',
      destructive: true,
    );
    if (yes) {
      await _run(() async {
        await _change(<String>[c.id], add: false);
        await _loadMembers();
      }, '${c.displayName} removed');
    }
  }

  Future<void> _add() async {
    final List<String>? ids = await pushPage<List<String>>(
      context,
      _AddMembersPage(already: (_members ?? const <Contact>[]).map((Contact c) => c.id).toSet()),
    );
    if (ids == null || ids.isEmpty) {
      return;
    }

    await _run(() async {
      await _change(ids, add: true);
      await _loadMembers();
    }, '${ids.length} added');
  }

  Future<void> _delete() async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete this $_label?',
      body: '"$_name" will be deleted. The contacts in it stay in your contacts. Messages already sent are not affected.',
      confirm: 'Delete',
      cancel: 'Keep',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    setState(() => _busy = true);
    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      await (_isGroup ? api.deleteGroup(widget.id) : api.deleteCategory(widget.id));
      if (mounted) {
        showInfoToast(context, 'Deleted');
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
        setState(() => _busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final List<Contact>? members = _members;
    final bool manage = _canManage && !_busy;
    return PageScaffold(
      title: _isGroup ? 'Group info' : 'Category info',
      child: members == null
          ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load',
                  text: _loadError!,
                  action: () {
                    setState(() => _loadError = null);
                    _load();
                  },
                  actionLabel: 'Try again',
                ))
          : ListView(
              padding: const EdgeInsets.only(bottom: 24),
              children: <Widget>[
                const SizedBox(height: 16),
                Center(
                  child: PersonAvatar(
                    name: _name,
                    size: 88,
                    icon: _isGroup ? FLucideIcons.users : FLucideIcons.tag,
                  ),
                ),
                const SizedBox(height: 12),
                Center(
                  child: GestureDetector(
                    onTap: manage ? _rename : null,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: <Widget>[
                        Text(_name, style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w700)),
                        if (manage) ...<Widget>[
                          const SizedBox(width: 8),
                          Icon(FLucideIcons.pencil, size: 16, color: theme.colors.mutedForeground),
                        ],
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 4),
                Center(
                  child: Text(
                    '${_isGroup ? 'Group' : 'Category'} · $_memberTotal ${_memberTotal == 1 ? 'member' : 'members'}',
                    style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                  ),
                ),
                if (!_isGroup && _canManage)
                  CardSection(
                    title: 'Colour',
                    padding: const EdgeInsets.all(14),
                    children: <Widget>[
                      Wrap(
                        spacing: 12,
                        runSpacing: 12,
                        children: <Widget>[
                          for (final int c in categoryColors)
                            GestureDetector(
                              onTap: manage ? () => _setColor(c) : null,
                              child: Container(
                                width: 34,
                                height: 34,
                                decoration: BoxDecoration(
                                  color: Color(c),
                                  shape: BoxShape.circle,
                                  border: Border.all(
                                    color: colorFromHex(_color) == c ? theme.colors.foreground : const Color(0x00000000),
                                    width: 3,
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ],
                  ),
                CardSection(
                  title: '$_memberTotal ${_memberTotal == 1 ? 'member' : 'members'}',
                  trailing: _canManage
                      ? FButton(
                          size: FButtonSizeVariant.sm,
                          mainAxisSize: MainAxisSize.min,
                          prefix: const Icon(FLucideIcons.userPlus),
                          onPress: manage ? _add : null,
                          child: const Text('Add'),
                        )
                      : null,
                  children: <Widget>[
                    if (members.isEmpty)
                      Padding(
                        padding: const EdgeInsets.all(16),
                        child: Text(
                          'Nobody here yet. Add people to send them messages from this chat.',
                          style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                        ),
                      ),
                    for (final Contact c in members)
                      ListRow(
                        leading: PersonAvatar(name: c.displayName, size: 40),
                        title: c.displayName,
                        subtitle: c.phoneNumber,
                        dimmed: c.optedOut,
                        badge: c.optedOut ? Pill('No broadcasts', color: theme.colors.destructive) : null,
                        trailing: _canManage
                            ? FButton.icon(
                                variant: FButtonVariant.ghost,
                                onPress: manage ? () => _remove(c) : null,
                                child: Icon(FLucideIcons.userMinus, color: theme.colors.destructive),
                              )
                            : null,
                      ),
                  ],
                ),
                if (_memberTotal > members.length)
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                    child: Text(
                      'Showing the first ${members.length}. See all in the Contacts tab.',
                      style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                    ),
                  ),
                if (_canManage)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
                    child: FButton(
                      variant: FButtonVariant.outline,
                      prefix: Icon(FLucideIcons.trash2, color: theme.colors.destructive),
                      onPress: manage ? _delete : null,
                      child: Text('Delete this $_label', style: TextStyle(color: theme.colors.destructive)),
                    ),
                  ),
              ],
            ),
    );
  }
}

class _RenamePage extends StatefulWidget {
  const _RenamePage({required this.label, required this.name});

  final String label;
  final String name;

  @override
  State<_RenamePage> createState() => _RenamePageState();
}

class _RenamePageState extends State<_RenamePage> {
  late final TextEditingController _name = TextEditingController(text: widget.name);

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  void _done() {
    final String name = _name.text.trim();
    if (name.isNotEmpty) {
      Navigator.of(context).pop(name);
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Rename ${widget.label}',
        footer: SafeArea(
          top: false,
          child: Padding(padding: const EdgeInsets.all(12), child: FButton(onPress: _done, child: const Text('Save'))),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: FTextField(
            control: FTextFieldControl.managed(controller: _name),
            label: const Text('Name'),
            autofocus: true,
            onSubmit: (_) => _done(),
          ),
        ),
      );
}

/// Search contacts and tick who to add. Returns the chosen ids.
class _AddMembersPage extends ConsumerStatefulWidget {
  const _AddMembersPage({required this.already});

  final Set<String> already;

  @override
  ConsumerState<_AddMembersPage> createState() => _AddMembersPageState();
}

class _AddMembersPageState extends ConsumerState<_AddMembersPage> {
  final TextEditingController _search = TextEditingController();
  List<Contact>? _candidates;
  Set<String> _picked = <String>{};
  Timer? _timer;
  int _seq = 0;

  @override
  void initState() {
    super.initState();
    _find();
  }

  @override
  void dispose() {
    _timer?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _find() async {
    final int seq = ++_seq;
    try {
      final ContactPage page = await ref.read(contactsApiProvider).contacts(search: _search.text, pageSize: 200);
      if (mounted && seq == _seq) {
        setState(() => _candidates = page.items);
      }
    } on Object {
      if (mounted && seq == _seq) {
        setState(() => _candidates = const <Contact>[]);
      }
    }
  }

  void _toggle(String id) => setState(() {
        _picked = Set<String>.of(_picked);
        if (!_picked.remove(id)) {
          _picked.add(id);
        }
      });

  @override
  Widget build(BuildContext context) {
    final List<Contact> list = (_candidates ?? const <Contact>[]).where((Contact c) => !widget.already.contains(c.id)).toList();
    return PageScaffold(
      title: 'Add people',
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            onPress: _picked.isEmpty ? null : () => Navigator.of(context).pop(_picked.toList()),
            child: Text(_picked.isEmpty ? 'Tick people to add' : 'Add ${_picked.length}'),
          ),
        ),
      ),
      child: ListView(
        children: <Widget>[
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
            child: FTextField(
              control: FTextFieldControl.managed(
                controller: _search,
                onChange: (_) {
                  _timer?.cancel();
                  _timer = Timer(const Duration(milliseconds: 300), _find);
                },
              ),
              hint: 'Search by name or number',
              prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
                  FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.search)),
            ),
          ),
          if (_candidates == null) const LoadingView(),
          if (_candidates != null && list.isEmpty)
            const EmptyView(icon: FLucideIcons.users, text: 'Nobody else found.'),
          if (list.isNotEmpty)
            CardSection(
              children: <Widget>[
                for (final Contact c in list)
                  ListRow(
                    leading: FCheckbox(value: _picked.contains(c.id), onChange: (_) => _toggle(c.id)),
                    title: c.displayName,
                    subtitle: c.phoneNumber,
                    trailing: PersonAvatar(name: c.displayName, size: 36),
                    onTap: () => _toggle(c.id),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}
