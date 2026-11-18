import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';

import '../../app/theme.dart';
import '../../core/api/api_client.dart';
import '../../core/format.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../../core/ui.dart';
import '../home/me_page.dart';
import '../sending/status_look.dart';
import 'tenants_page.dart';

/// Shapes of /api/v1/platform/dashboard. Counts only, by design: no contacts or message text.
class Dashboard {
  const Dashboard({
    required this.tenants,
    required this.activeTenants,
    required this.users,
    required this.numbersConnected,
    required this.numbers,
    required this.messagesToday,
    required this.failedToday,
    required this.rows,
    required this.alerts,
    required this.activity,
  });

  factory Dashboard.fromJson(Json json) => Dashboard(
        tenants: asInt(json['tenants']),
        activeTenants: asInt(json['activeTenants']),
        users: asInt(json['users']),
        numbersConnected: asInt(json['numbersConnected']),
        numbers: asInt(json['numbers']),
        messagesToday: asInt(json['messagesToday']),
        failedToday: asInt(json['failedToday']),
        rows: asList(json['tenantRows']),
        alerts: asList(json['alerts']),
        activity: asList(json['recentActivity']),
      );

  final int tenants;
  final int activeTenants;
  final int users;
  final int numbersConnected;
  final int numbers;
  final int messagesToday;
  final int failedToday;
  final List<Json> rows;
  final List<Json> alerts;
  final List<Json> activity;
}

/// The platform admin's start screen, the same as the website's Dashboard: KPIs, numbers that need a look, each
/// organisation's numbers and messages, and recent activity. Tenants and Me are in the header.
class PlatformHomePage extends ConsumerStatefulWidget {
  const PlatformHomePage({super.key});

  @override
  ConsumerState<PlatformHomePage> createState() => _PlatformHomePageState();
}

class _PlatformHomePageState extends ConsumerState<PlatformHomePage> {
  Dashboard? _data;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final ApiClient api = ref.read(apiClientProvider);
      final Dashboard d = Dashboard.fromJson((await api.get('/api/v1/platform/dashboard'))! as Json);
      if (mounted) {
        setState(() {
          _data = d;
          _error = null;
        });
      }
    } on Object catch (e) {
      if (mounted) {
        setState(() => _error = errorText(e));
      }
    }
  }

  Widget _kpi(FThemeData theme, String label, String value, [String? note]) => Expanded(
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: theme.colors.card,
            border: Border.all(color: theme.colors.border),
            borderRadius: BorderRadius.circular(14),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(label, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
              const SizedBox(height: 4),
              Text(value, style: theme.typography.body.xl.copyWith(fontWeight: FontWeight.w700)),
              if (note != null) Text(note, style: theme.typography.body.xs.copyWith(color: theme.colors.mutedForeground)),
            ],
          ),
        ),
      );

  @override
  Widget build(BuildContext context) {
    final FThemeData theme = context.theme;
    final Dashboard? d = _data;
    return FScaffold(
      childPad: false,
      header: FHeader(
        title: const Text('Dashboard'),
        suffixes: <Widget>[
          FHeaderAction(
            icon: const Icon(FLucideIcons.building2),
            onPress: () async {
              await pushPage<void>(context, const TenantsPage());
              await _load();
            },
          ),
          FHeaderAction(
            icon: const Icon(FLucideIcons.userRound),
            onPress: () => pushPage<void>(context, const PageScaffold(title: 'Me', child: MePage())),
          ),
        ],
      ),
      child: d == null
          ? (_error == null
              ? const LoadingView()
              : EmptyView(icon: FLucideIcons.wifiOff, title: 'Could not load the dashboard', text: _error!, action: _load, actionLabel: 'Try again'))
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.symmetric(vertical: 8),
                children: <Widget>[
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                    child: Column(
                      spacing: 8,
                      children: <Widget>[
                        Row(
                          spacing: 8,
                          children: <Widget>[
                            _kpi(theme, 'Organisations', '${d.tenants}', '${d.activeTenants} active'),
                            _kpi(theme, 'People', '${d.users}'),
                          ],
                        ),
                        Row(
                          spacing: 8,
                          children: <Widget>[
                            _kpi(theme, 'Numbers connected', '${d.numbersConnected} / ${d.numbers}'),
                            _kpi(theme, 'Messages today', '${d.messagesToday}', '${d.failedToday} failed'),
                          ],
                        ),
                      ],
                    ),
                  ),
                  if (d.alerts.isNotEmpty)
                    CardSection(
                      title: 'Needs a look',
                      children: <Widget>[
                        for (final Json a in d.alerts)
                          ListRow(
                            leading: StatusDot('', color: AppTheme.bad),
                            title: '${a['tenantName']} · ${a['userName']}${a['numberEnd'] == null ? '' : ' · ••${a['numberEnd']}'}',
                            subtitle: <String>[
                              a['status'] == 'number_in_use' ? 'Number used by another account' : '${a['status']}'.replaceAll('_', ' '),
                              if (asDateOrNull(a['since']) != null) 'since ${whenText(asDateOrNull(a['since'])!)}',
                            ].join(' · '),
                          ),
                      ],
                    ),
                  CardSection(
                    title: 'Organisations',
                    children: <Widget>[
                      if (d.rows.isEmpty) const ListRow(leading: IconBadge(FLucideIcons.building2), title: 'No organisations yet'),
                      for (final Json t in d.rows)
                        ListRow(
                          leading: PersonAvatar(name: '${t['name']}', size: 38),
                          title: '${t['name']}',
                          subtitle: '${t['users']} people · ${t['numbersConnected']}/${t['numbers']} numbers · '
                              '${t['sentToday']} sent today${asInt(t['failedToday']) > 0 ? ' · ${t['failedToday']} failed' : ''}',
                          dimmed: !asBool(t['isActive']),
                        ),
                    ],
                  ),
                  CardSection(
                    title: 'Activity',
                    children: <Widget>[
                      if (d.activity.isEmpty) const ListRow(leading: IconBadge(FLucideIcons.clock), title: 'Nothing yet'),
                      for (final Json e in d.activity)
                        ListRow(
                          leading: const IconBadge(FLucideIcons.clock),
                          title: '${e['text']}',
                          subtitle: <String>[
                            '${e['actorName']}',
                            if (e['tenantName'] != null) '${e['tenantName']}',
                            if (asDateOrNull(e['at']) != null) whenText(asDateOrNull(e['at'])!),
                          ].join(' · '),
                        ),
                    ],
                  ),
                ],
              ),
            ),
    );
  }
}
