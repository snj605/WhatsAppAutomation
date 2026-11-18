import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/format.dart';
import '../../core/providers.dart';
import '../../core/ui.dart';
import '../files/files_api.dart';
import 'contacts_api.dart';

/// Import contacts from Excel or CSV: choose a file, check the preview, then import.
/// Nothing is saved until the user presses Import. Same steps and rules as the website.
/// Pops with true when contacts were imported.
class ImportPage extends ConsumerStatefulWidget {
  const ImportPage({super.key});

  @override
  ConsumerState<ImportPage> createState() => _ImportPageState();
}

class _ImportPageState extends ConsumerState<ImportPage> {
  PickedFile? _file;
  ImportResult? _result;
  bool _busy = false;
  bool _onlyProblems = false;
  String? _error;
  bool _downloading = false;

  Future<void> _downloadTemplate() async {
    setState(() {
      _downloading = true;
      _error = null;
    });
    try {
      final List<int> bytes = await ref.read(apiClientProvider).getBytes('/api/v1/contacts/import/template');
      final bool saved = await saveToPhone(
        fileName: 'contacts-template.xlsx',
        bytes: bytes,
        mimeType: contentTypeFor('xlsx'),
      );
      if (saved && mounted) {
        showInfoToast(context, 'Template saved', description: 'Fill it in, then choose it below.');
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _downloading = false);
      }
    }
  }

  Future<void> _choose() async {
    setState(() => _error = null);
    final PickedFile? file;
    try {
      file = await pickFile(extensions: const <String>['xlsx', 'csv']);
    } on Object catch (e) {
      setState(() => _error = errorText(e));
      return;
    }

    if (file == null) {
      return;
    }

    if (file.extension != 'xlsx' && file.extension != 'csv') {
      setState(() => _error = 'Choose an Excel (.xlsx) or CSV (.csv) file.');
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      setState(() => _error = 'The file is larger than 5 MB. Split it into smaller files.');
      return;
    }

    setState(() {
      _busy = true;
      _file = file;
      _result = null;
    });
    try {
      final ImportResult result = await ref.read(contactsApiProvider).importFile(file, apply: false);
      if (mounted) {
        setState(() => _result = result);
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _apply() async {
    final PickedFile? file = _file;
    if (file == null) {
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final ImportResult result = await ref.read(contactsApiProvider).importFile(file, apply: true);
      if (mounted) {
        setState(() => _result = result);
        if (result.created > 0) {
          // New numbers are checked on WhatsApp in the background; the Contacts tab shows the progress.
          showInfoToast(
            context,
            'Checking ${result.created} new ${result.created == 1 ? 'number' : 'numbers'} on WhatsApp',
            description: 'Numbers without WhatsApp move to "Not on WhatsApp" and leave their groups.',
          );
        }
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  void _restart() => setState(() {
        _file = null;
        _result = null;
        _error = null;
        _onlyProblems = false;
      });

  Widget _stat(FThemeData theme, int value, String label, Color color) => Expanded(
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 12),
          decoration: BoxDecoration(color: color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(12)),
          child: Column(
            children: <Widget>[
              Text('$value', style: theme.typography.display.xl.copyWith(color: color, fontWeight: FontWeight.w700)),
              Text(label, style: theme.typography.body.xs.copyWith(color: color, fontWeight: FontWeight.w600)),
            ],
          ),
        ),
      );

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final ImportResult? r = _result;
    final bool done = r?.applied ?? false;
    final Widget? footer = r == null
        ? null
        : SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: done
                  ? FButton(onPress: () => Navigator.of(context).pop(true), child: const Text('Done'))
                  : Row(
                      children: <Widget>[
                        Expanded(
                          child: FButton(
                            variant: FButtonVariant.outline,
                            onPress: _busy ? null : _restart,
                            child: const Text('Other file'),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          flex: 2,
                          child: FButton(
                            onPress: _busy || r.created + r.updated == 0 ? null : _apply,
                            prefix: _busy ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm) : null,
                            child: Text('Import ${r.created + r.updated}'),
                          ),
                        ),
                      ],
                    ),
            ),
          );

    return PopScope(
      canPop: !done,
      onPopInvokedWithResult: (bool didPop, Object? _) {
        if (!didPop) {
          Navigator.of(context).pop(true);
        }
      },
      child: PageScaffold(
        title: 'Import contacts',
        subtitle: 'From Excel or CSV',
        footer: footer,
        child: ListView(
          padding: const EdgeInsets.only(bottom: 24),
          children: <Widget>[
            if (r == null) ...<Widget>[
              CardSection(
                title: 'Columns in your file',
                padding: const EdgeInsets.all(14),
                children: <Widget>[
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      for (final (String name, String rule) in const <(String, String)>[
                        ('Phone *', 'with country code, e.g. +919876543210'),
                        ('Category', 'optional; a new name creates the category'),
                        ('Name (English / Hindi / Gujarati)', 'the one in the contact’s language is required'),
                        ('Language', 'en, hi or gu; empty uses your default'),
                        ('Groups', 'comma-separated'),
                        ('Each of your occasions (Birthday, Asthi, …)', 'DD-MM or DD-MM-YYYY; the template has a column for each'),
                        ('Each of your contact fields (City, …)', 'a column with the field’s name'),
                      ])
                        Padding(
                          padding: const EdgeInsets.only(bottom: 8),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: <Widget>[
                              Padding(
                                padding: const EdgeInsets.only(top: 5),
                                child: Icon(FLucideIcons.circle, size: 7, color: theme.colors.primary),
                              ),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Text.rich(
                                  TextSpan(
                                    children: <InlineSpan>[
                                      TextSpan(text: '$name  ', style: const TextStyle(fontWeight: FontWeight.w700)),
                                      TextSpan(text: rule, style: TextStyle(color: theme.colors.mutedForeground)),
                                    ],
                                  ),
                                  style: theme.typography.body.sm,
                                ),
                              ),
                            ],
                          ),
                        ),
                      Text(
                        'Use .xlsx for Hindi and Gujarati. If you use CSV, save it as "CSV UTF-8".',
                        style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                      ),
                      const SizedBox(height: 12),
                      FButton(
                        variant: FButtonVariant.outline,
                        prefix: _downloading
                            ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
                            : const Icon(FLucideIcons.download),
                        onPress: _downloading ? null : _downloadTemplate,
                        child: const Text('Download the Excel template'),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'With the right columns, dropdowns for Category and Language, and sample rows.',
                        style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                      ),
                    ],
                  ),
                ],
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: GestureDetector(
                  onTap: _busy ? null : _choose,
                  child: Container(
                    padding: const EdgeInsets.symmetric(vertical: 32, horizontal: 16),
                    decoration: BoxDecoration(
                      color: theme.colors.secondary,
                      borderRadius: BorderRadius.circular(16),
                      border: Border.all(color: theme.colors.primary.withValues(alpha: 0.35), width: 1.5),
                    ),
                    child: Column(
                      children: <Widget>[
                        if (_busy)
                          const FCircularProgress.loader(semanticsLabel: 'Reading the file')
                        else
                          Icon(FLucideIcons.fileSpreadsheet, size: 40, color: theme.colors.primary),
                        const SizedBox(height: 12),
                        Text(
                          _busy ? 'Reading ${_file?.name ?? 'the file'}…' : 'Choose an Excel or CSV file',
                          style: theme.typography.body.md.copyWith(
                            color: theme.colors.secondaryForeground,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          '.xlsx or .csv, up to 5 MB and 5000 contacts',
                          style: theme.typography.body.xs.copyWith(color: theme.colors.secondaryForeground),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ] else ...<Widget>[
              CardSection(
                padding: const EdgeInsets.all(14),
                children: <Widget>[
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: <Widget>[
                      Row(
                        children: <Widget>[
                          IconBadge(done ? FLucideIcons.circleCheck : FLucideIcons.fileSpreadsheet),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(
                                  done ? 'Import finished' : 'Check the preview',
                                  style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w700),
                                ),
                                Text(
                                  '${_file?.name ?? ''} · ${r.rows.length} rows · ${fileSizeText(_file?.size ?? 0)}',
                                  style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 12),
                      Row(
                        children: <Widget>[
                          _stat(theme, r.created, done ? 'Added' : 'New', theme.colors.primary),
                          const SizedBox(width: 8),
                          _stat(theme, r.updated, done ? 'Updated' : 'To update', const Color(0xFF3B82F6)),
                          const SizedBox(width: 8),
                          _stat(theme, r.failed, done ? 'Skipped' : 'Problems', theme.colors.destructive),
                        ],
                      ),
                      if (r.newCategories.isNotEmpty) ...<Widget>[
                        const SizedBox(height: 10),
                        Text(
                          '${done ? 'Created' : 'Will create'} categories: ${r.newCategories.join(', ')}',
                          style: theme.typography.body.sm,
                        ),
                      ],
                      if (r.newGroups.isNotEmpty) ...<Widget>[
                        const SizedBox(height: 6),
                        Text('${done ? 'Created' : 'Will create'} groups: ${r.newGroups.join(', ')}', style: theme.typography.body.sm),
                      ],
                      if (done && r.failed > 0) ...<Widget>[
                        const SizedBox(height: 10),
                        Text(
                          '${r.failed} rows with problems were not saved. Fix them in the file and import it again.',
                          style: theme.typography.body.sm.copyWith(color: theme.colors.destructive),
                        ),
                      ],
                    ],
                  ),
                ],
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: ChoiceChips<bool>(
                  options: const <(bool, String)>[(false, 'All rows'), (true, 'Only problems')],
                  selected: _onlyProblems,
                  onChange: (bool v) => setState(() => _onlyProblems = v),
                ),
              ),
              CardSection(
                children: <Widget>[
                  for (final ImportRow row in r.rows.where((ImportRow x) => !_onlyProblems || x.action == 'error').take(300))
                    ListRow(
                      leading: IconBadge(
                        row.action == 'create'
                            ? FLucideIcons.userPlus
                            : row.action == 'update'
                                ? FLucideIcons.pencil
                                : FLucideIcons.circleAlert,
                        size: 34,
                        color: row.action == 'error'
                            ? theme.colors.destructive
                            : row.action == 'update'
                                ? const Color(0xFF3B82F6)
                                : theme.colors.primary,
                      ),
                      title: '${row.row}. ${row.name ?? row.phoneNumber ?? '—'}',
                      subtitle: row.action == 'error'
                          ? row.errors.join(' ')
                          : '${row.phoneNumber ?? ''}${row.category != null ? ' · ${row.category}' : ''}',
                    ),
                ],
              ),
            ],
            if (_error != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: FAlert(
                  variant: FAlertVariant.destructive,
                  icon: const Icon(FLucideIcons.circleAlert),
                  title: Text(_error!),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
