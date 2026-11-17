import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../core/ui.dart';
import 'update_controller.dart';

/// Wraps the whole app. A required update replaces every screen until it is installed;
/// an optional one shows a bar at the top with Update and Later.
class UpdateGate extends ConsumerStatefulWidget {
  const UpdateGate({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<UpdateGate> createState() => _UpdateGateState();
}

class _UpdateGateState extends ConsumerState<UpdateGate> with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) => ref.read(updateProvider.notifier).checkDaily());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Phones stay open for days: check again when the app comes back (still at most once a day).
    if (state == AppLifecycleState.resumed) {
      ref.read(updateProvider.notifier).checkDaily();
    }
  }

  @override
  Widget build(BuildContext context) {
    final UpdateState update = ref.watch(updateProvider);
    final AppRelease? release = update.release;
    if (release == null) {
      return widget.child;
    }

    if (release.required) {
      return UpdateRequiredPage(update: update);
    }

    if (update.dismissed && !update.busy) {
      return widget.child;
    }

    return Column(
      children: <Widget>[
        _UpdateBar(update: update),
        Expanded(child: widget.child),
      ],
    );
  }
}

class _UpdateBar extends ConsumerWidget {
  const _UpdateBar({required this.update});

  final UpdateState update;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    final AppRelease release = update.release!;
    const Color white = Color(0xFFFFFFFF);
    return Container(
      color: theme.colors.primary,
      child: SafeArea(
        bottom: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Row(
                children: <Widget>[
                  const Icon(FLucideIcons.download, size: 18, color: white),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      update.installing
                          ? 'Installing… confirm on the next screen'
                          : update.progress != null
                              ? 'Downloading update ${update.progress}%'
                              : 'Update available: version ${release.version}',
                      style: theme.typography.body.sm.copyWith(color: white, fontWeight: FontWeight.w700),
                    ),
                  ),
                  if (!update.busy) ...<Widget>[
                    GestureDetector(
                      onTap: () => ref.read(updateProvider.notifier).dismiss(),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                        child: Text('Later', style: theme.typography.body.sm.copyWith(color: const Color(0xD9FFFFFF))),
                      ),
                    ),
                    GestureDetector(
                      onTap: () => ref.read(updateProvider.notifier).install(),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                        decoration: BoxDecoration(color: white, borderRadius: BorderRadius.circular(999)),
                        child: Text(
                          'Update',
                          style: theme.typography.body.sm.copyWith(color: theme.colors.primary, fontWeight: FontWeight.w700),
                        ),
                      ),
                    ),
                  ],
                ],
              ),
              if (update.progress != null) ...<Widget>[
                const SizedBox(height: 6),
                Container(
                  height: 4,
                  alignment: Alignment.centerLeft,
                  decoration: BoxDecoration(color: const Color(0x40FFFFFF), borderRadius: BorderRadius.circular(999)),
                  child: FractionallySizedBox(
                    widthFactor: (update.progress! / 100).clamp(0.0, 1.0),
                    child: Container(
                      decoration: BoxDecoration(color: white, borderRadius: BorderRadius.circular(999)),
                    ),
                  ),
                ),
              ],
              if (update.error != null) ...<Widget>[
                const SizedBox(height: 4),
                Text(update.error!, style: theme.typography.body.xs.copyWith(color: white)),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Shown instead of the app when an update is required.
class UpdateRequiredPage extends ConsumerWidget {
  const UpdateRequiredPage({super.key, required this.update});

  final UpdateState update;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final FThemeData theme = context.theme;
    final AppRelease release = update.release!;
    return FScaffold(
      childPad: false,
      child: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: <Widget>[
            const SizedBox(height: 32),
            const Center(child: IconBadge(FLucideIcons.download, size: 88)),
            const SizedBox(height: 20),
            Text(
              'Update needed',
              textAlign: TextAlign.center,
              style: theme.typography.display.xl2.copyWith(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 6),
            Text(
              'Version ${release.version} is required to keep using the app.'
              '${update.currentVersion.isEmpty ? '' : ' You have ${update.currentVersion}.'}',
              textAlign: TextAlign.center,
              style: theme.typography.body.sm.copyWith(color: theme.colors.mutedForeground),
            ),
            if (release.notes.isNotEmpty)
              CardSection(
                title: "What's new",
                margin: const EdgeInsets.only(top: 20),
                padding: const EdgeInsets.all(14),
                children: <Widget>[Text(release.notes, style: theme.typography.body.sm)],
              ),
            const SizedBox(height: 24),
            if (update.progress != null) ...<Widget>[
              FDeterminateProgress(value: update.progress! / 100),
              const SizedBox(height: 8),
              Text('Downloading ${update.progress}%', textAlign: TextAlign.center, style: theme.typography.body.sm),
            ] else if (update.installing)
              Text(
                'Confirm "Install" on the next screen.',
                textAlign: TextAlign.center,
                style: theme.typography.body.md.copyWith(fontWeight: FontWeight.w600),
              )
            else
              FButton(
                prefix: const Icon(FLucideIcons.download),
                onPress: () => ref.read(updateProvider.notifier).install(),
                child: const Text('Update now'),
              ),
            if (update.error != null) ...<Widget>[
              const SizedBox(height: 12),
              FAlert(
                variant: FAlertVariant.destructive,
                icon: const Icon(FLucideIcons.circleAlert),
                title: Text(update.error!),
              ),
            ],
            const SizedBox(height: 16),
            Text(
              'Your contacts, messages and sign-in stay as they are.',
              textAlign: TextAlign.center,
              style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground),
            ),
          ],
        ),
      ),
    );
  }
}
