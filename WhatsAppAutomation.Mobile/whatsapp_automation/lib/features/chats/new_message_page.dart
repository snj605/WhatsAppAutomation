import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/ui.dart';
import '../broadcasts/broadcast_models.dart';
import '../broadcasts/broadcasts_api.dart';
import '../contacts/contact_models.dart';
import '../contacts/contacts_api.dart';
import '../files/files_api.dart';
import '../preview/wa_bubble.dart';
import '../preview/wa_person_preview.dart';
import '../whatsapp/whatsapp_gate.dart';
import 'broadcast_page.dart';
import 'message_bar.dart';

/// People and a text to start with, for example birthday wishes from the Birthdays tab.
class ComposerPreset {
  const ComposerPreset({required this.name, required this.people, required this.text, this.moduleId, this.attachment});

  final String name;
  final List<({String id, String label})> people;
  final String text;

  /// Wishes of an occasion module: they go before other messages and show on the module's page.
  final String? moduleId;

  /// The module template's file, sent with the text.
  final UploadedFile? attachment;
}

/// A new message in three steps, like the website: 1. Who  2. Message  3. Check and send.
class NewMessagePage extends ConsumerStatefulWidget {
  const NewMessagePage({super.key, this.preset});

  final ComposerPreset? preset;

  @override
  ConsumerState<NewMessagePage> createState() => _NewMessagePageState();
}

class _NewMessagePageState extends ConsumerState<NewMessagePage> {
  static const int _page = 200;

  final TextEditingController _text = TextEditingController();
  final TextEditingController _search = TextEditingController();

  int _step = 1;

  /// all, categories, groups or people.
  String? _audience;
  List<Category> _categories = <Category>[];
  List<Group> _groups = <Group>[];
  int _allCount = 0;
  Set<String> _categoryIds = <String>{};
  Set<String> _groupIds = <String>{};
  Map<String, String> _picked = <String, String>{};
  List<Contact> _contacts = <Contact>[];
  int _contactsTotal = 0;
  bool _loadingContacts = false;
  int _loadSeq = 0;
  Timer? _searchTimer;

  BroadcastPreview? _preview;
  bool _previewing = false;
  int _sampleIndex = 0;
  String _sampleLanguage = 'any';
  bool _sending = false;
  UploadedFile? _attachment;
  bool _uploading = false;

  @override
  void initState() {
    super.initState();
    final ComposerPreset? preset = widget.preset;
    if (preset != null) {
      _text.text = preset.text;
      _attachment = preset.attachment;
      _picked = <String, String>{for (final ({String id, String label}) p in preset.people) p.id: p.label};
      _audience = 'people';
      _step = 2;
    }

    _text.addListener(() => setState(() {}));
    _loadLists();
  }

  @override
  void dispose() {
    _searchTimer?.cancel();
    _text.dispose();
    _search.dispose();
    super.dispose();
  }

  Future<void> _loadLists() async {
    try {
      final ContactsApi api = ref.read(contactsApiProvider);
      final (List<Category> categories, List<Group> groups, ContactPage all) =
          await (api.categories(), api.groups(), api.contacts(pageSize: 1)).wait;
      if (mounted) {
        setState(() {
          _categories = categories;
          _groups = groups;
          _allCount = all.total;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Could not load your contacts', e);
      }
    }
  }

  Future<void> _loadContacts() async {
    final int seq = ++_loadSeq;
    setState(() => _loadingContacts = true);
    try {
      final ContactPage page = await ref.read(contactsApiProvider).contacts(search: _search.text, pageSize: _page);
      if (mounted && seq == _loadSeq) {
        setState(() {
          _contacts = page.items;
          _contactsTotal = page.total;
        });
      }
    } on Object catch (e) {
      if (mounted && seq == _loadSeq) {
        showErrorToast(context, 'Could not load contacts', e);
      }
    } finally {
      if (mounted && seq == _loadSeq) {
        setState(() => _loadingContacts = false);
      }
    }
  }

  Future<void> _pickAllFound() async {
    try {
      final List<Contact> all = List<Contact>.of(_contacts);
      for (int page = 2; all.length < _contactsTotal; page++) {
        final ContactPage more =
            await ref.read(contactsApiProvider).contacts(search: _search.text, page: page, pageSize: _page);
        if (more.items.isEmpty) {
          break;
        }

        all.addAll(more.items);
      }

      if (mounted) {
        setState(() => _picked = <String, String>{..._picked, for (final Contact c in all) c.id: c.displayName});
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Could not select everyone', e);
      }
    }
  }

  int get _roughCount => switch (_audience) {
        'all' => _allCount,
        'categories' => _categories.where((Category c) => _categoryIds.contains(c.id)).fold(0, (int n, Category c) => n + c.contactCount),
        'groups' => _groups.where((Group g) => _groupIds.contains(g.id)).fold(0, (int n, Group g) => n + g.contactCount),
        'people' => _picked.length,
        _ => 0,
      };

  String get _whoLabel {
    switch (_audience) {
      case 'all':
        return 'All contacts';
      case 'categories':
        return _categories.where((Category c) => _categoryIds.contains(c.id)).map((Category c) => c.name).join(', ');
      case 'groups':
        return _groups.where((Group g) => _groupIds.contains(g.id)).map((Group g) => g.name).join(', ');
      case 'people':
        final List<String> names = _picked.values.toList();
        return names.length <= 3 ? names.join(', ') : '${names.take(3).join(', ')} and ${names.length - 3} more';
      default:
        return '';
    }
  }

  BroadcastInput _input() {
    final String text = _text.text.trim();
    return BroadcastInput(
      name: widget.preset?.name ?? chatName(text),
      text: text,
      attachmentId: _attachment?.id,
      sendVideoAsDocument: _attachment?.isBigVideo ?? false,
      allContacts: _audience == 'all',
      moduleId: _audience == 'people' ? widget.preset?.moduleId : null,
      categoryIds: _audience == 'categories' ? _categoryIds.toList() : const <String>[],
      groupIds: _audience == 'groups' ? _groupIds.toList() : const <String>[],
      contactIds: _audience == 'people' ? _picked.keys.toList() : const <String>[],
    );
  }

  Future<void> _goToCheck() async {
    setState(() {
      _step = 3;
      _preview = null;
      _previewing = true;
      _sampleIndex = 0;
    });
    try {
      final BroadcastPreview preview = await ref.read(broadcastsApiProvider).preview(_input());
      if (mounted) {
        setState(() => _preview = preview);
      }
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Could not check the message', e);
      }
    } finally {
      if (mounted) {
        setState(() => _previewing = false);
      }
    }
  }

  Future<void> _send() async {
    if (_sending || (_preview?.willSend ?? 0) == 0) {
      return;
    }

    setState(() => _sending = true);
    if (!await ensureWhatsAppConnected(context, ref) || !mounted) {
      if (mounted) {
        setState(() => _sending = false);
      }

      return;
    }

    try {
      final BroadcastSummary created = await ref.read(broadcastsApiProvider).create(_input());
      if (!mounted) {
        return;
      }

      showInfoToast(context, 'Sending started', description: '${created.counts.pending} people will get it one by one.');
      await Navigator.of(context).pushReplacement(MaterialPageRoute<void>(builder: (_) => BroadcastPage(id: created.id)));
    } on Object catch (e) {
      if (mounted) {
        showErrorToast(context, 'Not sent', e);
      }
    } finally {
      if (mounted) {
        setState(() => _sending = false);
      }
    }
  }

  void _back() {
    if (_step == 1 || (widget.preset != null && _step == 2)) {
      Navigator.of(context).maybePop();
    } else {
      setState(() => _step -= 1);
    }
  }

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final (String title, Widget body, Widget footer) = switch (_step) {
      1 => ('1. Who gets it?', _who(theme), _footer('Next: write the message', _roughCount > 0 ? (() => setState(() => _step = 2)) : null)),
      2 => ('2. Message', _message(theme), _footer('Next: check', _text.text.trim().isNotEmpty ? _goToCheck : null)),
      _ => ('3. Check and send', _check(theme), _footer(
            _preview == null ? 'Send' : 'Send to ${_preview!.willSend} ${_preview!.willSend == 1 ? 'person' : 'people'}',
            (_preview?.willSend ?? 0) > 0 && !_sending ? _send : null,
            icon: FLucideIcons.send,
            busy: _sending,
          )),
    };
    return PopScope(
      canPop: _step == 1 || (widget.preset != null && _step == 2),
      onPopInvokedWithResult: (bool didPop, Object? _) {
        if (!didPop) {
          _back();
        }
      },
      child: FScaffold(
        childPad: false,
        header: FHeader.nested(
          titleAlignment: AlignmentDirectional.centerStart,
          title: Text(title),
          prefixes: <Widget>[FHeaderAction.back(onPress: _back)],
        ),
        footer: footer,
        child: body,
      ),
    );
  }

  Widget _footer(String label, VoidCallback? onPress, {IconData? icon, bool busy = false}) => SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FButton(
            onPress: onPress,
            prefix: busy
                ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
                : (icon == null ? null : Icon(icon)),
            child: Text(label),
          ),
        ),
      );

  Widget _who(FThemeData theme) => ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          ChoiceChips<String>(
            options: const <(String, String)>[
              ('all', 'All contacts'),
              ('categories', 'Categories'),
              ('groups', 'Groups'),
              ('people', 'Choose people'),
            ],
            selected: _audience,
            onChange: (String a) {
              setState(() => _audience = a);
              if (a == 'people' && _contacts.isEmpty) {
                _loadContacts();
              }
            },
          ),
          const SizedBox(height: 16),
          if (_audience == null)
            Text('Choose who should get the message.', style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground)),
          if (_audience == 'all')
            FAlert(
              icon: const Icon(FLucideIcons.users),
              title: Text('Everyone in your contacts: $_allCount'),
              subtitle: const Text('People who stopped broadcasts are skipped.'),
            ),
          if (_audience == 'categories')
            _pickList(
              theme,
              <(String, String, int)>[for (final Category c in _categories) (c.id, c.name, c.contactCount)],
              _categoryIds,
              (Set<String> s) => setState(() => _categoryIds = s),
              'No categories yet. Add them under Contacts › Categories & groups.',
            ),
          if (_audience == 'groups')
            _pickList(
              theme,
              <(String, String, int)>[for (final Group g in _groups) (g.id, g.name, g.contactCount)],
              _groupIds,
              (Set<String> s) => setState(() => _groupIds = s),
              'No groups yet. Add them under Contacts › Categories & groups.',
            ),
          if (_audience == 'people') ..._people(theme),
        ],
      );

  Widget _pickList(
    FThemeData theme,
    List<(String, String, int)> items,
    Set<String> selected,
    ValueChanged<Set<String>> onChange,
    String empty,
  ) {
    if (items.isEmpty) {
      return Text(empty, style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground));
    }

    return CardSection(
      margin: EdgeInsets.zero,
      children: <Widget>[
        for (final (String id, String name, int count) in items)
          ListRow(
            leading: FCheckbox(
              value: selected.contains(id),
              onChange: (bool on) => onChange(on ? (Set<String>.of(selected)..add(id)) : (Set<String>.of(selected)..remove(id))),
            ),
            title: name,
            subtitle: '$count ${count == 1 ? 'person' : 'people'}',
            onTap: () => onChange(
              selected.contains(id) ? (Set<String>.of(selected)..remove(id)) : (Set<String>.of(selected)..add(id)),
            ),
          ),
      ],
    );
  }

  List<Widget> _people(FThemeData theme) => <Widget>[
        FTextField(
          control: FTextFieldControl.managed(
            controller: _search,
            onChange: (_) {
              _searchTimer?.cancel();
              _searchTimer = Timer(const Duration(milliseconds: 300), _loadContacts);
            },
          ),
          hint: 'Search by name or number',
          prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
              FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.search)),
        ),
        const SizedBox(height: 8),
        Row(
          children: <Widget>[
            Expanded(
              child: Text(
                '${_picked.length} chosen',
                style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
              ),
            ),
            if (_picked.isNotEmpty)
              FButton(
                variant: FButtonVariant.ghost,
                size: FButtonSizeVariant.sm,
                mainAxisSize: MainAxisSize.min,
                onPress: () => setState(() => _picked = <String, String>{}),
                child: const Text('Clear'),
              ),
            if (_contacts.isNotEmpty)
              FButton(
                variant: FButtonVariant.ghost,
                size: FButtonSizeVariant.sm,
                mainAxisSize: MainAxisSize.min,
                onPress: _pickAllFound,
                child: Text('Select all $_contactsTotal'),
              ),
          ],
        ),
        if (_loadingContacts && _contacts.isEmpty) const LoadingView(),
        if (_contacts.isNotEmpty)
          CardSection(
            margin: EdgeInsets.zero,
            children: <Widget>[
              for (final Contact c in _contacts)
                ListRow(
                  leading: FCheckbox(
                    value: _picked.containsKey(c.id),
                    onChange: (_) => _togglePerson(c),
                  ),
                  title: c.displayName,
                  subtitle: c.optedOut ? 'No broadcasts · ${c.phoneNumber}' : c.phoneNumber,
                  trailing: PersonAvatar(name: c.displayName, size: 34),
                  dimmed: c.optedOut,
                  onTap: () => _togglePerson(c),
                ),
            ],
          ),
        if (_contactsTotal > _contacts.length)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Text(
              'Showing ${_contacts.length} of $_contactsTotal. Search to find others.',
              textAlign: TextAlign.center,
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ),
      ];

  void _togglePerson(Contact c) => setState(() {
        _picked = Map<String, String>.of(_picked);
        if (_picked.remove(c.id) == null) {
          _picked[c.id] = c.displayName;
        }
      });

  Widget _message(FThemeData theme) => ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          FAlert(
            icon: const Icon(FLucideIcons.users),
            title: Text('To: $_whoLabel'),
            subtitle: Text('About $_roughCount ${_roughCount == 1 ? 'person' : 'people'}'),
          ),
          const SizedBox(height: 16),
          ComposerTools(
            controller: _text,
            attaching: _uploading,
            onTemplateFile: (UploadedFile f) => setState(() => _attachment = f),
            onAttach: () async {
              setState(() => _uploading = true);
              final UploadedFile? file = await pickAndUpload(context, ref);
              if (mounted) {
                setState(() {
                  _uploading = false;
                  _attachment = file ?? _attachment;
                });
              }
            },
          ),
          if (_attachment != null) ...<Widget>[
            const SizedBox(height: 8),
            AttachmentChip(file: _attachment!, onRemove: () => setState(() => _attachment = null)),
          ],
          const SizedBox(height: 8),
          FTextField.multiline(
            control: FTextFieldControl.managed(controller: _text),
            hint: 'Type your message. Tap Name to put in each person’s name.',
            minLines: 5,
            maxLines: 12,
          ),
          const SizedBox(height: 8),
          Text(
            'Each person gets their own message: [Name] becomes their name.',
            style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
          ),
          if (_text.text.trim().isNotEmpty) ...<Widget>[
            const SizedBox(height: 16),
            const SectionLabel('How it looks on WhatsApp'),
            WaPersonPreview(text: _text.text.trim(), file: WaFile.upload(_attachment), moduleId: _audience == 'people' ? widget.preset?.moduleId : null),
          ],
        ],
      );

  Widget _check(FThemeData theme) {
    final BroadcastPreview? p = _preview;
    if (_previewing || p == null) {
      return const LoadingView();
    }

    final int skipped = p.total - p.willSend;
    final List<String> sampleLanguages =
        p.samples.map((PreviewSample x) => x.language).where((String l) => l != 'all').toSet().toList();
    final List<PreviewSample> samples = _sampleLanguage == 'any'
        ? p.samples
        : p.samples.where((PreviewSample x) => x.language == _sampleLanguage).toList();
    final PreviewSample? sample = samples.isEmpty ? null : samples[_sampleIndex % samples.length];
    final Map<String, int> reasons = <String, int>{};
    for (final SkippedPerson s in p.skipped) {
      reasons[s.reason] = (reasons[s.reason] ?? 0) + 1;
    }

    return ListView(
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        FAlert(
          variant: p.willSend == 0 ? FAlertVariant.destructive : FAlertVariant.primary,
          icon: Icon(p.willSend == 0 ? FLucideIcons.circleAlert : FLucideIcons.circleCheck),
          title: Text(
            p.willSend == 0
                ? (p.total == 0 ? 'Nobody to send to. Go back and choose who should get it.' : 'Nobody can get this message as written.')
                : '${p.willSend} ${p.willSend == 1 ? 'person' : 'people'} will get it',
          ),
          subtitle: Text(
            '${skipped > 0 ? '$skipped will be skipped. ' : ''}'
            '${p.estimatedDays > 1 ? 'Sending takes about ${p.estimatedDays} days.' : 'They go out one by one.'}',
          ),
        ),
        if (sampleLanguages.length > 1) ...<Widget>[
          const SizedBox(height: 16),
          ChoiceChips<String>(
            options: <(String, String)>[
              ('any', 'Everyone'),
              for (final String l in sampleLanguages) (l, languageLabel(l)),
            ],
            selected: _sampleLanguage,
            onChange: (String l) => setState(() {
              _sampleLanguage = l;
              _sampleIndex = 0;
            }),
          ),
        ],
        if (sample != null) ...<Widget>[
          const SizedBox(height: 16),
          SectionLabel('${sample.contactName} will get'),
          WaBubble(text: sample.text, file: WaFile.upload(_attachment), chip: sample.contactName),
          if (samples.length > 1)
            Align(
              alignment: Alignment.centerRight,
              child: FButton(
                variant: FButtonVariant.ghost,
                size: FButtonSizeVariant.sm,
                mainAxisSize: MainAxisSize.min,
                onPress: () => setState(() => _sampleIndex += 1),
                child: Text('Show another person (${(_sampleIndex % samples.length) + 1} of ${samples.length})'),
              ),
            ),
        ],
        if (reasons.isNotEmpty) ...<Widget>[
          const SizedBox(height: 16),
          const SectionLabel('Skipped'),
          for (final MapEntry<String, int> r in reasons.entries)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Text('${r.value} · ${r.key}', style: theme.typography.body.sm),
            ),
        ],
        const SizedBox(height: 16),
        Text(
          'You can pause or stop any time.',
          style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
        ),
      ],
    );
  }
}
