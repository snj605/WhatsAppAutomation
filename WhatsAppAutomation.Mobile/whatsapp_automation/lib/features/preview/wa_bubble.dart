import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_client.dart';
import '../../core/format.dart';
import '../../core/json.dart';
import '../../core/providers.dart';
import '../broadcasts/broadcast_models.dart';
import '../files/files_api.dart';

/// The file part of a message, as the screens have it (an upload, a template's file or a sent broadcast's file).
class WaFile {
  const WaFile({
    required this.id,
    required this.fileName,
    required this.contentType,
    required this.sizeBytes,
    this.isBigVideo = false,
    this.kept = true,
  });

  final String id;
  final String fileName;
  final String contentType;
  final int sizeBytes;

  /// A video over 100 MB goes as a document.
  final bool isBigVideo;

  /// False once the server freed the file after sending: only its card is shown.
  final bool kept;

  static WaFile? upload(UploadedFile? f) => f == null
      ? null
      : WaFile(id: f.id, fileName: f.fileName, contentType: f.contentType, sizeBytes: f.sizeBytes, isBigVideo: f.isBigVideo);

  static WaFile? sent(BroadcastAttachment? f) => f == null
      ? null
      : WaFile(id: f.id, fileName: f.fileName, contentType: f.contentType, sizeBytes: f.sizeBytes, isBigVideo: f.isBigVideo, kept: f.kept);
}

/// What WhatsApp shows a file as. A video over 100 MB goes as a document; SVG is not shown as a photo.
String waKind(String contentType, {bool isBigVideo = false}) {
  final String t = contentType.toLowerCase();
  if (t.startsWith('image/') && !t.contains('svg')) {
    return 'image';
  }

  if (t.startsWith('video/')) {
    return isBigVideo ? 'document' : 'video';
  }

  return t.startsWith('audio/') ? 'audio' : 'document';
}

/// "PDF", "DOCX", … from the file name.
String docType(String fileName) {
  final int dot = fileName.lastIndexOf('.');
  final String ext = dot < 0 ? 'FILE' : fileName.substring(dot + 1).toUpperCase();
  return ext.isEmpty || ext.length > 5 ? 'FILE' : ext;
}

/// The colour of a document's icon, as WhatsApp uses: PDF red, Word blue, Excel green, PowerPoint orange.
Color docColor(String fileName) => switch (docType(fileName)) {
      'PDF' => const Color(0xFFE5252A),
      'DOC' || 'DOCX' || 'RTF' || 'ODT' => const Color(0xFF2B579A),
      'XLS' || 'XLSX' || 'CSV' || 'ODS' => const Color(0xFF217346),
      'PPT' || 'PPTX' || 'ODP' => const Color(0xFFD24726),
      _ => const Color(0xFF8696A0),
    };

/// The time as WhatsApp on a phone writes it: "8:15 pm", "12:05 am".
String waTimeText(DateTime t) => timeText(t).toLowerCase();

/// WhatsApp's formatting as text spans: *bold*, _italic_, ~strike~, ```monospace``` and `code`.
/// Marks inside words (2*3*4) stay as typed, the same rule as WhatsApp and the website.
List<InlineSpan> waSpans(String text) {
  final RegExp marks = RegExp(r'```([\s\S]+?)```|`([^`\n]+)`|(?<=^|[\s(])([*_~])(?!\s)([^\n]*?[^\s])\3(?=$|[\s.,!?)])', multiLine: true);
  final List<InlineSpan> spans = <InlineSpan>[];
  int at = 0;
  for (final RegExpMatch m in marks.allMatches(text)) {
    if (m.start > at) {
      spans.add(TextSpan(text: text.substring(at, m.start)));
    }

    if (m.group(1) != null || m.group(2) != null) {
      spans.add(TextSpan(text: m.group(1) ?? m.group(2), style: const TextStyle(fontFamily: 'monospace')));
    } else {
      final TextStyle style = switch (m.group(3)) {
        '*' => const TextStyle(fontWeight: FontWeight.w700),
        '_' => const TextStyle(fontStyle: FontStyle.italic),
        _ => const TextStyle(decoration: TextDecoration.lineThrough),
      };
      spans.add(TextSpan(text: m.group(4), style: style));
    }

    at = m.end;
  }

  if (at < text.length) {
    spans.add(TextSpan(text: text.substring(at)));
  }

  return spans;
}

/// Short-lived links to file content (GET /api/v1/files/{id}/view-link), so a photo can be shown without the
/// sign-in token. One link per file is reused until a minute before it ends.
class FileLinks {
  FileLinks(this._api);

  final ApiClient _api;
  final Map<String, ({String url, DateTime until})> _cache = <String, ({String url, DateTime until})>{};

  Future<String?> urlFor(String fileId) async {
    final ({String url, DateTime until})? cached = _cache[fileId];
    if (cached != null && cached.until.isAfter(DateTime.now())) {
      return cached.url;
    }

    try {
      final Json link = (await _api.get('/api/v1/files/$fileId/view-link'))! as Json;
      final String url = '${_api.baseUrl}${link['url'] as String}';
      final DateTime until = (asDateOrNull(link['expiresAt']) ?? DateTime.now()).subtract(const Duration(minutes: 1));
      _cache[fileId] = (url: url, until: until);
      return url;
    } on Object {
      // Freed after sending, or not the user's: the preview shows the file card instead.
      return null;
    }
  }
}

final Provider<FileLinks> fileLinksProvider = Provider<FileLinks>((Ref ref) => FileLinks(ref.watch(apiClientProvider)));

/// One sent message drawn the way WhatsApp draws it: green bubble on the chat background, the real photo, a video
/// tile with a play button (opens the video), a document card (type and size), the caption with WhatsApp's
/// formatting, the time and ticks. Same as the website's preview.
class WaBubble extends ConsumerStatefulWidget {
  const WaBubble({super.key, this.text, this.file, this.time, this.status, this.chip});

  final String? text;
  final WaFile? file;
  final DateTime? time;

  /// "pending", "sent", "delivered", "read", "failed"; null shows read ticks (a preview).
  final String? status;

  /// A small label above the bubble, like WhatsApp's day chip ("Preview", a person's name).
  final String? chip;

  @override
  ConsumerState<WaBubble> createState() => _WaBubbleState();
}

class _WaBubbleState extends ConsumerState<WaBubble> {
  String? _url;
  String? _loadedFor;

  String? get _kind => widget.file == null ? null : waKind(widget.file!.contentType, isBigVideo: widget.file!.isBigVideo);

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(WaBubble oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.file?.id != widget.file?.id) {
      _load();
    }
  }

  void _load() {
    final WaFile? f = widget.file;
    _url = null;
    _loadedFor = f?.id;
    if (f == null || !f.kept || _kind == 'document') {
      return;
    }

    ref.read(fileLinksProvider).urlFor(f.id).then((String? url) {
      if (mounted && _loadedFor == f.id) {
        setState(() => _url = url);
      }
    });
  }

  Future<void> _open() async {
    if (_url != null) {
      await launchUrl(Uri.parse(_url!), mode: LaunchMode.externalApplication);
    }
  }

  @override
  Widget build(BuildContext context) {
    final bool dark = context.theme.colors.brightness == Brightness.dark;
    final Color wall = dark ? const Color(0xFF0B141A) : const Color(0xFFEFEAE2);
    final Color out = dark ? const Color(0xFF005C4B) : const Color(0xFFD9FDD3);
    final Color ink = dark ? const Color(0xFFE9EDEF) : const Color(0xFF111B21);
    final Color meta = dark ? const Color(0xFF8FA1A9) : const Color(0xFF667781);
    final bool media = _kind == 'image' || _kind == 'video';
    // WhatsApp on a phone: 16 sp text with 21 sp lines; a photo bubble is about 72% of the screen wide and a text
    // bubble at most 80%. Each person's own font size setting can still move a word to the next line.
    final TextStyle body = TextStyle(color: ink, fontSize: 16, height: 21 / 16);
    final double screen = MediaQuery.sizeOf(context).width;
    final bool hasText = widget.text?.trim().isNotEmpty ?? false;
    final Widget clock = Row(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text(waTimeText(widget.time ?? DateTime.now()), style: TextStyle(color: meta, fontSize: 12, height: 1.25)),
        const SizedBox(width: 3),
        _ticks(meta),
      ],
    );
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 12),
      decoration: BoxDecoration(color: wall, borderRadius: BorderRadius.circular(12)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: <Widget>[
          if (widget.chip != null)
            Center(
              child: Container(
                margin: const EdgeInsets.only(bottom: 8),
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(color: dark ? const Color(0xFF182229) : Colors.white, borderRadius: BorderRadius.circular(8)),
                child: Text(widget.chip!, style: TextStyle(color: meta, fontSize: 12)),
              ),
            ),
          LayoutBuilder(
            builder: (BuildContext context, BoxConstraints room) {
              final double widest = room.maxWidth;
              final double photoWidth = (screen * 0.72).clamp(0, widest).toDouble();
              return ConstrainedBox(
            constraints: media
                ? BoxConstraints.tightFor(width: photoWidth)
                : BoxConstraints(maxWidth: (screen * 0.8).clamp(90, widest < 90 ? 90 : widest).toDouble(), minWidth: 90),
            child: Container(
              padding: media ? const EdgeInsets.fromLTRB(3, 3, 3, 6) : const EdgeInsets.fromLTRB(9, 6, 8, 6),
              decoration: BoxDecoration(
                color: out,
                borderRadius: const BorderRadius.only(
                  topLeft: Radius.circular(8),
                  bottomLeft: Radius.circular(8),
                  bottomRight: Radius.circular(8),
                ),
                boxShadow: const <BoxShadow>[BoxShadow(color: Color(0x210B141A), offset: Offset(0, 1), blurRadius: 0.5)],
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  if (widget.file case final WaFile f) _filePart(f, ink, meta),
                  // Like WhatsApp: the time takes room only at the end of the last line (the spacer), so lines
                  // break where the phone breaks them; on a full last line it moves to a line of its own.
                  if (hasText)
                    Padding(
                      padding: media ? const EdgeInsets.fromLTRB(6, 5, 6, 0) : EdgeInsets.zero,
                      child: Stack(
                        children: <Widget>[
                          Text.rich(
                            TextSpan(
                              style: body,
                              children: <InlineSpan>[
                                ...waSpans(widget.text!),
                                const WidgetSpan(child: SizedBox(width: 74, height: 16)),
                              ],
                            ),
                          ),
                          Positioned(right: 0, bottom: 0, child: clock),
                        ],
                      ),
                    )
                  else
                    Align(
                      alignment: Alignment.centerRight,
                      child: Padding(padding: EdgeInsets.only(top: 2, right: media ? 6 : 0), child: clock),
                    ),
                ],
              ),
            ),
          );
            },
          ),
        ],
      ),
    );
  }

  Widget _ticks(Color meta) => switch (widget.status) {
        'pending' => Icon(FLucideIcons.clock, size: 12, color: meta),
        'sent' => Icon(FLucideIcons.check, size: 14, color: meta),
        'delivered' => Icon(FLucideIcons.checkCheck, size: 14, color: meta),
        'failed' => const Icon(FLucideIcons.circleAlert, size: 13, color: Color(0xFFD92D20)),
        _ => const Icon(FLucideIcons.checkCheck, size: 14, color: Color(0xFF53BDEB)),
      };

  Widget _filePart(WaFile f, Color ink, Color meta) {
    switch (_kind) {
      case 'image':
        return ClipRRect(
          borderRadius: BorderRadius.circular(6),
          child: _url == null
              ? _placeholder(FLucideIcons.image, f.kept ? f.fileName : 'Photo (no longer kept)', meta)
              : Image.network(
                  _url!,
                  fit: BoxFit.cover,
                  width: double.infinity,
                  height: 220,
                  errorBuilder: (_, _, _) => _placeholder(FLucideIcons.image, f.fileName, meta),
                  loadingBuilder: (BuildContext context, Widget child, ImageChunkEvent? progress) =>
                      progress == null ? child : _placeholder(FLucideIcons.image, 'Loading…', meta),
                ),
        );
      case 'video':
        return GestureDetector(
          onTap: _open,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: Container(
              height: 180,
              width: double.infinity,
              color: const Color(0xFF1F2C33),
              child: Stack(
                alignment: Alignment.center,
                children: <Widget>[
                  Container(
                    width: 52,
                    height: 52,
                    decoration: const BoxDecoration(color: Color(0x8C0B141A), shape: BoxShape.circle),
                    child: const Icon(FLucideIcons.play, color: Colors.white, size: 22),
                  ),
                  Positioned(
                    left: 8,
                    bottom: 6,
                    child: Row(
                      children: <Widget>[
                        const Icon(FLucideIcons.video, color: Colors.white, size: 14),
                        const SizedBox(width: 4),
                        Text(
                          f.kept ? fileSizeText(f.sizeBytes) : 'No longer kept',
                          style: const TextStyle(color: Colors.white, fontSize: 12),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      case 'audio':
        return GestureDetector(
          onTap: _open,
          child: Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Icon(FLucideIcons.play, color: meta, size: 20),
                const SizedBox(width: 8),
                Flexible(child: Text(f.fileName, style: TextStyle(color: ink), overflow: TextOverflow.ellipsis)),
              ],
            ),
          ),
        );
      default:
        return Container(
          margin: const EdgeInsets.only(bottom: 4),
          padding: const EdgeInsets.all(10),
          decoration: BoxDecoration(color: const Color(0x0D000000), borderRadius: BorderRadius.circular(6)),
          child: Row(
            children: <Widget>[
              Container(
                width: 34,
                height: 40,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: docColor(f.fileName), borderRadius: BorderRadius.circular(4)),
                child: Text(docType(f.fileName),
                    style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w700)),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(f.fileName, maxLines: 1, overflow: TextOverflow.ellipsis,
                        style: TextStyle(color: ink, fontWeight: FontWeight.w500)),
                    Text(
                      '${docType(f.fileName)} · ${fileSizeText(f.sizeBytes)}${f.isBigVideo ? ' · video sent as a file' : ''}',
                      style: TextStyle(color: meta, fontSize: 12),
                    ),
                  ],
                ),
              ),
              Icon(FLucideIcons.download, size: 18, color: meta),
            ],
          ),
        );
    }
  }

  Widget _placeholder(IconData icon, String label, Color meta) => Container(
        height: 160,
        width: double.infinity,
        color: const Color(0x14000000),
        padding: const EdgeInsets.all(8),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: <Widget>[
            Icon(icon, size: 28, color: meta),
            const SizedBox(height: 8),
            Text(label, textAlign: TextAlign.center, style: TextStyle(color: meta, fontSize: 13)),
          ],
        ),
      );
}
