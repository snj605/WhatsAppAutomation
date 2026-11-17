import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/json.dart';
import '../../core/providers.dart';

final Provider<FilesApi> filesApiProvider = Provider<FilesApi>((Ref ref) => FilesApi(ref.watch(apiClientProvider)));

/// A file uploaded to send with a message (POST /api/v1/files).
class UploadedFile {
  const UploadedFile({required this.id, required this.fileName, required this.contentType, required this.sizeBytes});

  factory UploadedFile.fromJson(Json json) => UploadedFile(
        id: json['id'] as String,
        fileName: json['fileName'] as String? ?? '',
        contentType: json['contentType'] as String? ?? '',
        sizeBytes: asInt(json['sizeBytes']),
      );

  final String id;
  final String fileName;
  final String contentType;
  final int sizeBytes;
}

/// A file the user chose on the phone, read into memory.
class PickedFile {
  const PickedFile({required this.name, required this.bytes});

  final String name;
  final List<int> bytes;

  String get extension {
    final int dot = name.lastIndexOf('.');
    return dot < 0 ? '' : name.substring(dot + 1).toLowerCase();
  }
}

const int maxFileBytes = 16 * 1024 * 1024;

/// Programs and scripts the server refuses; checked here too so the user hears it at once (same list as the website).
const List<String> _blocked = <String>[
  'exe', 'msi', 'bat', 'cmd', 'com', 'scr', 'pif', 'ps1', 'vbs', 'vbe', 'js', 'jse', 'wsf', 'jar', 'apk', 'app', 'sh', 'dll',
];

/// Why a file cannot be sent, or null when it can (same rules as the website).
String? fileProblem(PickedFile file) {
  if (file.bytes.isEmpty) {
    return 'The file is empty.';
  }

  if (file.bytes.length > maxFileBytes) {
    return 'The file is larger than 16 MB.';
  }

  if (_blocked.contains(file.extension)) {
    return 'Programs and scripts cannot be sent. Send a document, photo or video.';
  }

  return null;
}

/// The content type for common file types, so WhatsApp shows a photo as a photo and a PDF as a document.
String contentTypeFor(String extension) => switch (extension) {
      'jpg' || 'jpeg' => 'image/jpeg',
      'png' => 'image/png',
      'gif' => 'image/gif',
      'webp' => 'image/webp',
      'heic' => 'image/heic',
      'mp4' => 'video/mp4',
      '3gp' => 'video/3gpp',
      'mov' => 'video/quicktime',
      'mp3' => 'audio/mpeg',
      'm4a' => 'audio/mp4',
      'ogg' || 'opus' => 'audio/ogg',
      'aac' => 'audio/aac',
      'pdf' => 'application/pdf',
      'doc' => 'application/msword',
      'docx' => 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      'xls' => 'application/vnd.ms-excel',
      'xlsx' => 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'ppt' => 'application/vnd.ms-powerpoint',
      'pptx' => 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      'csv' => 'text/csv',
      'txt' => 'text/plain',
      'zip' => 'application/zip',
      _ => 'application/octet-stream',
    };

/// Opens the phone's file chooser. Returns null when the user cancels.
/// [extensions] limits the choice (for example xlsx and csv for an import).
Future<PickedFile?> pickFile({List<String>? extensions}) async {
  final PlatformFile? file = extensions == null
      ? await FilePicker.pickFile()
      : await FilePicker.pickFile(type: FileType.custom, allowedExtensions: extensions);
  if (file == null) {
    return null;
  }

  return PickedFile(name: file.name, bytes: await file.readAsBytes());
}

/// Asks where to save [bytes] on the phone (Downloads, Drive…) and saves them there.
/// Returns false when the user cancels.
Future<bool> saveToPhone({required String fileName, required List<int> bytes, required String mimeType}) async {
  final Uri? saved = await FilePicker.saveFile(fileName: fileName, bytes: Uint8List.fromList(bytes), mimeType: mimeType);
  return saved != null;
}

class FilesApi {
  const FilesApi(this._api);

  final ApiClient _api;

  Future<UploadedFile> upload(PickedFile file) async {
    final String? problem = fileProblem(file);
    if (problem != null) {
      throw ApiException(problem, status: 400);
    }

    return UploadedFile.fromJson(
      (await _api.upload(
        '/api/v1/files',
        bytes: file.bytes,
        fileName: file.name,
        contentType: contentTypeFor(file.extension),
      ))! as Json,
    );
  }
}
