import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import 'contact_models.dart';
import 'contacts_api.dart';

/// Categories and groups: add, rename, delete; and the default language for new contacts.
/// Same as the website's "Categories & groups" page.
class ListsPage extends ConsumerStatefulWidget {
  const ListsPage({super.key});

  @override
  ConsumerState<ListsPage> createState() => _ListsPageState();
}

class _ListsPageState extends ConsumerState<ListsPage> {
  List<Category>? _categories;
  List<Group>? _groups;
  String _language = 'en';
  String? _loadError;

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
      final (List<Category> c, List<Group> g, String l) = await (api.categories(), api.groups(), api.defaultLanguage()).wait;
      if (mounted) {
        setState(() {
          _loadError = null;
          _categories = c;
          _groups = g;
          _language = l;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_categories == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load the lists', e);
        }
      }
    }
  }

  Future<void> _editCategory(Category? c) async {
    final ({String name, String color})? result =
        await pushPage<({String name, String color})>(context, _NamePage(kind: 'category', name: c?.name, color: c?.color));
    if (result == null || !mounted) {
      return;
    }

    try {
      await ref.read(contactsApiProvider).saveCategory(c?.id, result.name, result.color);
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not saved', e);
      }
    }
  }

  Future<void> _editGroup(Group? g) async {
    final ({String name, String color})? result =
        await pushPage<({String name, String color})>(context, _NamePage(kind: 'group', name: g?.name));
    if (result == null || !mounted) {
      return;
    }

    try {
      await ref.read(contactsApiProvider).saveGroup(g?.id, result.name);
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not saved', e);
      }
    }
  }

  Future<void> _remove(String kind, String id, String name, int count) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: kind == 'category' ? 'Delete category?' : 'Delete group?',
      body: '"$name" will be removed. This cannot be undone.',
      confirm: 'Delete',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      await (kind == 'category' ? api.deleteCategory(id) : api.deleteGroup(id));
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    }
  }

  Future<void> _setLanguage(String language) async {
    final String before = _language;
    setState(() => _language = language);
    try {
      await ref.read(contactsApiProvider).setDefaultLanguage(language);
    } on Object catch (e) {
      if (mounted) {
        setState(() => _language = before);
        showErrorToast(context, 'Not saved', e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final List<Category>? categories = _categories;
    final List<Group>? groups = _groups;
    return PageScaffold(
      title: 'Categories & groups',
      child: categories == null || groups == null
          ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load the lists',
                  text: _loadError!,
                  action: () {
                    setState(() => _loadError = null);
                    _load();
                  },
                  actionLabel: 'Try again',
                ))
          : ListView(
              padding: const EdgeInsets.all(16),
              children: <Widget>[
                CardSection(
                  margin: EdgeInsets.zero,
                  title: 'Categories',
                  trailing: _canManage
                      ? FButton(
                          size: FButtonSizeVariant.sm,
                          mainAxisSize: MainAxisSize.min,
                          prefix: const Icon(FLucideIcons.plus),
                          onPress: () => _editCategory(null),
                          child: const Text('Add'),
                        )
                      : null,
                  children: <Widget>[
                if (categories.isEmpty)
                  const ListRow(leading: IconBadge(FLucideIcons.tag), title: 'No categories yet', subtitle: 'For example Elders or Youngers'),
                for (final Category c in categories)
                  ListRow(
                    leading: IconBadge(FLucideIcons.tag, color: Color(colorFromHex(c.color))),
                    title: c.name,
                    subtitle: '${c.contactCount} ${c.contactCount == 1 ? 'contact' : 'contacts'}',
                    trailing: _canManage
                        ? FButton.icon(
                            variant: FButtonVariant.ghost,
                            onPress: () => _remove('category', c.id, c.name, c.contactCount),
                            child: const Icon(FLucideIcons.trash2),
                          )
                        : null,
                    onTap: _canManage ? () => _editCategory(c) : null,
                  ),
                  ],
                ),
                const SizedBox(height: 20),
                CardSection(
                  margin: EdgeInsets.zero,
                  title: 'Groups',
                  trailing: _canManage
                      ? FButton(
                          size: FButtonSizeVariant.sm,
                          mainAxisSize: MainAxisSize.min,
                          prefix: const Icon(FLucideIcons.plus),
                          onPress: () => _editGroup(null),
                          child: const Text('Add'),
                        )
                      : null,
                  children: <Widget>[
                if (groups.isEmpty)
                  const ListRow(leading: IconBadge(FLucideIcons.users), title: 'No groups yet', subtitle: 'For example Family or Office'),
                for (final Group g in groups)
                  ListRow(
                    leading: const IconBadge(FLucideIcons.users, color: Color(0xFF6366F1)),
                    title: g.name,
                    subtitle: '${g.contactCount} ${g.contactCount == 1 ? 'contact' : 'contacts'}',
                    trailing: _canManage
                        ? FButton.icon(
                            variant: FButtonVariant.ghost,
                            onPress: () => _remove('group', g.id, g.name, g.contactCount),
                            child: const Icon(FLucideIcons.trash2),
                          )
                        : null,
                    onTap: _canManage ? () => _editGroup(g) : null,
                  ),
                  ],
                ),
                const SizedBox(height: 24),
                const SectionLabel('Language for new contacts'),
                ChoiceChips<String>(
                  options: languages,
                  selected: _language,
                  onChange: _canManage ? _setLanguage : null,
                ),
              ],
            ),
    );
  }
}

/// The name (and for a category the colour) of a category or group. Returns them, or null when cancelled.
class _NamePage extends StatefulWidget {
  const _NamePage({required this.kind, this.name, this.color});

  final String kind;
  final String? name;
  final String? color;

  @override
  State<_NamePage> createState() => _NamePageState();
}

class _NamePageState extends State<_NamePage> {
  late final TextEditingController _name = TextEditingController(text: widget.name ?? '');
  late int _color = widget.color == null ? categoryColors.first : colorFromHex(widget.color);
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  void _done() {
    final String name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _error = 'Enter a name.');
      return;
    }

    Navigator.of(context).pop((name: name, color: hexFromColor(_color)));
  }

  @override
  Widget build(BuildContext context) {
    final bool isCategory = widget.kind == 'category';
    final String what = isCategory ? 'category' : 'group';
    return PageScaffold(
      title: widget.name == null ? 'New $what' : 'Edit $what',
      footer: SafeArea(
        top: false,
        child: Padding(padding: const EdgeInsets.all(12), child: FButton(onPress: _done, child: const Text('Save'))),
      ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          FTextField(
            control: FTextFieldControl.managed(controller: _name),
            label: const Text('Name'),
            hint: isCategory ? 'Elders' : 'Family',
            autofocus: true,
            onSubmit: (_) => _done(),
            error: _error == null ? null : Text(_error!),
          ),
          if (isCategory) ...<Widget>[
            const SizedBox(height: 16),
            const SectionLabel('Colour'),
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: <Widget>[
                for (final int c in categoryColors)
                  GestureDetector(
                    onTap: () => setState(() => _color = c),
                    child: Container(
                      width: 36,
                      height: 36,
                      decoration: BoxDecoration(
                        color: Color(c),
                        shape: BoxShape.circle,
                        border: Border.all(
                          color: _color == c ? context.theme.colors.foreground : const Color(0x00000000),
                          width: 3,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
