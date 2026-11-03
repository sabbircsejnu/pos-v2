import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

enum AppLogEvent { appStarted }

abstract interface class AppLogger {
  void record(AppLogEvent event);
}

final class DebugAppLogger implements AppLogger {
  const DebugAppLogger();

  @override
  void record(AppLogEvent event) {
    if (kDebugMode) {
      debugPrint('[mobile-foundation] ${event.name}');
    }
  }
}

final appLoggerProvider = Provider<AppLogger>((ref) {
  return const DebugAppLogger();
});
