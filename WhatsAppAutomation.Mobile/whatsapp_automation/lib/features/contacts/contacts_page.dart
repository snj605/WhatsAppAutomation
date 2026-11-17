import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../app/confirm_dialog.dart';
import '../../core/memory_cache.dart';
import '../../core/realtime/live_updates.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import '../chats/new_message_page.dart';
import 'contact_form_page.dart';
import 'contact_models.dart';
import 'contacts_api.dart';
import 'import_page.dart';
import 'lists_page.dart';

/// Opens the person's chat in WhatsApp itself (`https://wa.me/<number>`). Shows a toast when it cannot.
Future<void> openInWhatsApp(BuildContext context, String phoneNumber) async {
  final String digits = phoneNumber.replaceAll(RegExp(r'[^0-9]'), '');
  bool opened = false;
  try {
    opened = await launchUrl(Uri.parse('https://wa.me/$digits'), mode: LaunchMode.externalApplication);
  } on Object {
    opened = false;
  }

  if (!opened && context.mounted) {
    showFToast(
      context: context,
      variant: FToastVariant.destructive,
      icon: const Icon(FLucideIcons.circleAlert),
      title: const Text('Could not open WhatsApp'),
      description: const Text('Check that WhatsApp is installed on this phone.'),
    );
  }
}

/// The Contacts tab: search, filter by category, add, edit, stop broadcasts, delete.
/// Long-press a contact to select several, then delete, add to a group or stop broadcasts for all of them.
/// Numbers are checked on WhatsApp automatically; those without WhatsApp are kept in their own list.
class ContactsTab extends ConsumerStatefulWidget {
  const ContactsTab({super.key});

  @override
  ConsumerState<ContactsTab> createState() => ContactsTabState();
}

class ContactsTabState extends ConsumerState<ContactsTab> {
  static const int _pageSize = 50;
  static const String _cacheKey = 'contacts:first';

  final TextEditingController _search = TextEditingController();
  List<Category> _categories = <Category>[];
  List<Group> _groups = <Group>[];
  String _defaultLanguage = 'en';
  String? _categoryId;
  bool _needsDetails = false;
  final List<Contact> _contacts = <Contact>[];
  int _total = 0;
  int _page = 0;
  bool _loading = false;
  String? _error;
  int _seq = 0;
  Timer? _searchTimer;

  /// null: the normal list (without numbers that are not on WhatsApp). "not_on_whatsapp": only those.
  String? _whatsApp;
  WhatsAppCheckSummary _check = WhatsAppCheckSummary.empty;
  bool _checkBusy = false;
  StreamSubscription<void>? _live;

  /// Ticked contacts while selecting several (empty: not selecting).
  Map<String, Contact> _selected = <String, Contact>{};
  bool _bulkBusy = false;

  bool get _canManage {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.can('contacts.manage');
  }

  bool get _unfiltered => _search.text.trim().isEmpty && _categoryId == null && !_needsDetails && _whatsApp == null;

  bool get _showingNotOnWhatsApp => _whatsApp == 'not_on_whatsapp';

  @override
  void initState() {
    super.initState();
    final ContactPage? cached = MemoryCache.get<ContactPage>(_cacheKey);
    if (cached != null) {
      _contacts.addAll(cached.items);
      _total = cached.total;
      _page = 1;
    }

    _loadLists();
    _loadCheck();
    reload();
    // Results of the WhatsApp check arrive live: refresh the counts and the list.
    _live = ref.read(liveUpdatesProvider).contactsChanged.listen((_) {
      _loadCheck();
      reload();
    });
  }

  @override
  void dispose() {
    _live?.cancel();
    _searchTimer?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _loadLists() async {
    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      final (List<Category> categories, List<Group> groups, String language) =
          await (api.categories(), api.groups(), api.defaultLanguage()).wait;
      if (mounted) {
        setState(() {
          _categories = categories;
          _groups = groups;
          _defaultLanguage = language;
        });
      }
    } on Object {
      // The list still works without the filters.
    }
  }

  Future<void> _loadCheck() async {
    try {
      final WhatsAppCheckSummary check = await ref.read(contactsApiProvider).whatsAppCheck();
      if (mounted) {
        setState(() => _check = check);
      }
    } on Object {
      // The list works without the counts.
    }
  }

  /// Checks every contact that was never checked (saved before the check existed), or these contacts again.
  Future<void> _requestCheck([List<String>? ids]) async {
    setState(() => _checkBusy = true);
    try {
      final WhatsAppCheckSummary check = await ref.read(contactsApiProvider).requestWhatsAppCheck(ids);
      if (!mounted) {
        return;
      }

      setState(() => _check = check);
      showInfoToast(
        context,
        check.connected ? 'Checking numbers on WhatsApp' : 'Will be checked when WhatsApp is connected',
        description: check.connected
            ? 'Numbers without WhatsApp move to "Not on WhatsApp".'
            : 'Link your WhatsApp number on the Me tab.',
      );
      await reload();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Could not start the check', e);
      }
    } finally {
      if (mounted) {
        setState(() => _checkBusy = false);
      }
    }
  }

  Future<void> reload() async {
    final bool keepShowing = _unfiltered && _contacts.isNotEmpty && _page <= 1;
    _page = 0;
    if (!keepShowing) {
      _contacts.clear();
    }

    await _more(replace: keepShowing);
  }

  Future<void> _more({bool replace = false}) async {
    final int seq = ++_seq;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final ContactPage page = await ref.read(contactsApiProvider).contacts(
            search: _search.text,
            categoryId: _categoryId,
            needsDetails: _needsDetails,
            page: _page + 1,
            pageSize: _pageSize,
            whatsApp: _whatsApp,
          );
      if (_page == 0 && _unfiltered) {
        MemoryCache.set(_cacheKey, page);
      }

      if (mounted && seq == _seq) {
        setState(() {
          if (replace) {
            _contacts.clear();
          }

          _page += 1;
          _contacts.addAll(page.items);
          _total = page.total;
        });
      }
    } on Object catch (e) {
      if (mounted && seq == _seq) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted && seq == _seq) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> add() => _edit(null);

  Future<void> _edit(Contact? contact) async {
    final Contact? saved = await pushPage<Contact>(
      context,
      ContactFormPage(contact: contact, categories: _categories, groups: _groups, defaultLanguage: _defaultLanguage),
    );
    if (saved != null && mounted) {
      await _loadCheck();
      if (!mounted) {
        return;
      }

      showInfoToast(
        context,
        contact == null ? 'Contact added' : 'Contact saved',
        description: !saved.waitingForCheck
            ? saved.displayName
            : _check.connected
                ? '${saved.displayName} · checking the number on WhatsApp…'
                : '${saved.displayName} · the number is checked on WhatsApp once WhatsApp is connected.',
      );
      await _loadLists();
      await reload();
    }
  }

  Future<void> openImport() async {
    final bool? imported = await pushPage<bool>(context, const ImportPage());
    if (imported ?? false) {
      await _loadCheck();
      await _loadLists();
      await reload();
    }
  }

  Future<void> openLists() async {
    await pushPage<void>(context, const ListsPage());
    await _loadLists();
    await reload();
  }

  Future<void> _setOptOut(Contact c, {required bool stop}) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: stop ? 'Stop broadcasts?' : 'Allow broadcasts again?',
      body: stop
          ? '${c.displayName} will not get any broadcast until you allow it again.'
          : '${c.displayName} will get broadcasts again.',
      confirm: stop ? 'Stop broadcasts' : 'Allow',
      destructive: stop,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      final Contact saved = await ref.read(contactsApiProvider).setOptOut(c.id, optedOut: stop);
      if (mounted) {
        showInfoToast(context, stop ? 'Broadcasts stopped' : 'Broadcasts allowed', description: saved.displayName);
        await reload();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not changed', e);
      }
    }
  }

  Future<void> _deleteOne(Contact c) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete contact?',
      body: '${c.displayName} will be removed. This cannot be undone.',
      confirm: 'Delete',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      await ref.read(contactsApiProvider).deleteContacts(<String>[c.id]);
      if (mounted) {
        showInfoToast(context, 'Contact deleted');
        await _loadLists();
        await reload();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    }
  }

  Future<void> _open(Contact c) async {
    final String? action = await pushPage<String>(context, _ContactPage(contact: c, canManage: _canManage));
    if (!mounted || action == null) {
      return;
    }

    switch (action) {
      case 'edit':
        await _edit(c);
      case 'optout':
        await _setOptOut(c, stop: !c.optedOut);
      case 'delete':
        await _deleteOne(c);
      case 'recheck':
        await _requestCheck(<String>[c.id]);
      case 'message':
        await pushPage<void>(
          context,
          NewMessagePage(
            preset: ComposerPreset(
              name: 'Message to ${c.displayName}',
              people: <({String id, String label})>[(id: c.id, label: c.displayName)],
              text: '',
            ),
          ),
        );
    }
  }

  // ---- Selecting several ----

  void _toggleSelect(Contact c) => setState(() {
        _selected = Map<String, Contact>.of(_selected);
        if (_selected.remove(c.id) == null) {
          _selected[c.id] = c;
        }
      });

  void _clearSelection() => setState(() => _selected = <String, Contact>{});

  Future<void> _bulk(Future<void> Function(List<String> ids) action, String done) async {
    final List<String> ids = _selected.keys.toList();
    setState(() => _bulkBusy = true);
    try {
      await action(ids);
      if (mounted) {
        showInfoToast(context, done);
        _clearSelection();
        await _loadLists();
        await reload();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not done', e);
      }
    } finally {
      if (mounted) {
        setState(() => _bulkBusy = false);
      }
    }
  }

  Future<void> _bulkDelete() async {
    final int n = _selected.length;
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete $n ${n == 1 ? 'contact' : 'contacts'}?',
      body: 'They will be removed. This cannot be undone.',
      confirm: 'Delete',
      destructive: true,
    );
    if (yes) {
      await _bulk((List<String> ids) => ref.read(contactsApiProvider).deleteContacts(ids), '$n deleted');
    }
  }

  Future<void> _bulkAddToGroup() async {
    final Group? group = await pushPage<Group>(context, _PickGroupPage(groups: _groups));
    if (group == null) {
      return;
    }

    final int n = _selected.length;
    await _bulk(
      (List<String> ids) => ref.read(contactsApiProvider).changeGroupMembers(group.id, ids, add: true),
      '$n added to ${group.name}',
    );
  }

  Future<void> _bulkStopBroadcasts() async {
    final int n = _selected.length;
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Stop broadcasts for $n ${n == 1 ? 'contact' : 'contacts'}?',
      body: 'They will not get any broadcast until you allow it again.',
      confirm: 'Stop broadcasts',
      destructive: true,
    );
    if (yes) {
      await _bulk((List<String> ids) async {
        final ContactsApi api = ref.read(contactsApiProvider);
        for (final String id in ids) {
          await api.setOptOut(id, optedOut: true);
        }
      }, 'Broadcasts stopped for $n');
    }
  }

  String _subtitle(Contact c) {
    final List<String> parts = <String>[c.phoneNumber];
    final Category? cat = _categories.where((Category x) => x.id == c.categoryId).firstOrNull;
    if (cat != null) {
      parts.add(cat.name);
    }

    return parts.join(' · ');
  }

  Widget _bulkBar(FThemeData theme) => Container(
        padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
        decoration: BoxDecoration(
          color: theme.colors.card,
          border: Border(top: BorderSide(color: theme.colors.border, width: 0.6)),
          boxShadow: cardShadow(context),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text(
                    '${_selected.length} selected',
                    style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w700),
                  ),
                ),
                FButton(
                  variant: FButtonVariant.ghost,
                  size: FButtonSizeVariant.sm,
                  mainAxisSize: MainAxisSize.min,
                  onPress: _bulkBusy ? null : _clearSelection,
                  child: const Text('Cancel'),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              children: <Widget>[
                Expanded(
                  child: FButton(
                    variant: FButtonVariant.outline,
                    size: FButtonSizeVariant.sm,
                    prefix: const Icon(FLucideIcons.userPlus),
                    onPress: _bulkBusy || _groups.isEmpty || _showingNotOnWhatsApp ? null : _bulkAddToGroup,
                    child: const Text('To group'),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: FButton(
                    variant: FButtonVariant.outline,
                    size: FButtonSizeVariant.sm,
                    prefix: const Icon(FLucideIcons.bellOff),
                    onPress: _bulkBusy ? null : _bulkStopBroadcasts,
                    child: const Text('Stop'),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: FButton(
                    variant: FButtonVariant.destructive,
                    size: FButtonSizeVariant.sm,
                    prefix: _bulkBusy
                        ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
                        : const Icon(FLucideIcons.trash2),
                    onPress: _bulkBusy ? null : _bulkDelete,
                    child: const Text('Delete'),
                  ),
                ),
              ],
            ),
          ],
        ),
      );

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final bool selecting = _selected.isNotEmpty;
    return PopScope(
      canPop: !selecting,
      onPopInvokedWithResult: (bool didPop, Object? _) {
        if (!didPop) {
          _clearSelection();
        }
      },
      child: Column(
        children: <Widget>[
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async {
                await _loadLists();
                await reload();
              },
              child: NotificationListener<ScrollNotification>(
                onNotification: (ScrollNotification n) {
                  if (n.metrics.pixels > n.metrics.maxScrollExtent - 400 && !_loading && _contacts.length < _total) {
                    _more();
                  }

                  return false;
                },
                child: ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.only(bottom: 24),
                  children: <Widget>[
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
                      child: FTextField(
                        control: FTextFieldControl.managed(
                          controller: _search,
                          onChange: (_) {
                            _searchTimer?.cancel();
                            _searchTimer = Timer(const Duration(milliseconds: 300), reload);
                          },
                        ),
                        hint: 'Search by name or number',
                        prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
                            FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.search)),
                      ),
                    ),
                    _checkBanner(theme),
                    if (_check.notOnWhatsApp > 0 || _showingNotOnWhatsApp)
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                        child: ChoiceChips<String?>(
                          options: <(String?, String)>[
                            (null, 'On WhatsApp'),
                            ('not_on_whatsapp', 'Not on WhatsApp ${_check.notOnWhatsApp}'),
                          ],
                          selected: _whatsApp,
                          onChange: (String? value) {
                            setState(() {
                              _whatsApp = value;
                              _selected = <String, Contact>{};
                            });
                            reload();
                          },
                        ),
                      ),
                    if (_showingNotOnWhatsApp)
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                        child: Text(
                          'These numbers have no WhatsApp. They are kept apart: they cannot be in groups, get no birthday '
                          'wishes and no messages. Open one to check it again.',
                          style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                        ),
                      ),
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                      child: ChoiceChips<String?>(
                        options: <(String?, String)>[
                          (null, 'All'),
                          for (final Category c in _categories) (c.id, '${c.name} ${c.contactCount}'),
                        ],
                        selected: _categoryId,
                        onChange: (String? id) {
                          setState(() => _categoryId = id);
                          reload();
                        },
                      ),
                    ),
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                      child: Row(
                        children: <Widget>[
                          Expanded(
                            child: Text(
                              '$_total ${_total == 1 ? 'contact' : 'contacts'}'
                              '${_canManage && !selecting ? ' · long-press to select' : ''}',
                              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                            ),
                          ),
                          FButton(
                            variant: _needsDetails ? FButtonVariant.primary : FButtonVariant.ghost,
                            size: FButtonSizeVariant.sm,
                            mainAxisSize: MainAxisSize.min,
                            onPress: () {
                              setState(() => _needsDetails = !_needsDetails);
                              reload();
                            },
                            child: const Text('Needs details'),
                          ),
                          if (_canManage)
                            FButton(
                              variant: FButtonVariant.ghost,
                              size: FButtonSizeVariant.sm,
                              mainAxisSize: MainAxisSize.min,
                              prefix: const Icon(FLucideIcons.fileSpreadsheet),
                              onPress: openImport,
                              child: const Text('Import'),
                            ),
                        ],
                      ),
                    ),
                    if (_error != null && _contacts.isEmpty)
                      EmptyView(
                        icon: FLucideIcons.wifiOff,
                        title: 'Could not load contacts',
                        text: _error!,
                        action: reload,
                        actionLabel: 'Try again',
                      ),
                    if (!_loading && _error == null && _contacts.isEmpty)
                      EmptyView(
                        icon: FLucideIcons.users,
                        title: _unfiltered ? 'No contacts yet' : null,
                        text: _unfiltered ? 'Add people one by one, or import them from Excel.' : 'Nothing matches.',
                        action: _canManage && _unfiltered ? add : null,
                        actionLabel: 'Add contact',
                      ),
                    if (_contacts.isNotEmpty)
                      CardSection(
                        children: <Widget>[
                          for (final Contact c in _contacts)
                            ListRow(
                              leading: selecting
                                  ? FCheckbox(value: _selected.containsKey(c.id), onChange: (_) => _toggleSelect(c))
                                  : PersonAvatar(name: c.displayName, size: 46),
                              title: c.displayName,
                              subtitle: _subtitle(c),
                              dimmed: c.optedOut || c.notOnWhatsApp,
                              badge: c.notOnWhatsApp
                                  ? Pill('Not on WhatsApp', color: theme.colors.destructive)
                                  : c.optedOut
                                      ? Pill('No broadcasts', color: theme.colors.destructive)
                                      : c.needsDetails
                                          ? const Pill('Needs details', color: Color(0xFFD97706))
                                          : c.waitingForCheck
                                              ? const Pill('Checking…', color: Color(0xFF0EA5E9))
                                              : null,
                              trailing: selecting ? null : Icon(FLucideIcons.chevronRight, size: 18, color: theme.colors.mutedForeground),
                              onTap: selecting ? () => _toggleSelect(c) : () => _open(c),
                              onLongPress: _canManage ? () => _toggleSelect(c) : null,
                            ),
                        ],
                      ),
                    if (_loading) const LoadingView(),
                  ],
                ),
              ),
            ),
          ),
          if (selecting) _bulkBar(theme),
        ],
      ),
    );
  }

  /// What the WhatsApp check is doing: numbers being checked, waiting for WhatsApp, or never checked.
  Widget _checkBanner(FThemeData theme) {
    final WhatsAppCheckSummary s = _check;
    final String? text = s.pending > 0
        ? s.connected
            ? 'Checking ${s.pending} ${s.pending == 1 ? 'number' : 'numbers'} on WhatsApp…'
            : '${s.pending} ${s.pending == 1 ? 'number waits' : 'numbers wait'} for the WhatsApp check. '
                'It starts when your WhatsApp is connected.'
        : s.unchecked > 0
            ? '${s.unchecked} ${s.unchecked == 1 ? 'contact was' : 'contacts were'} never checked on WhatsApp.'
            : null;
    if (text == null) {
      return const SizedBox.shrink();
    }

    final bool canStart = s.pending == 0 && s.unchecked > 0 && _canManage;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      child: Container(
        padding: const EdgeInsets.fromLTRB(12, 10, 8, 10),
        decoration: BoxDecoration(
          color: const Color(0xFF0EA5E9).withValues(alpha: 0.08),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: const Color(0xFF0EA5E9).withValues(alpha: 0.25)),
        ),
        child: Row(
          children: <Widget>[
            if (s.pending > 0 && s.connected)
              const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
            else
              const Icon(FLucideIcons.shieldCheck, size: 18, color: Color(0xFF0EA5E9)),
            const SizedBox(width: 10),
            Expanded(child: Text(text, style: theme.typography.body.sm)),
            if (canStart)
              FButton(
                size: FButtonSizeVariant.sm,
                mainAxisSize: MainAxisSize.min,
                onPress: _checkBusy ? null : _requestCheck,
                child: const Text('Check now'),
              ),
          ],
        ),
      ),
    );
  }
}

/// One contact: details and what can be done. Pops with message, edit, optout, delete or recheck.
class _ContactPage extends StatelessWidget {
  const _ContactPage({required this.contact, required this.canManage});

  final Contact contact;
  final bool canManage;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final Contact c = contact;
    final List<String> details = <String>[
      languageLabel(c.language),
      if (c.salutation != null) c.salutation!,
      if (c.birthday != null)
        'Birthday ${c.birthday!.day}/${c.birthday!.month}${c.birthday!.year != null ? '/${c.birthday!.year}' : ''}',
    ];
    return PageScaffold(
      title: c.displayName,
      subtitle: c.phoneNumber,
      child: ListView(
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          const SizedBox(height: 16),
          Center(child: PersonAvatar(name: c.displayName, size: 84)),
          const SizedBox(height: 10),
          Center(child: Text(c.displayName, style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w700))),
          if (c.realName != null && c.realName != c.displayName)
            Center(child: Text(c.realName!, style: theme.typography.body.md)),
          Center(child: Text(c.phoneNumber, style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground))),
          const SizedBox(height: 6),
          Center(
            child: Text(
              details.join(' · '),
              textAlign: TextAlign.center,
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
          if (c.optedOut)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Center(child: Pill('No broadcasts', color: theme.colors.destructive)),
            ),
          if (c.notOnWhatsApp)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
              child: FAlert(
                variant: FAlertVariant.destructive,
                icon: const Icon(FLucideIcons.circleAlert),
                title: const Text('Not on WhatsApp'),
                subtitle: const Text(
                  'This number has no WhatsApp. It is kept apart: it cannot be in groups and gets no messages. '
                  'If the person joined WhatsApp later, press "Check again".',
                ),
              ),
            )
          else if (c.waitingForCheck || c.neverChecked)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Center(
                child: Pill(c.waitingForCheck ? 'Checking on WhatsApp…' : 'Not checked on WhatsApp yet',
                    color: const Color(0xFF0EA5E9)),
              ),
            ),
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Row(
              children: <Widget>[
                Expanded(
                  child: FButton(
                    prefix: const Icon(FLucideIcons.messageCircle),
                    onPress: c.optedOut || c.notOnWhatsApp ? null : () => Navigator.of(context).pop('message'),
                    child: const Text('Send message'),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: FButton(
                    variant: FButtonVariant.outline,
                    prefix: const Icon(FLucideIcons.externalLink),
                    onPress: () => openInWhatsApp(context, c.phoneNumber),
                    child: const Text('Open WhatsApp'),
                  ),
                ),
              ],
            ),
          ),
          if (c.groups.isNotEmpty)
            CardSection(
              title: 'Groups',
              padding: const EdgeInsets.all(12),
              children: <Widget>[
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: <Widget>[
                    for (final ({String id, String name}) g in c.groups) Pill(g.name),
                  ],
                ),
              ],
            ),
          if (c.notes != null)
            CardSection(
              title: 'Notes',
              padding: const EdgeInsets.all(12),
              children: <Widget>[Text(c.notes!, style: theme.typography.body.sm)],
            ),
          if (canManage)
            CardSection(
              children: <Widget>[
                ListRow(
                  leading: const IconBadge(FLucideIcons.pencil),
                  title: 'Edit',
                  subtitle: 'Names, category, groups, birthday',
                  trailing: const Icon(FLucideIcons.chevronRight, size: 18),
                  onTap: () => Navigator.of(context).pop('edit'),
                ),
                if (c.notOnWhatsApp || c.neverChecked)
                  ListRow(
                    leading: const IconBadge(FLucideIcons.refreshCw, color: Color(0xFF0EA5E9)),
                    title: c.notOnWhatsApp ? 'Check again' : 'Check on WhatsApp',
                    subtitle: 'Asks WhatsApp whether this number has WhatsApp',
                    onTap: () => Navigator.of(context).pop('recheck'),
                  ),
                ListRow(
                  leading: IconBadge(c.optedOut ? FLucideIcons.bell : FLucideIcons.bellOff, color: const Color(0xFFD97706)),
                  title: c.optedOut ? 'Allow broadcasts again' : 'Stop broadcasts',
                  subtitle: c.optedOut ? 'They are skipped now.' : 'For people who do not want messages.',
                  onTap: () => Navigator.of(context).pop('optout'),
                ),
                ListRow(
                  leading: IconBadge(FLucideIcons.trash2, color: theme.colors.destructive),
                  title: 'Delete contact',
                  onTap: () => Navigator.of(context).pop('delete'),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

/// Choose one group. Returns it.
class _PickGroupPage extends StatelessWidget {
  const _PickGroupPage({required this.groups});

  final List<Group> groups;

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Add to group',
        child: ListView(
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: <Widget>[
            CardSection(
              children: <Widget>[
                for (final Group g in groups)
                  ListRow(
                    leading: const IconBadge(FLucideIcons.users, color: Color(0xFF6366F1)),
                    title: g.name,
                    subtitle: '${g.contactCount} ${g.contactCount == 1 ? 'member' : 'members'}',
                    trailing: const Icon(FLucideIcons.plus, size: 18),
                    onTap: () => Navigator.of(context).pop(g),
                  ),
              ],
            ),
          ],
        ),
      );
}
