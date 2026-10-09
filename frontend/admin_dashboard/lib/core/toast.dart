import 'dart:async';

import 'package:flutter/material.dart';

import 'auth_session.dart';
import 'messages.dart';

enum ToastKind { success, error, info }

/// A dismissable top-right toast shown above dialogs, one at a time.
abstract final class Toast {
  static OverlayEntry? _current;
  static Timer? _timer;

  static void show(BuildContext context, {required String title, String? message, ToastKind kind = ToastKind.info}) {
    final overlay = Overlay.maybeOf(context, rootOverlay: true);
    if (overlay == null) return;
    dismiss();
    late final OverlayEntry entry;
    entry = OverlayEntry(
      builder: (_) => _ToastView(title: title, message: message, kind: kind, onDismiss: dismiss),
    );
    _current = entry;
    overlay.insert(entry);
    _timer = Timer(kind == ToastKind.error ? const Duration(seconds: 7) : const Duration(seconds: 4), dismiss);
  }

  static void dismiss() {
    _timer?.cancel();
    _timer = null;
    // The screen it was shown on may already be gone (e.g. after sign-out).
    if (_current?.mounted ?? false) _current!.remove();
    _current = null;
  }
}

void showSuccess(BuildContext context, String title, {String? message}) =>
    Toast.show(context, title: title, message: message, kind: ToastKind.success);

/// [title] says what failed and the message says why; ended sessions are handled on the sign-in screen.
void showError(BuildContext context, String title, Object error) {
  if (error is NeedsSignIn) return;
  Toast.show(context, title: title, message: describeError(error), kind: ToastKind.error);
}

/// Shows a toast when a list fails to load. The future itself is untouched, so the page still shows its error state.
void reportLoadFailure(State state, String title, Future<Object?> future) {
  future.then<void>(
    (_) {},
    onError: (Object e) {
      if (state.mounted) showError(state.context, title, e);
    },
  );
}

class _ToastView extends StatelessWidget {
  const _ToastView({required this.title, required this.message, required this.kind, required this.onDismiss});

  final String title;
  final String? message;
  final ToastKind kind;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final accent = switch (kind) {
      ToastKind.success => colors.secondary,
      ToastKind.error => colors.error,
      ToastKind.info => colors.primary,
    };
    final wide = MediaQuery.sizeOf(context).width >= 600;

    return Positioned(
      top: 16,
      right: 16,
      left: wide ? null : 16,
      child: SafeArea(
        child: TweenAnimationBuilder<double>(
          tween: Tween(begin: 0, end: 1),
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOut,
          builder: (context, t, child) => Opacity(
            opacity: t,
            child: Transform.translate(offset: Offset(0, -8 * (1 - t)), child: child),
          ),
          child: Semantics(
            liveRegion: true,
            container: true,
            child: Material(
              key: const Key('toast'),
              color: colors.surfaceContainerHigh,
              elevation: 6,
              shadowColor: Colors.black54,
              borderRadius: BorderRadius.circular(10),
              clipBehavior: Clip.antiAlias,
              child: ConstrainedBox(
                constraints: BoxConstraints(maxWidth: wide ? 400 : double.infinity),
                child: IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Container(width: 5, color: accent),
                      Expanded(
                        child: Padding(
                          padding: const EdgeInsets.fromLTRB(16, 12, 4, 12),
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                title,
                                style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700, color: accent),
                              ),
                              if (message != null) ...[
                                const SizedBox(height: 4),
                                Text(message!, style: theme.textTheme.bodyMedium?.copyWith(color: colors.onSurface)),
                              ],
                            ],
                          ),
                        ),
                      ),
                      Align(
                        alignment: Alignment.topCenter,
                        child: TextButton(onPressed: onDismiss, child: const Text('Dismiss')),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
