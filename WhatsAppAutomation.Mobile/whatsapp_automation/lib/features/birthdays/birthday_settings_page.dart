import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/ui.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';
import '../templates/templates_api.dart';
import '../templates/templates_page.dart';

/// Automatic birthday wishes: on or off, the time they start each day and the template everyone gets.
/// The template's name placeholders make each wish personal. Pops with true when saved.
class BirthdaySettingsPage extends ConsumerStatefulWidget {
  const BirthdaySettingsPage({super.key, this.settings});

  final BirthdaySettings? settings;

  @override
  ConsumerState<BirthdaySettingsPage> createState() => _BirthdaySettingsPageState();
}

class _BirthdaySettingsPageState extends ConsumerState<BirthdaySettingsPage> {
  late bool _enabled = widget.settings?.enabled ?? false;
  late FTime _time = _parse(widget.settings?.time) ?? const FTime(7, 15);
  late String? _templateId = widget.settings?.templateId;
  List<MessageTemplate>? _templates;
  String? _error;
  bool _saving = false;

  static FTime? _parse(String? hhmm) {
    final List<String>? parts = hhmm?.split(':');
    if (parts == null || parts.length != 2) {
      return null;
    }

    final int? h = int.tryParse(parts[0]);
    final int? m = int.tryParse(parts[1]);
    return h == null || m == null || h > 23 || m > 59 ? null : FTime(h, m);
  }

  String get _hhmm => '${_time.hour.toString().padLeft(2, '0')}:${_time.minute.toString().padLeft(2, '0')}';

  @override
  void initState() {
    super.initState();
    _loadTemplates();
  }

  Future<void> _loadTemplates() async {
    try {
      final List<MessageTemplate> list = await ref.read(templatesApiProvider).list();
      if (mounted) {
        setState(() {
          _templates = list;
          if (_templateId != null && list.every((MessageTemplate t) => t.id != _templateId)) {
            _templateId = null;
          }
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  Future<void> _save() async {
    if (_enabled && _templateId == null) {
      setState(() => _error = 'Choose the template to send.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final BirthdaySettings saved =
          await ref.read(contactsApiProvider).saveBirthdaySettings(enabled: _enabled, time: _hhmm, templateId: _templateId);
      if (!mounted) {
        return;
      }

      showInfoToast(
        context,
        saved.ready ? 'Automatic wishes are on' : 'Automatic wishes are off',
        description: saved.ready ? 'Next: ${whenText(saved.nextRunAt!)}' : null,
      );
      Navigator.of(context).pop(true);
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
    final List<MessageTemplate>? templates = _templates;
    final MessageTemplate? chosen = templates?.where((MessageTemplate t) => t.id == _templateId).firstOrNull;
    return PageScaffold(
      title: 'Automatic wishes',
      subtitle: 'Birthdays',
      footer: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            prefix: _saving ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
            onPress: _saving ? null : _save,
            child: const Text('Save'),
          ),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.only(bottom: 24),
        children: <Widget>[
          CardSection(
            children: <Widget>[
              ListRow(
                leading: const IconBadge(FLucideIcons.alarmClock, color: Color(0xFFEC4899)),
                title: 'Send birthday wishes by themselves',
                subtitle: 'Every day at the time below, to everyone with a birthday that day',
                trailing: FSwitch(value: _enabled, onChange: _saving ? null : (bool v) => setState(() => _enabled = v)),
                onTap: _saving ? null : () => setState(() => _enabled = !_enabled),
              ),
            ],
          ),
          CardSection(
            title: 'Time · starts at ${clockText(DateTime(2000, 1, 1, _time.hour, _time.minute))}',
            padding: const EdgeInsets.all(12),
            children: <Widget>[
              SizedBox(
                height: 180,
                child: FTimePicker(
                  control: FTimePickerControl.managed(initial: _time, onChange: (FTime t) => setState(() => _time = t)),
                  minuteInterval: 5,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                'Wishes go out one by one, a little apart, like a person sending them. With 50 birthdays the last one '
                'goes out within about 30–45 minutes. Birthday wishes always go before other messages.',
                style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
              ),
            ],
          ),
          CardSection(
            title: 'Template',
            children: <Widget>[
              if (templates == null && _error == null) const LoadingView(),
              if (templates != null && templates.isEmpty)
                ListRow(
                  leading: const IconBadge(FLucideIcons.filePlus),
                  title: 'No templates yet',
                  subtitle: 'Make one with {{name}} in it, then choose it here',
                  trailing: const Icon(FLucideIcons.chevronRight, size: 18),
                  onTap: () async {
                    await pushPage<void>(context, const TemplatesPage());
                    await _loadTemplates();
                  },
                ),
              for (final MessageTemplate t in templates ?? const <MessageTemplate>[])
                ListRow(
                  leading: FCheckbox(value: t.id == _templateId, onChange: (_) => setState(() => _templateId = t.id)),
                  title: t.name,
                  subtitle: sampleText(t.text),
                  onTap: () => setState(() => _templateId = t.id),
                ),
            ],
          ),
          if (chosen != null)
            CardSection(
              title: 'What people get (example)',
              padding: const EdgeInsets.all(12),
              children: <Widget>[SelectableText(sampleText(chosen.text), style: theme.typography.body.sm)],
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              child: FAlert(
                variant: FAlertVariant.destructive,
                icon: const Icon(FLucideIcons.circleAlert),
                title: Text(_error!),
              ),
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            child: Text(
              'If WhatsApp is not connected at that time, the app asks you to reconnect, and the wishes go out as soon as '
              'it is connected again. If you switch this on after today\'s time has passed, it starts tomorrow; '
              'you can still send today\'s by hand from the Birthdays list.',
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
        ],
      ),
    );
  }
}
