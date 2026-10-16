extends SceneTree
var frames := 0
var did_close := false
func _initialize():
    ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
    ProjectSettings.set_setting("application/config/custom_user_dir_name", "Sts2TogetherStartupCrashLab")
    # Keep this native-only reproducer offline. No user profile or telemetry endpoint.
    ProjectSettings.set_setting("sentry/config/dsn", "http://public@127.0.0.1:9/1")
    if not OS.get_user_data_dir().contains("Sts2TogetherStartupCrashLab"):
        quit(20)
        return
    process_frame.connect(close_in_frame)

func close_in_frame():
    frames += 1
    var sdk = Engine.get_singleton("SentrySDK")
    if not did_close and sdk.call("is_enabled"):
        did_close = true
        for connection in get_signal_connection_list("process_frame"):
            var cb: Callable = connection["callable"]
            var obj = cb.get_object()
            print("REPRO CONNECTION ", obj.get_class() if obj else "null", " ", cb.get_method())
        if "--defer-close" in OS.get_cmdline_args():
            print("REPRO DEFER CLOSE")
            sdk.call_deferred("close")
        else:
            print("REPRO IMMEDIATE CLOSE")
            sdk.call("close")
        print("REPRO CLOSE RETURNED")
    if frames >= 5:
        print("REPRO SURVIVED frames=", frames, " closed=", did_close)
        quit(0 if did_close else 21)
