extends SceneTree

func _initialize():
    ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
    ProjectSettings.set_setting("application/config/custom_user_dir_name", "Sts2TogetherDemoLabStartup")
    # Exercise the real native backend without sending diagnostic data externally.
    ProjectSettings.set_setting("sentry/config/dsn", "http://public@127.0.0.1:9/1")
    if not OS.get_user_data_dir().contains("Sts2TogetherDemoLabStartup"):
        push_error("Startup audit profile isolation failed")
        quit(20)
        return
    # Seed only this disposable profile, including native mod-loader consent.
    DirAccess.make_dir_recursive_absolute(OS.get_user_data_dir() + "/default/1")
    var settings = {
        "schema_version": 5, "fullscreen": false, "skip_intro_logo": false,
        "seen_ea_disclaimer": true, "limit_fps_in_background": false,
        "fps_limit": 60, "volume_master": 0.0,
        "window_size": {"X": 1280, "Y": 720},
        "window_position": {"X": 100, "Y": 100},
        "mod_settings": {"mods_enabled": true, "mod_list": [
            {"id": "Sts2Demo", "is_enabled": true, "source": "mods_directory"},
            {"id": "Sts2DemoLab", "is_enabled": true, "source": "mods_directory"}
        ]}
    }
    var file = FileAccess.open("user://default/1/settings.save", FileAccess.WRITE)
    if not file:
        push_error("Cannot seed isolated startup settings")
        quit(21)
        return
    file.store_string(JSON.stringify(settings))
    file.close()
    call_deferred("start_game")

func start_game():
    change_scene_to_file("res://scenes/game.tscn")
