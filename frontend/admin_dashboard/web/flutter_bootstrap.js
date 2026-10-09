{{flutter_js}}
{{flutter_build_config}}

// Serve CanvasKit locally so the dashboard works without internet access.
_flutter.loader.load({
  config: { canvasKitBaseUrl: "canvaskit/" },
});
