import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../auth/auth_models.dart';
import '../auth/session_controller.dart';
import 'admin_api.dart';

/// Admin: roles and what each may do. Same as the website's Roles page.
class RolesPage extends ConsumerStatefulWidget {
  const RolesPage({super.key});

  @override
  ConsumerState<RolesPage> createState() => _RolesPageState();
}

class _RolesPageState extends ConsumerState<RolesPage> {
  List<Role>? _roles;
  List<Permission> _permissions = <Permission>[];
  String? _loadError;

  bool get _canManage {
    final SessionState s = ref.read(sessionProvider);
    return s is SignedIn && s.can('roles.manage');
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final AdminApi api = ref.read(adminApiProvider);
      final (List<Role> roles, List<Permission> permissions) = await (api.roles(), api.permissions()).wait;
      if (mounted) {
        setState(() {
          _loadError = null;
          _roles = roles;
          _permissions = permissions;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_roles == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load roles', e);
        }
      }
    }
  }

  Future<void> _edit(Role? role) async {
    final bool? saved = await pushPage<bool>(
      context,
      // The server decides what may change in a built-in role and says so if a change is refused.
      _RolePage(role: role, permissions: _permissions, readOnly: !_canManage),
    );
    if (saved ?? false) {
      await _load();
    }
  }

  Future<void> _delete(Role role) async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Delete role "${role.name}"?',
      body: role.userCount == 0
          ? 'Nobody has this role.'
          : '${role.userCount} ${role.userCount == 1 ? 'account has' : 'accounts have'} this role and will lose it.',
      confirm: 'Delete',
      destructive: true,
    );
    if (!yes || !mounted) {
      return;
    }

    try {
      await ref.read(adminApiProvider).deleteRole(role.id);
      await _load();
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
      }
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Roles',
        actions: <Widget>[
          if (_canManage) FHeaderAction(icon: const Icon(FLucideIcons.plus), onPress: () => _edit(null)),
        ],
        child: _roles == null
            ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load roles',
                  text: _loadError!,
                  action: () {
                    setState(() => _loadError = null);
                    _load();
                  },
                  actionLabel: 'Try again',
                ))
            : ListView(
                padding: const EdgeInsets.symmetric(vertical: 8),
                children: <Widget>[
                  CardSection(
                    children: <Widget>[
                  for (final Role r in _roles!)
                    ListRow(
                      leading: IconBadge(r.isSystem ? FLucideIcons.shieldCheck : FLucideIcons.shield, color: const Color(0xFF6366F1)),
                      title: r.name,
                      subtitle: '${r.permissions.length} permissions · ${r.userCount} '
                          '${r.userCount == 1 ? 'account' : 'accounts'}${r.isSystem ? ' · Built in' : ''}',
                      trailing: _canManage && !r.isSystem
                          ? FButton.icon(
                              variant: FButtonVariant.ghost,
                              onPress: () => _delete(r),
                              child: const Icon(FLucideIcons.trash2),
                            )
                          : null,
                      onTap: () => _edit(r),
                    ),
                    ],
                  ),
                ],
              ),
      );
}

class _RolePage extends ConsumerStatefulWidget {
  const _RolePage({required this.role, required this.permissions, required this.readOnly});

  final Role? role;
  final List<Permission> permissions;
  final bool readOnly;

  @override
  ConsumerState<_RolePage> createState() => _RolePageState();
}

class _RolePageState extends ConsumerState<_RolePage> {
  late final TextEditingController _name = TextEditingController(text: widget.role?.name ?? '');
  late final TextEditingController _description = TextEditingController(text: widget.role?.description ?? '');
  late Set<String> _granted = Set<String>.of(widget.role?.permissions ?? const <String>[]);
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty) {
      setState(() => _error = 'Enter a name.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(adminApiProvider).saveRole(
            widget.role?.id,
            name: _name.text.trim(),
            description: _description.text.trim().isEmpty ? null : _description.text.trim(),
            permissions: _granted.toList(),
          );
      if (mounted) {
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
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final Map<String, List<Permission>> byGroup = <String, List<Permission>>{};
    for (final Permission p in widget.permissions) {
      byGroup.putIfAbsent(p.group, () => <Permission>[]).add(p);
    }

    final bool locked = widget.readOnly || _saving;
    return PageScaffold(
      title: widget.role == null ? 'New role' : widget.role!.name,
      footer: widget.readOnly
          ? null
          : SafeArea(
              top: false,
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: FButton(onPress: _saving ? null : _save, child: const Text('Save role')),
              ),
            ),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          FTextField(control: FTextFieldControl.managed(controller: _name), label: const Text('Name'), enabled: !locked),
          const SizedBox(height: 12),
          FTextField(
            control: FTextFieldControl.managed(controller: _description),
            label: const Text('Description (optional)'),
            enabled: !locked,
          ),
          const SizedBox(height: 16),
          for (final MapEntry<String, List<Permission>> g in byGroup.entries) ...<Widget>[
            SectionLabel(g.key),
            for (final Permission p in g.value)
              Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: FCheckbox(
                  value: _granted.contains(p.name),
                  enabled: !locked,
                  label: Text(p.description.isEmpty ? p.name : p.description),
                  description: Text(p.name, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
                  onChange: (bool on) => setState(() {
                    _granted = Set<String>.of(_granted);
                    if (on) {
                      _granted.add(p.name);
                    } else {
                      _granted.remove(p.name);
                    }
                  }),
                ),
              ),
          ],
          if (_error != null) ...<Widget>[
            const SizedBox(height: 12),
            FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
          ],
        ],
      ),
    );
  }
}
