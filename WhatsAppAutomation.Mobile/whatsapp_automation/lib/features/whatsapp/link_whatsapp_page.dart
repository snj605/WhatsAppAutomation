import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart' show SelectableText;

import '../../app/confirm_dialog.dart';
import '../../core/ui.dart';
import 'test_message_page.dart';
import 'whatsapp_controller.dart';
import 'whatsapp_models.dart';

/// Links the user's WhatsApp in one of two ways, the same as the website:
/// - a QR shown here, scanned by the phone that has WhatsApp: easiest when this app runs on a second phone or a
///   tablet;
/// - a code typed into WhatsApp: for when WhatsApp is on this same phone (it cannot scan its own screen).
class LinkWhatsAppPage extends ConsumerStatefulWidget {
  const LinkWhatsAppPage({super.key});

  @override
  ConsumerState<LinkWhatsAppPage> createState() => _LinkWhatsAppPageState();
}

class _LinkWhatsAppPageState extends ConsumerState<LinkWhatsAppPage> with WidgetsBindingObserver {
  final TextEditingController _phone = TextEditingController(text: '+91');
  String? _phoneError;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _phone.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Coming back from WhatsApp: ask the server whether the link is done.
    if (state == AppLifecycleState.resumed) {
      ref.read(whatsAppProvider.notifier).refresh();
    }
  }

  Future<void> _getCode() async {
    final String? phone = normalizePhone(_phone.text);
    if (phone == null) {
      setState(() => _phoneError = 'Type the full number with country code, like +919825012345.');
      return;
    }

    setState(() => _phoneError = null);
    FocusScope.of(context).unfocus();
    await ref.read(whatsAppProvider.notifier).linkWithCode(phone);
  }

  Future<void> _copy(String code) async {
    await Clipboard.setData(ClipboardData(text: code));
    if (mounted) {
      showFToast(context: context, icon: const Icon(FLucideIcons.copy), title: const Text('Code copied'));
    }
  }

  Future<void> _unlink() async {
    final bool yes = await showConfirmDialog(
      context: context,
      title: 'Unlink this WhatsApp number?',
      body: 'Sending stops until a number is linked again.',
      confirm: 'Unlink',
      destructive: true,
    );
    if (yes) {
      await ref.read(whatsAppProvider.notifier).unlink();
      if (mounted && ref.read(whatsAppProvider).account?.statusReason == 'unlinked_remove_on_phone') {
        await showConfirmDialog(
          context: context,
          title: 'Remove it on the phone too',
          body: 'The number was unlinked here, but WhatsApp could not be reached, so the device may still show on the '
              'phone. On the phone open WhatsApp → Settings → Linked devices and remove it.',
          confirm: 'OK',
          cancel: 'Close',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final WhatsAppState state = ref.watch(whatsAppProvider);
    final WhatsAppAccount? account = state.account;
    final FThemeData theme = context.theme;

    final List<Widget> content;
    if (state.loading) {
      content = const <Widget>[
        SizedBox(height: 80),
        Center(child: FCircularProgress.loader(semanticsLabel: 'Loading')),
      ];
    } else if (account != null && account.isLinked) {
      content = _linked(theme, state, account);
    } else if (state.method == 'qr') {
      content = _qr(theme, state);
    } else if (state.pairingCode != null) {
      content = _code(theme, state, state.pairingCode!);
    } else {
      content = _form(theme, state, account);
    }

    return FScaffold(
      childPad: false,
      header: FHeader.nested(
        title: const Text('WhatsApp'),
        titleAlignment: AlignmentDirectional.centerStart,
        prefixes: <Widget>[FHeaderAction.back(onPress: () => Navigator.of(context).maybePop())],
      ),
      child: SafeArea(
        top: false,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: <Widget>[
            ...content,
            if (state.error != null) ...<Widget>[
              const SizedBox(height: 16),
              FAlert(
                variant: FAlertVariant.destructive,
                icon: const Icon(FLucideIcons.circleAlert),
                title: Text(state.error!),
              ),
            ],
          ],
        ),
      ),
    );
  }

  List<Widget> _linked(FThemeData theme, WhatsAppState state, WhatsAppAccount account) => <Widget>[
        FCard(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              children: <Widget>[
                Icon(
                  account.isConnected ? FLucideIcons.circleCheck : FLucideIcons.refreshCw,
                  size: 56,
                  color: account.isConnected ? theme.colors.primary : theme.colors.mutedForeground,
                ),
                const SizedBox(height: 12),
                Text(
                  account.label,
                  textAlign: TextAlign.center,
                  style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w600),
                ),
                if (account.phoneNumber != null) ...<Widget>[
                  const SizedBox(height: 4),
                  Text(account.phoneNumber!, style: theme.typography.body.md),
                ],
                if (account.profileName != null)
                  Text(
                    account.profileName!,
                    style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
                  ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16),
        if (account.status == 'disconnected' || account.status == 'reconnecting') ...<Widget>[
          FButton(
            onPress: state.busy ? null : () => ref.read(whatsAppProvider.notifier).reconnect(),
            prefix: const Icon(FLucideIcons.refreshCw),
            child: const Text('Reconnect (no new QR)'),
          ),
          const SizedBox(height: 10),
        ],
        FButton(
          variant: FButtonVariant.outline,
          onPress: state.busy ? null : _unlink,
          prefix: const Icon(FLucideIcons.unlink),
          child: const Text('Unlink this number'),
        ),
        if (account.isConnected) ...<Widget>[
          const SizedBox(height: 10),
          FButton(
            prefix: const Icon(FLucideIcons.send),
            onPress: () => pushPage<void>(context, const TestMessagePage()),
            child: const Text('Send a test message'),
          ),
        ],
      ];

  List<Widget> _form(FThemeData theme, WhatsAppState state, WhatsAppAccount? account) => <Widget>[
        Text('Link your WhatsApp', style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 8),
        Text(
          'Choose the way that fits: a QR code to scan, or a code to type in WhatsApp.',
          style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
        ),
        if (account != null && (account.status == 'logged_out' || account.status == 'restricted')) ...<Widget>[
          const SizedBox(height: 12),
          Text(account.label, style: TextStyle(color: theme.colors.error)),
        ],
        const SizedBox(height: 20),
        Text('1. Scan a QR code', style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 4),
        Text(
          'Best when WhatsApp is on another phone and this app is on a second phone or a tablet.',
          style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
        ),
        const SizedBox(height: 12),
        FButton(
          onPress: state.busy ? null : () => ref.read(whatsAppProvider.notifier).linkWithQr(),
          prefix: const Icon(FLucideIcons.qrCode),
          child: const Text('Show QR code'),
        ),
        const SizedBox(height: 28),
        Text('2. Use a code', style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 4),
        Text(
          'When WhatsApp is on this same phone. Type its number; you get a code to enter in WhatsApp.',
          style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
        ),
        const SizedBox(height: 12),
        FTextField(
          control: FTextFieldControl.managed(controller: _phone),
          enabled: !state.busy,
          keyboardType: TextInputType.phone,
          inputFormatters: <TextInputFormatter>[FilteringTextInputFormatter.allow(RegExp(r'[0-9+\s]'))],
          onSubmit: (_) => _getCode(),
          label: const Text('WhatsApp number'),
          hint: '+919825012345',
          prefixBuilder: (BuildContext context, FTextFieldStyle style, Set<FTextFieldVariant> variants) =>
              FTextField.prefixIconBuilder(context, style, variants, const Icon(FLucideIcons.phone)),
          error: _phoneError == null ? null : Text(_phoneError!),
        ),
        const SizedBox(height: 24),
        FButton(
          onPress: state.busy ? null : _getCode,
          prefix: state.busy || account?.status == 'linking'
              ? const FCircularProgress.loader(size: FCircularProgressSizeVariant.sm)
              : null,
          child: const Text('Get the code'),
        ),
        if (!state.busy && account?.status == 'linking' && state.method == 'code') ...<Widget>[
          const SizedBox(height: 12),
          const Text('Waiting for the code… If nothing appears in a minute, press the button again.', textAlign: TextAlign.center),
        ],
      ];

  /// The QR from the server (it changes about every 20 seconds), with the steps for the phone that scans it.
  List<Widget> _qr(FThemeData theme, WhatsAppState state) => <Widget>[
        Text('Scan this QR code', style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 16),
        Center(
          child: Container(
            width: 280,
            height: 280,
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(color: const Color(0xFFFFFFFF), borderRadius: BorderRadius.circular(12)),
            child: state.qrImage == null
                ? const Center(child: FCircularProgress.loader(semanticsLabel: 'Waiting for the QR code'))
                : Image.memory(state.qrImage!, gaplessPlayback: true, filterQuality: FilterQuality.none, semanticLabel: 'QR code to link WhatsApp'),
          ),
        ),
        const SizedBox(height: 24),
        const _Step(number: 1, text: 'On the phone that has WhatsApp, open WhatsApp.'),
        const _Step(number: 2, text: 'Tap the three dots (Android) or Settings (iPhone), then Linked devices.'),
        const _Step(number: 3, text: 'Tap Link a device and point the phone at this screen.'),
        const SizedBox(height: 8),
        Text(
          'The code changes every 20 seconds; that is normal. This screen changes by itself when the link is done.',
          style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
        ),
        const SizedBox(height: 16),
        FButton(
          variant: FButtonVariant.ghost,
          onPress: state.busy ? null : () => ref.read(whatsAppProvider.notifier).chooseAgain(),
          child: const Text('Use a code instead'),
        ),
      ];

  List<Widget> _code(FThemeData theme, WhatsAppState state, String code) => <Widget>[
        Text('Enter this code in WhatsApp', style: theme.typography.display.xl.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 16),
        FCard(
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 24, horizontal: 16),
            child: Column(
              children: <Widget>[
                SelectableText(
                  formatPairingCode(code),
                  style: theme.typography.display.xl3.copyWith(
                    fontWeight: FontWeight.w700,
                    letterSpacing: 4,
                    color: theme.colors.primary,
                  ),
                ),
                const SizedBox(height: 12),
                FButton(
                  variant: FButtonVariant.outline,
                  mainAxisSize: MainAxisSize.min,
                  onPress: () => _copy(code),
                  prefix: const Icon(FLucideIcons.copy),
                  child: const Text('Copy code'),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24),
        const _Step(number: 1, text: 'Open WhatsApp on this phone.'),
        const _Step(number: 2, text: 'Tap the three dots (Android) or Settings (iPhone), then Linked devices.'),
        const _Step(number: 3, text: 'Tap Link a device, then "Link with phone number instead".'),
        const _Step(number: 4, text: 'Type or paste the code. Then come back here.'),
        const SizedBox(height: 16),
        Text(
          'The code works for a few minutes. This screen changes by itself when the link is done.',
          style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
        ),
        const SizedBox(height: 16),
        FButton(
          variant: FButtonVariant.ghost,
          onPress: state.busy ? null : _getCode,
          child: const Text('Get a new code'),
        ),
      ];
}

class _Step extends StatelessWidget {
  const _Step({required this.number, required this.text});

  final int number;
  final String text;

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          FAvatar.raw(
            size: 28,
            child: Text('$number', style: theme.typography.body.xs),
          ),
          const SizedBox(width: 12),
          Expanded(child: Text(text, style: theme.typography.body.md)),
        ],
      ),
    );
  }
}
