import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import 'admin_api.dart';

/// Admin: the accounts. Create, turn on / off, change roles. Same as the website's Users page.
class UsersPage extends ConsumerStatefulWidget {
  const UsersPage({super.key});

  @override
  ConsumerState<UsersPage> createState() => _UsersPageState();
}

class _UsersPageState extends ConsumerState<UsersPage> {
  List<UserDetails>? _users;
  List<Role> _roles = <Role>[];
  String? _loadError;

  bool get _canManage {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.can('users.manage');
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final AdminApi api = ref.read(adminApiProvider);
      final (List<UserDetails> users, List<Role> roles) = await (api.users(), api.roles()).wait;
      if (mounted) {
        setState(() {
          _loadError = null;
          _users = users;
          _roles = roles;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_users == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load accounts', e);
        }
      }
    }
  }

  Future<void> _create() async {
    final bool? created = await pushPage<bool>(context, _NewUserPage(roles: _roles));
    if (created ?? false) {
      await _load();
    }
  }

  Future<void> _open(UserDetails u) async {
    if (!_canManage) {
      return;
    }

    final bool? changed = await pushPage<bool>(context, _UserPage(user: u, roles: _roles));
    if (changed ?? false) {
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Accounts',
        actions: <Widget>[
          if (_canManage) FHeaderAction(icon: const Icon(FLucideIcons.userPlus), onPress: _create),
        ],
        child: _users == null
            ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load accounts',
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
                    for (final UserDetails u in _users!)
                      ListRow(
                        leading: PersonAvatar(name: u.displayName, size: 44),
                        title: u.displayName,
                        subtitle: '${u.userName} · ${u.roles.isEmpty ? 'No role' : u.roles.map((({String id, String name}) r) => r.name).join(', ')}'
                            '${u.isActive ? '' : ' · Turned off'}',
                        dimmed: !u.isActive,
                        trailing: _canManage ? const Icon(FLucideIcons.chevronRight, size: 18) : null,
                        badge: u.isActive ? null : const Pill('Off', color: Color(0xFF94A3B8)),
                        onTap: () => _open(u),
                      ),
                      ],
                    ),
                  ],
                ),
              ),
      );
}

class _NewUserPage extends ConsumerStatefulWidget {
  const _NewUserPage({required this.roles});

  final List<Role> roles;

  @override
  ConsumerState<_NewUserPage> createState() => _NewUserPageState();
}

class _NewUserPageState extends ConsumerState<_NewUserPage> {
  final TextEditingController _userName = TextEditingController();
  final TextEditingController _displayName = TextEditingController();
  final TextEditingController _password = TextEditingController();
  Set<String> _roleIds = <String>{};
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _userName.dispose();
    _displayName.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_userName.text.trim().isEmpty || _displayName.text.trim().isEmpty || _password.text.isEmpty) {
      setState(() => _error = 'Enter the user name, the name and a first password.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(adminApiProvider).createUser(
            userName: _userName.text.trim(),
            displayName: _displayName.text.trim(),
            password: _password.text,
            roleIds: _roleIds.toList(),
          );
      if (mounted) {
        showInfoToast(context, 'Account created', description: _displayName.text.trim());
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      // The server checks the password rule and tells what is wrong.
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
        title: 'New account',
        footer: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: FButton(onPress: _saving ? null : _save, child: const Text('Create account')),
          ),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(
              control: FTextFieldControl.managed(controller: _userName),
              label: const Text('User name'),
              description: const Text('Used to sign in.'),
              autocorrect: false,
              enabled: !_saving,
            ),
            const SizedBox(height: 12),
            FTextField(
              control: FTextFieldControl.managed(controller: _displayName),
              label: const Text('Name'),
              enabled: !_saving,
            ),
            const SizedBox(height: 12),
            FTextField.password(
              control: FTextFieldControl.managed(controller: _password),
              label: const Text('First password'),
              enabled: !_saving,
            ),
            const SizedBox(height: 16),
            const SectionLabel('Roles'),
            MultiChoiceChips(
              options: <(String, String)>[for (final Role r in widget.roles) (r.id, r.name)],
              selected: _roleIds,
              onToggle: _saving
                  ? null
                  : (String id) => setState(() {
                        _roleIds = Set<String>.of(_roleIds);
                        if (!_roleIds.remove(id)) {
                          _roleIds.add(id);
                        }
                      }),
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}

class _UserPage extends ConsumerStatefulWidget {
  const _UserPage({required this.user, required this.roles});

  final UserDetails user;
  final List<Role> roles;

  @override
  ConsumerState<_UserPage> createState() => _UserPageState();
}

class _UserPageState extends ConsumerState<_UserPage> {
  late Set<String> _roleIds = widget.user.roles.map((({String id, String name}) r) => r.id).toSet();
  late bool _active = widget.user.isActive;
  bool _saving = false;
  bool _changed = false;

  Future<void> _setActive(bool on) async {
    if (!on) {
      final bool yes = await showConfirmDialog(
        context: context,
        title: 'Turn off ${widget.user.displayName}?',
        body: 'They can no longer sign in. You can turn the account on again later.',
        confirm: 'Turn off',
        destructive: true,
      );
      if (!yes) {
        return;
      }
    }

    setState(() => _saving = true);
    try {
      await ref.read(adminApiProvider).setUserActive(widget.user.id, isActive: on);
      if (mounted) {
        setState(() {
          _active = on;
          _changed = true;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not changed', e);
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  bool get _isMe {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.user.id == widget.user.id;
  }

  Future<void> _delete() async {
    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.userX,
      title: 'Delete ${widget.user.displayName}?',
      body: 'The account is removed for good, with all its contacts, groups, templates and messages. Its WhatsApp '
          'number is logged out. This cannot be undone.\n\nTo only stop them signing in, turn the account off instead.',
      confirm: 'Delete for good',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(adminApiProvider).deleteUser(widget.user.id);
      if (mounted) {
        showInfoToast(context, 'Account deleted', description: widget.user.displayName);
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    } finally {
      if (mounted) {
        setState(() => _saving = false);
      }
    }
  }

  Future<void> _saveRoles() async {
    setState(() => _saving = true);
    try {
      await ref.read(adminApiProvider).setUserRoles(widget.user.id, _roleIds.toList());
      if (mounted) {
        showInfoToast(context, 'Roles saved');
        Navigator.of(context).pop(true);
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

  @override
  Widget build(BuildContext context) => PopScope(
        canPop: !_changed,
        onPopInvokedWithResult: (bool didPop, Object? _) {
          if (!didPop) {
            Navigator.of(context).pop(true);
          }
        },
        child: PageScaffold(
          title: widget.user.displayName,
          subtitle: widget.user.userName,
          footer: SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: FButton(onPress: _saving ? null : _saveRoles, child: const Text('Save roles')),
            ),
          ),
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: <Widget>[
              FTileGroup(
                children: <FTileMixin>[
                  FTile(
                    prefix: const Icon(FLucideIcons.power),
                    title: const Text('Account turned on'),
                    subtitle: Text(_active ? 'Can sign in' : 'Cannot sign in'),
                    suffix: FSwitch(value: _active, onChange: _saving ? null : _setActive),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              const SectionLabel('Roles'),
              MultiChoiceChips(
                options: <(String, String)>[for (final Role r in widget.roles) (r.id, r.name)],
                selected: _roleIds,
                onToggle: _saving
                    ? null
                    : (String id) => setState(() {
                          _roleIds = Set<String>.of(_roleIds);
                          if (!_roleIds.remove(id)) {
                            _roleIds.add(id);
                          }
                        }),
              ),
              if (!_isMe) ...<Widget>[
                const SizedBox(height: 24),
                FButton(
                  variant: FButtonVariant.destructive,
                  prefix: const Icon(FLucideIcons.trash2),
                  onPress: _saving ? null : _delete,
                  child: const Text('Delete account'),
                ),
              ],
            ],
          ),
        ),
      );
}
