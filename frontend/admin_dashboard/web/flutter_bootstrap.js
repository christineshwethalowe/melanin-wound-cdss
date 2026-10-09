{{flutter_js}}
{{flutter_build_config}}

// Load CanvasKit from this server (build/web/canvaskit) instead of Google's CDN, so the dashboard also works on a
// facility network without internet access.
_flutter.loader.load({
  config: { canvasKitBaseUrl: "canvaskit/" },
});
