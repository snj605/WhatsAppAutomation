/// The last data each screen loaded, kept while the app runs, so a screen opened again shows it at once and then
/// refreshes in the background. Cleared on sign out. Nothing is written to the phone's storage.
abstract final class MemoryCache {
  static final Map<String, Object> _items = <String, Object>{};

  static T? get<T>(String key) {
    final Object? value = _items[key];
    return value is T ? value : null;
  }

  static void set(String key, Object value) => _items[key] = value;

  static void clear() => _items.clear();
}
