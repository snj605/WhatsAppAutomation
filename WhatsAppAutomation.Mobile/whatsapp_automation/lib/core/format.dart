import 'package:flutter/widgets.dart';

/// Up to two initials, for the round avatar (same rule as the website).
String initialsOf(String name) {
  final List<String> parts = name.trim().split(RegExp(r'\s+')).where((String p) => p.isNotEmpty).toList();
  if (parts.isEmpty) {
    return '?';
  }

  final String first = parts.first.characters.first;
  final String last = parts.length > 1 ? parts.last.characters.first : '';
  return (first + last).toUpperCase();
}

/// Soft background and text colour for a person's avatar, always the same for the same name (as on the website).
({Color background, Color foreground}) avatarColors(String name) {
  int hash = 0;
  for (final int rune in name.runes) {
    hash = (hash * 31 + rune).toSigned(32);
  }

  final double hue = (hash.abs() % 360).toDouble();
  return (
    background: HSLColor.fromAHSL(1, hue, 0.70, 0.93).toColor(),
    foreground: HSLColor.fromAHSL(1, hue, 0.45, 0.32).toColor(),
  );
}

const List<String> _months = <String>['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const List<String> _weekdays = <String>['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

String monthName(int month) => _months[month - 1];

/// "3:05 PM"
String timeText(DateTime t) {
  final int hour = t.hour % 12 == 0 ? 12 : t.hour % 12;
  final String minute = t.minute.toString().padLeft(2, '0');
  return '$hour:$minute ${t.hour < 12 ? 'AM' : 'PM'}';
}

/// "07:15:00 AM": the exact time, used wherever the next send is shown.
String clockText(DateTime t) {
  final int hour = t.hour % 12 == 0 ? 12 : t.hour % 12;
  String two(int n) => n.toString().padLeft(2, '0');
  return '${two(hour)}:${two(t.minute)}:${two(t.second)} ${t.hour < 12 ? 'AM' : 'PM'}';
}

/// "Today 07:15:00 AM", "Tomorrow 07:15:00 AM" or "Fri 21 Nov 07:15:00 AM".
String whenText(DateTime t) {
  final DateTime now = DateTime.now();
  final DateTime day = DateTime(t.year, t.month, t.day);
  final int days = day.difference(DateTime(now.year, now.month, now.day)).inDays;
  final String date = switch (days) {
    0 => 'Today',
    1 => 'Tomorrow',
    _ => '${_weekdays[t.weekday - 1].substring(0, 3)} ${t.day} ${_months[t.month - 1]}',
  };
  return '$date ${clockText(t)}';
}

/// "14 Nov"
String dayMonthText(DateTime t) => '${t.day} ${_months[t.month - 1]}';

/// "Saturday 14 Nov"
String longDayText(DateTime t) => '${_weekdays[t.weekday - 1]} ${t.day} ${_months[t.month - 1]}';

/// For the chat list: the time today, "Yesterday", or the date.
String listTimeText(DateTime t) {
  final DateTime now = DateTime.now();
  final DateTime today = DateTime(now.year, now.month, now.day);
  final DateTime day = DateTime(t.year, t.month, t.day);
  final int days = today.difference(day).inDays;
  if (days == 0) {
    return timeText(t);
  }

  return days == 1 ? 'Yesterday' : dayMonthText(t);
}

/// For the date line between messages: "Today", "Yesterday" or the date.
String dayLineText(DateTime t) {
  final DateTime now = DateTime.now();
  final int days = DateTime(now.year, now.month, now.day).difference(DateTime(t.year, t.month, t.day)).inDays;
  return days == 0 ? 'Today' : days == 1 ? 'Yesterday' : longDayText(t);
}

String fileSizeText(int bytes) {
  if (bytes < 1024) {
    return '$bytes B';
  }

  if (bytes < 1024 * 1024) {
    return '${(bytes / 1024).round()} KB';
  }

  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}

/// "{{name}}" and the other placeholders shown in words, so the user sees what each person gets.
String friendlyPlaceholders(String text) => text
    .replaceAll(RegExp(r'\{\{\s*name_gu\s*\}\}', caseSensitive: false), '[Gujarati name]')
    .replaceAll(RegExp(r'\{\{\s*name_hi\s*\}\}', caseSensitive: false), '[Hindi name]')
    .replaceAll(RegExp(r'\{\{\s*name_en\s*\}\}', caseSensitive: false), '[English name]')
    .replaceAll(RegExp(r'\{\{\s*name\s*\}\}', caseSensitive: false), '[Name]')
    .replaceAll(RegExp(r'\{\{\s*salutation\s*\}\}', caseSensitive: false), '[Salutation]');

final RegExp _placeholder = RegExp(
  r'\{\{\s*(name|name_en|name_hi|name_gu|salutation)\s*\}\}|\{(name|name_en|name_hi|name_gu|salutation)\}',
  caseSensitive: false,
);
final RegExp _choice = RegExp(r'\{([^{}|]*(?:\|[^{}|]*)+)\}');

/// The message text with placeholders and word choices shown as highlighted chips (like the website's bubble).
List<InlineSpan> highlightedText(String text, {required TextStyle chip}) {
  final RegExp both = RegExp('${_choice.pattern}|${_placeholder.pattern}', caseSensitive: false);
  final List<InlineSpan> spans = <InlineSpan>[];
  int at = 0;
  for (final RegExpMatch m in both.allMatches(text)) {
    if (m.start > at) {
      spans.add(TextSpan(text: text.substring(at, m.start)));
    }

    final String label = m.group(1) != null
        ? m.group(1)!.split('|').map((String o) => o.trim()).join(' / ')
        : (m.group(2) ?? m.group(3) ?? '').toLowerCase();
    spans.add(TextSpan(text: ' $label ', style: chip));
    at = m.end;
  }

  if (at < text.length) {
    spans.add(TextSpan(text: text.substring(at)));
  }

  return spans;
}

/// The message as one example person would get it: placeholders filled with a sample person
/// and each "{a|b}" word choice showing its first option. Used for live previews while typing.
String sampleText(String text, {String name = 'Ramesh Patel'}) => text
    .replaceAllMapped(RegExp(r'\{([^{}|]*(?:\|[^{}|]*)+)\}'), (Match m) => m.group(1)!.split('|').first.trim())
    .replaceAll(RegExp(r'\{\{\s*name_gu\s*\}\}|\{name_gu\}', caseSensitive: false), 'રમેશ પટેલ')
    .replaceAll(RegExp(r'\{\{\s*name_hi\s*\}\}|\{name_hi\}', caseSensitive: false), 'रमेश पटेल')
    .replaceAll(RegExp(r'\{\{\s*name_en\s*\}\}|\{name_en\}', caseSensitive: false), name)
    .replaceAll(RegExp(r'\{\{\s*name\s*\}\}|\{name\}', caseSensitive: false), name)
    .replaceAll(RegExp(r'\{\{\s*salutation\s*\}\}|\{salutation\}', caseSensitive: false), 'Dear')
    .replaceAll(RegExp(r'\{\{\s*for_name\s*\}\}', caseSensitive: false), 'Late Shri Mohanbhai')
    // The user's own contact fields ({{city}}): each person's value goes there.
    .replaceAllMapped(RegExp(r'\{\{\s*([A-Za-z0-9_]+)\s*\}\}'), (Match m) => '[${m.group(1)}]');

/// "07:15" → "07:15 AM".
String hhmmText(String? hhmm) {
  final List<String> parts = (hhmm ?? '00:00').split(':');
  final DateTime t = DateTime(2000, 1, 1, int.tryParse(parts.first) ?? 0, parts.length > 1 ? int.tryParse(parts[1]) ?? 0 : 0);
  return timeText(t);
}
