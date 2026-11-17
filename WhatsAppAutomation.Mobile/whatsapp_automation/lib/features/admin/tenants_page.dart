import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import '../home/me_page.dart';
import 'admin_api.dart';
import 'admin_forms.dart';

/// The platform admin's whole app: the tenants, and Me (password, look, sign out).
class PlatformHomePage extends StatelessWidget {
  const PlatformHomePage({super.key});

  @override
  Widget build(BuildContext context) => const TenantsPage(showMe: true);
}

/// Tenants (platform admin): every tenant with counts. Never shows a tenant's contacts or messages.
/// Same screen as the website's Tenants page.
class TenantsPage extends ConsumerStatefulWidget {
  const TenantsPage({super.key, this.showMe = false});

  final bool showMe;

  @override
  ConsumerState<TenantsPage> createState() => _TenantsPageState();
}

class _TenantsPageState extends ConsumerState<TenantsPage> {
  List<Tenant>? _tenants;
  String? _loadError;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final List<Tenant> tenants = await ref.read(adminApiProvider).tenants();
      if (mounted) {
        setState(() {
          _loadError = null;
          _tenants = tenants;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        if (_tenants == null) {
          setState(() => _loadError = errorText(e));
        } else {
          showErrorToast(context, 'Could not load tenants', e);
        }
      }
    }
  }

  Future<void> _add() async {
    final bool? added = await pushPage<bool>(context, const _NewTenantPage());
    if (added ?? false) {
      await _load();
    }
  }

  Future<void> _open(Tenant t) async {
    await pushPage<void>(context, _TenantPage(tenant: t));
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    final List<Tenant>? tenants = _tenants;
    final int people = tenants?.fold<int>(0, (int n, Tenant t) => n + t.users) ?? 0;
    return PageScaffold(
      title: 'Tenants',
      subtitle: 'Counts only. Contacts and messages of tenants are never shown here.',
      actions: <Widget>[
        FHeaderAction(icon: const Icon(FLucideIcons.plus), onPress: _add),
        if (widget.showMe)
          FHeaderAction(
            icon: const Icon(FLucideIcons.userRound),
            onPress: () => pushPage<void>(context, const PageScaffold(title: 'Me', child: MePage())),
          ),
      ],
      child: tenants == null
          ? (_loadError == null
              ? const LoadingView()
              : EmptyView(
                  icon: FLucideIcons.wifiOff,
                  title: 'Could not load tenants',
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
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                    child: Row(
                      children: <Widget>[
                        _Kpi(label: 'Tenants', value: '${tenants.length}'),
                        const SizedBox(width: 10),
                        _Kpi(label: 'Turned on', value: '${tenants.where((Tenant t) => t.isActive).length}'),
                        const SizedBox(width: 10),
                        _Kpi(label: 'People', value: '$people'),
                      ],
                    ),
                  ),
                  if (tenants.isEmpty)
                    const EmptyView(icon: FLucideIcons.building2, title: 'No tenants yet', text: 'Add the first one with +.')
                  else
                    CardSection(
                      children: <Widget>[
                        for (final Tenant t in tenants)
                          ListRow(
                            leading: PersonAvatar(name: t.name, size: 40),
                            title: t.name,
                            subtitle: '${t.users} ${t.users == 1 ? 'person' : 'people'} · ${t.admins} ${t.admins == 1 ? 'admin' : 'admins'}',
                            dimmed: !t.isActive,
                            badge: t.isActive ? null : const Pill('Off', color: Color(0xFF98A2B3)),
                            trailing: const Icon(FLucideIcons.chevronRight, size: 18),
                            onTap: () => _open(t),
                          ),
                      ],
                    ),
                ],
              ),
            ),
    );
  }
}

class _Kpi extends StatelessWidget {
  const _Kpi({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Expanded(
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: theme.colors.card,
          border: Border.all(color: theme.colors.border),
          borderRadius: BorderRadius.circular(14),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text(label, style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground, fontWeight: FontWeight.w600)),
            const SizedBox(height: 4),
            Text(value, style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w700)),
          ],
        ),
      ),
    );
  }
}

class _NewTenantPage extends ConsumerStatefulWidget {
  const _NewTenantPage();

  @override
  ConsumerState<_NewTenantPage> createState() => _NewTenantPageState();
}

class _NewTenantPageState extends ConsumerState<_NewTenantPage> {
  final TextEditingController _name = TextEditingController();
  final TextEditingController _adminName = TextEditingController();
  final TextEditingController _adminUser = TextEditingController();
  final TextEditingController _adminPassword = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _adminName.dispose();
    _adminUser.dispose();
    _adminPassword.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty || _adminName.text.trim().isEmpty || !isValidUserName(_adminUser.text.trim())) {
      setState(() => _error = 'Fill in the tenant name, the admin\'s name and a user name (letters, digits, ".", "_" or "-").');
      return;
    }

    if (_adminPassword.text.length < 12) {
      setState(() => _error = 'The admin\'s first password needs at least 12 characters.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(adminApiProvider).createTenant(
            name: _name.text.trim(),
            adminUserName: _adminUser.text.trim(),
            adminDisplayName: _adminName.text.trim(),
            adminPassword: _adminPassword.text,
          );
      if (mounted) {
        showInfoToast(context, 'Tenant added', description: '${_adminName.text.trim()} chooses their own password at the first sign-in.');
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
        title: 'New tenant',
        footer: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: FButton(onPress: _saving ? null : _save, child: Text(_saving ? 'Adding…' : 'Add tenant')),
          ),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(
              control: FTextFieldControl.managed(controller: _name),
              label: const Text('Tenant name'),
              description: const Text('A business or a family, e.g. "Bhakti Enterprise".'),
              enabled: !_saving,
            ),
            const SizedBox(height: 12),
            FTextField(control: FTextFieldControl.managed(controller: _adminName), label: const Text('First admin\'s name'), enabled: !_saving),
            const SizedBox(height: 12),
            FTextField(
              control: FTextFieldControl.managed(controller: _adminUser),
              label: const Text('First admin\'s user name'),
              autocorrect: false,
              enabled: !_saving,
            ),
            const SizedBox(height: 12),
            FTextField.password(
              control: FTextFieldControl.managed(controller: _adminPassword),
              label: const Text('First password'),
              description: const Text('At least 12 characters. Share it privately; the admin chooses their own at the first sign-in.'),
              enabled: !_saving,
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}

class _TenantPage extends ConsumerStatefulWidget {
  const _TenantPage({required this.tenant});

  final Tenant tenant;

  @override
  ConsumerState<_TenantPage> createState() => _TenantPageState();
}

class _TenantPageState extends ConsumerState<_TenantPage> {
  late Tenant _t = widget.tenant;
  late final TextEditingController _name = TextEditingController(text: widget.tenant.name);
  List<TenantAdmin>? _admins;
  bool _saving = false;

  AdminApi get _api => ref.read(adminApiProvider);

  @override
  void initState() {
    super.initState();
    _reload();
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _reload() async {
    try {
      final (List<Tenant> tenants, List<TenantAdmin> admins) = await (_api.tenants(), _api.tenantAdmins(_t.id)).wait;
      final Tenant? fresh = tenants.where((Tenant x) => x.id == _t.id).firstOrNull;
      if (mounted) {
        setState(() {
          if (fresh != null) _t = fresh;
          _admins = admins;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Could not load', e);
      }
    }
  }

  Future<void> _run(Future<void> Function() action, String done) async {
    setState(() => _saving = true);
    try {
      await action();
      if (mounted) showInfoToast(context, done);
    } on Object catch (e) {
      if (mounted) showErrorToast(context, 'Not saved', e);
    } finally {
      await _reload();
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveName() async {
    final String name = _name.text.trim();
    if (name.isEmpty || name == _t.name) return;
    await _run(() => _api.renameTenant(_t.id, name), 'Name saved');
  }

  Future<void> _setActive(bool on) async {
    if (!on) {
      final bool yes = await showConfirmDialog(
        context: context,
        title: 'Turn off ${_t.name}?',
        body: 'Everyone in it is signed out and cannot sign in, and nothing is sent for it. Nothing is deleted; turn it on '
            'again at any time.',
        confirm: 'Turn off',
        destructive: true,
      );
      if (!yes) return;
    }

    await _run(() => _api.setTenantActive(_t.id, isActive: on), on ? 'Turned on' : 'Turned off');
  }

  Future<void> _delete() async {
    final bool yes = await showConfirmDialog(
      context: context,
      icon: FLucideIcons.trash2,
      title: 'Delete ${_t.name}?',
      body: 'All ${_t.users} people of this tenant are removed for good, with all their contacts, templates and messages, '
          'and their WhatsApp numbers are logged out. This cannot be undone.',
      confirm: 'Delete for good',
      destructive: true,
    );
    if (!yes || !mounted) return;

    setState(() => _saving = true);
    try {
      await _api.deleteTenant(_t.id);
      if (mounted) {
        showInfoToast(context, 'Deleted', description: _t.name);
        Navigator.of(context).pop();
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not deleted', e);
        setState(() => _saving = false);
      }
    }
  }

  Future<void> _addAdmin() async {
    final bool? added = await pushPage<bool>(context, _NewAdminPage(tenantId: _t.id));
    if (added ?? false) await _reload();
  }

  Future<void> _adminActions(TenantAdmin a) async {
    final String? choice = await showChoiceDialog<String>(
      context: context,
      title: a.displayName,
      body: a.userName,
      choices: <DialogChoice<String>>[
        (value: 'password', label: 'Set new password', variant: FButtonVariant.outline),
        (value: 'active', label: a.isActive ? 'Turn off' : 'Turn on', variant: FButtonVariant.outline),
        (value: 'delete', label: 'Delete', variant: FButtonVariant.destructive),
      ],
    );
    if (!mounted || choice == null) return;

    switch (choice) {
      case 'password':
        final String? password = await pushPage<String>(context, SetPasswordPage(name: a.displayName, minLength: 12));
        if (password != null) await _run(() => _api.resetTenantAdminPassword(_t.id, a.id, password), 'New password set');
      case 'active':
        await _run(() => _api.setTenantAdminActive(_t.id, a.id, isActive: !a.isActive), a.isActive ? 'Turned off' : 'Turned on');
      case 'delete':
        final bool yes = await showConfirmDialog(
          context: context,
          title: 'Delete ${a.displayName}?',
          body: 'The account is removed for good, with everything that is theirs. This cannot be undone.',
          confirm: 'Delete for good',
          destructive: true,
        );
        if (yes) await _run(() => _api.deleteTenantAdmin(_t.id, a.id), 'Deleted');
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: _t.name,
        subtitle: '${_t.users} ${_t.users == 1 ? 'person' : 'people'}',
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(
              control: FTextFieldControl.managed(controller: _name),
              label: const Text('Tenant name'),
              enabled: !_saving,
              onSubmit: (_) => _saveName(),
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: FButton(variant: FButtonVariant.outline, onPress: _saving ? null : _saveName, child: const Text('Save name')),
            ),
            const SizedBox(height: 8),
            CardSection(
              title: 'Admins',
              margin: EdgeInsets.zero,
              trailing: FButton(variant: FButtonVariant.ghost, onPress: _saving ? null : _addAdmin, child: const Text('Add admin')),
              children: <Widget>[
                if (_admins == null)
                  const Padding(padding: EdgeInsets.all(16), child: Center(child: FCircularProgress.loader()))
                else
                  for (final TenantAdmin a in _admins!)
                    ListRow(
                      leading: PersonAvatar(name: a.displayName, size: 40),
                      title: a.displayName,
                      subtitle: '${a.userName} · ${a.isActive ? 'On' : 'Turned off'}',
                      dimmed: !a.isActive,
                      trailing: const Icon(FLucideIcons.ellipsis, size: 18),
                      onTap: _saving ? null : () => _adminActions(a),
                    ),
              ],
            ),
            const Padding(
              padding: EdgeInsets.fromLTRB(4, 8, 4, 16),
              child: Text(
                'The last active admin of a tenant cannot be turned off or deleted. Add another admin first.',
                style: TextStyle(fontSize: 12, color: Color(0xFF667085)),
              ),
            ),
            FTileGroup(
              children: <FTileMixin>[
                FTile(
                  prefix: const Icon(FLucideIcons.power),
                  title: const Text('Tenant turned on'),
                  subtitle: Text(_t.isActive ? 'Its people can sign in and send' : 'Nobody in it can sign in; nothing is sent'),
                  suffix: FSwitch(value: _t.isActive, onChange: _saving ? null : _setActive),
                ),
              ],
            ),
            if (!_t.isActive) ...<Widget>[
              const SizedBox(height: 24),
              FButton(
                variant: FButtonVariant.destructive,
                prefix: const Icon(FLucideIcons.trash2),
                onPress: _saving ? null : _delete,
                child: const Text('Delete tenant'),
              ),
            ],
          ],
        ),
      );
}

class _NewAdminPage extends ConsumerStatefulWidget {
  const _NewAdminPage({required this.tenantId});

  final String tenantId;

  @override
  ConsumerState<_NewAdminPage> createState() => _NewAdminPageState();
}

class _NewAdminPageState extends ConsumerState<_NewAdminPage> {
  final TextEditingController _name = TextEditingController();
  final TextEditingController _user = TextEditingController();
  final TextEditingController _password = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _user.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty || !isValidUserName(_user.text.trim()) || _password.text.length < 12) {
      setState(() => _error = 'Fill in a name, a user name and a first password of at least 12 characters.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(adminApiProvider).addTenantAdmin(
            widget.tenantId,
            userName: _user.text.trim(),
            displayName: _name.text.trim(),
            password: _password.text,
          );
      if (mounted) {
        showInfoToast(context, 'Admin added');
        Navigator.of(context).pop(true);
      }
    } on Object catch (e) {
      if (mounted) setState(() => _error = errorText(e));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => PageScaffold(
        title: 'Add admin',
        footer: SafeArea(
          top: false,
          child: Padding(padding: const EdgeInsets.all(12), child: FButton(onPress: _saving ? null : _save, child: const Text('Add admin'))),
        ),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            FTextField(control: FTextFieldControl.managed(controller: _name), label: const Text('Name'), enabled: !_saving),
            const SizedBox(height: 12),
            FTextField(control: FTextFieldControl.managed(controller: _user), label: const Text('User name'), autocorrect: false, enabled: !_saving),
            const SizedBox(height: 12),
            FTextField.password(
              control: FTextFieldControl.managed(controller: _password),
              label: const Text('First password'),
              description: const Text('At least 12 characters.'),
              enabled: !_saving,
            ),
            if (_error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(variant: FAlertVariant.destructive, icon: const Icon(FLucideIcons.circleAlert), title: Text(_error!)),
            ],
          ],
        ),
      );
}
