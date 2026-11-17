import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import 'admin_api.dart';
import 'admin_forms.dart';

/// The switches a tenant admin can turn on for a member, in the server's order. Same as the website.
const List<({String permission, String label, String sub, IconData icon})> memberSwitches =
    <({String permission, String label, String sub, IconData icon})>[
  (permission: 'modules.manage', label: 'Create own modules', sub: 'Birthday and other occasions, sent at a set time', icon: FLucideIcons.gift),
  (permission: 'broadcasts.send', label: 'Broadcasts', sub: 'Send to their own groups and lists', icon: FLucideIcons.megaphone),
  (permission: 'contacts.import', label: 'Import from Excel', sub: 'Add many contacts at once', icon: FLucideIcons.fileSpreadsheet),
  (permission: 'templates.manage', label: 'Templates', sub: 'Make and change message templates', icon: FLucideIcons.bookmark),
];

/// Members (tenant admin): everyone in the tenant. Tap a person to see and change what they may do.
/// Same screen as the website's Members page.
class MembersPage extends ConsumerStatefulWidget {
  const MembersPage({super.key});

  @override
  ConsumerState<MembersPage> createState() => _MembersPageState();
}

class _MembersPageState extends ConsumerState<MembersPage> {
  List<Member>? _members;
  String? _loadError;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final List<Member> members = await ref.read(adminApiProvider).members();
      if (mounted) {
        setState(() {
          _loadError = null;
          _members = members;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_members == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load members', e);
        }
      }
    }
  }

  String get _me {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn ? s.user.id : '';
  }

  String? get _tenantName {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn ? s.user.tenantName : null;
  }

  Future<void> _add() async {
    final bool? added = await pushPage<bool>(context, const _AddMemberPage());
    if (added ?? false) {
      await _load();
    }
  }

  Future<void> _open(Member m) async {
    if (m.id == _me) {
      showInfoToast(context, 'This is you', description: 'Change your own password under Me.');
      return;
    }

    await pushPage<void>(context, _MemberPage(member: m, onChanged: _load));
    await _load();
  }

  (String, Color) _line(Member m) {
    if (!m.isActive) return ('Turned off', const Color(0xFF98A2B3));
    final String you = m.id == _me ? 'You · ' : '';
    return switch (m.whatsAppStatus) {
      'connected' => ('${you}Connected${m.whatsAppNumber == null ? '' : ' · ${m.whatsAppNumber}'}', const Color(0xFF17B26A)),
      'not_linked' => ('${you}WhatsApp not linked yet', const Color(0xFFF79009)),
      'logged_out' || 'restricted' => ('${you}WhatsApp needs a look', const Color(0xFFD92D20)),
      _ => ('${you}Reconnecting', const Color(0xFFF79009)),
    };
  }

  @override
  Widget build(BuildContext context) {
    final List<Member>? members = _members;
    return PageScaffold(
      title: 'Members',
      subtitle: members == null ? _tenantName : '${_tenantName ?? ''} · ${members.length} ${members.length == 1 ? 'person' : 'people'}',
      actions: <Widget>[FHeaderAction(icon: const Icon(FLucideIcons.userPlus), onPress: _add)],
      child: members == null
          ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load members',
                  text: _loadError!,
                  action: () {
                    setState(() => _loadError = null);
                    _load();
                  },
                  actionLabel: 'Try again',
                ))
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.symmetric(vertical: 8),
                children: <Widget>[
                  CardSection(
                    children: <Widget>[
                      for (final Member m in members)
                        ListRow(
                          leading: PersonAvatar(name: m.displayName, size: 40),
                          title: m.displayName,
                          subtitle: '● ${_line(m).$1}',
                          dimmed: !m.isActive,
                          badge: Pill(
                            !m.isActive ? 'Off' : m.isAdmin ? 'Admin' : 'Member',
                            color: _line(m).$2,
                          ),
                          trailing: const Icon(FLucideIcons.chevronRight, size: 18),
                          onTap: () => _open(m),
                        ),
                    ],
                  ),
                  const Padding(
                    padding: EdgeInsets.fromLTRB(20, 4, 20, 16),
                    child: Text(
                      'Each member links their own WhatsApp number and keeps their own contacts. A number already used by '
                      'anyone on the platform is refused.',
                      style: TextStyle(fontSize: 12, color: Color(0xFF667085)),
                    ),
                  ),
                ],
              ),
            ),
    );
  }
}

class _AddMemberPage extends ConsumerStatefulWidget {
  const _AddMemberPage();

  @override
  ConsumerState<_AddMemberPage> createState() => _AddMemberPageState();
}

class _AddMemberPageState extends ConsumerState<_AddMemberPage> {
  final TextEditingController _displayName = TextEditingController();
  final TextEditingController _userName = TextEditingController();
  final TextEditingController _password = TextEditingController();
  bool _isAdmin = false;
  final Set<String> _switches = <String>{};
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _displayName.dispose();
    _userName.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final String userName = _userName.text.trim();
    final String name = _displayName.text.trim();
    final int min = _isAdmin ? 12 : 6;
    if (name.isEmpty || !isValidUserName(userName)) {
      setState(() => _error = 'Fill in a name and a user name (letters, digits, ".", "_" or "-").');
      return;
    }

    if (_password.text.length < min) {
      setState(() => _error = 'The first password needs at least $min characters.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(adminApiProvider).createMember(
            userName: userName,
            displayName: name,
            password: _password.text,
            isAdmin: _isAdmin,
            switches: _isAdmin ? <String>[] : _switches.toList(),
          );
      if (mounted) {
        showInfoToast(context, 'Added', description: '$name chooses their own password at the first sign-in.');
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Add member',
        footer: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: FButton(onPress: _saving ? null : _save, child: Text(_saving ? 'Adding…' : 'Add')),
          ),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(control: FTextFieldControl.managed(controller: _displayName), label: const Text('Name'), enabled: !_saving),
            const SizedBox(height: 12),
            FTextField(
              control: FTextFieldControl.managed(controller: _userName),
              label: const Text('User name'),
              description: const Text('Used to sign in. Letters, digits, ".", "_" and "-".'),
              autocorrect: false,
              enabled: !_saving,
            ),
            const SizedBox(height: 12),
            FTextField.password(
              control: FTextFieldControl.managed(controller: _password),
              label: const Text('First password'),
              description: Text('At least ${_isAdmin ? 12 : 6} characters. Share it privately; they choose their own at the first sign-in.'),
              enabled: !_saving,
            ),
            const SizedBox(height: 16),
            FTileGroup(
              children: <FTileMixin>[
                FTile(
                  prefix: const Icon(FLucideIcons.shield),
                  title: const Text('Admin'),
                  subtitle: const Text('May do everything, and manages members'),
                  suffix: FSwitch(value: _isAdmin, onChange: _saving ? null : (bool v) => setState(() => _isAdmin = v)),
                ),
                if (!_isAdmin)
                  for (final s in memberSwitches)
                    FTile(
                      prefix: Icon(s.icon),
                      title: Text(s.label),
                      subtitle: Text(s.sub),
                      suffix: FSwitch(
                        value: _switches.contains(s.permission),
                        onChange: _saving
                            ? null
                            : (bool v) => setState(() => v ? _switches.add(s.permission) : _switches.remove(s.permission)),
                      ),
                    ),
              ],
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}

class _MemberPage extends ConsumerStatefulWidget {
  const _MemberPage({required this.member, required this.onChanged});

  final Member member;
  final Future<void> Function() onChanged;

  @override
  ConsumerState<_MemberPage> createState() => _MemberPageState();
}

class _MemberPageState extends ConsumerState<_MemberPage> {
  late Member _m = widget.member;
  late final TextEditingController _name = TextEditingController(text: widget.member.displayName);
  bool _saving = false;

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  AdminApi get _api => ref.read(adminApiProvider);

  /// Saves, then reloads this person; shows the server's message if something is refused.
  Future<void> _run(Future<void> Function() action, String? done) async {
    setState(() => _saving = true);
    try {
      await action();
      await widget.onChanged();
      final List<Member> fresh = await _api.members();
      final Member? me = fresh.where((Member x) => x.id == _m.id).firstOrNull;
      if (mounted && me != null) {
        setState(() => _m = me);
      }
      if (mounted && done != null) {
        showInfoToast(context, done);
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not saved', e);
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  Future<void> _toggleSwitch(String permission, bool on) {
    final List<String> switches = <String>[
      for (final String s in _m.switches)
        if (s != permission) s,
      if (on) permission,
    ];
    return _run(() => _api.updateMember(_m.id, displayName: _m.displayName, isAdmin: false, switches: switches), null);
  }

  Future<void> _toggleAdmin(bool on) => _run(
        () => _api.updateMember(_m.id, displayName: _m.displayName, isAdmin: on, switches: _m.switches),
        on ? '${_m.displayName} is now an admin' : '${_m.displayName} is now a member',
      );

  Future<void> _saveName() async {
    final String name = _name.text.trim();
    if (name.isEmpty || name == _m.displayName) return;
    await _run(() => _api.updateMember(_m.id, displayName: name, isAdmin: _m.isAdmin, switches: _m.switches), 'Name saved');
  }

  Future<void> _setActive(bool on) async {
    if (!on) {
      final bool yes = await showConfirmDialog(
        context: context,
        title: 'Turn off ${_m.displayName}?',
        body: 'They are signed out on every device, cannot sign in, and nothing is sent from their number. Nothing is '
            'deleted; turn them on again at any time.',
        confirm: 'Turn off',
        destructive: true,
      );
      if (!yes) return;
    }

    await _run(() => _api.setMemberActive(_m.id, isActive: on), on ? 'Turned on' : 'Turned off');
  }

  Future<void> _resetPassword() async {
    final String? password = await pushPage<String>(context, SetPasswordPage(name: _m.displayName, minLength: _m.isAdmin ? 12 : 6));
    if (password != null) {
      await _run(() => _api.resetMemberPassword(_m.id, password), 'New password set');
    }
  }

  Future<void> _delete() async {
    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.userX,
      title: 'Delete ${_m.displayName}?',
      body: 'The account is removed for good, with all its contacts, groups, templates and messages. Its WhatsApp '
          'number is logged out. This cannot be undone.\n\nTo only stop them, turn them off instead.',
      confirm: 'Delete for good',
      destructive: true,
    );
    if (!yes || !mounted) return;

    setState(() => _saving = true);
    try {
      await _api.deleteMember(_m.id);
      if (mounted) {
        showInfoToast(context, 'Deleted', description: _m.displayName);
        Navigator.of(context).pop();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
        setState(() => _saving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: _m.displayName,
        subtitle: _m.userName,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(
              control: FTextFieldControl.managed(controller: _name),
              label: const Text('Name'),
              enabled: !_saving,
              onSubmit: (_) => _saveName(),
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: FButton(variant: FButtonVariant.outline, onPress: _saving ? null : _saveName, child: const Text('Save name')),
            ),
            const SizedBox(height: 16),
            SectionLabel('What ${_m.displayName.split(' ').first} may do'),
            FTileGroup(
              children: <FTileMixin>[
                FTile(
                  prefix: const Icon(FLucideIcons.shield),
                  title: const Text('Admin'),
                  subtitle: const Text('May do everything, and manages members'),
                  suffix: FSwitch(value: _m.isAdmin, onChange: _saving ? null : _toggleAdmin),
                ),
                if (!_m.isAdmin)
                  for (final s in memberSwitches)
                    FTile(
                      prefix: Icon(s.icon),
                      title: Text(s.label),
                      subtitle: Text(s.sub),
                      suffix: FSwitch(
                        value: _m.switches.contains(s.permission),
                        onChange: _saving || !_m.isActive ? null : (bool v) => _toggleSwitch(s.permission, v),
                      ),
                    ),
              ],
            ),
            const SizedBox(height: 16),
            FTileGroup(
              children: <FTileMixin>[
                FTile(
                  prefix: const Icon(FLucideIcons.power),
                  title: const Text('Turned on'),
                  subtitle: Text(_m.isActive ? 'Can sign in and send' : 'Cannot sign in; nothing is sent'),
                  suffix: FSwitch(value: _m.isActive, onChange: _saving ? null : _setActive),
                ),
                FTile(
                  prefix: const Icon(FLucideIcons.keyRound),
                  title: const Text('Set new password'),
                  subtitle: const Text('For someone who forgot theirs'),
                  suffix: const Icon(FLucideIcons.chevronRight),
                  onPress: _saving ? null : _resetPassword,
                ),
              ],
            ),
            const SizedBox(height: 24),
            FButton(
              variant: FButtonVariant.destructive,
              prefix: const Icon(FLucideIcons.trash2),
              onPress: _saving ? null : _delete,
              child: const Text('Delete'),
            ),
          ],
        ),
      );
}
