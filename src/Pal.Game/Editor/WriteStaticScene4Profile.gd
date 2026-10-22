extends SceneTree

# Offline local profile authoring for the prepared static scene4 package only.
func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    var options := {}
    var i := 0
    while i < args.size():
        if i + 1 >= args.size() or options.has(args[i]):
            fail("expected unique profile author arguments with values")
            return
        options[args[i]] = args[i + 1]
        i += 2
    for key in ["--resource-root", "--profile-uri", "--walk-frames-word", "--data-sha256"]:
        if not options.has(key):
            fail("missing profile author argument: " + key)
            return
    if options["--resource-root"] != "res://Assets/StaticScene4" or options["--profile-uri"] != "res://Assets/StaticScene4/Profiles/Scene4Static.tres":
        fail("static profile author accepts only the documented StaticScene4 paths")
        return
    var word_text: String = options["--walk-frames-word"]
    if not word_text.is_valid_int():
        fail("source walk-frame value must be a decimal WORD")
        return
    var walk_word := int(word_text)
    if walk_word < 0 or walk_word > 65535:
        fail("source walk-frame value must fit WORD")
        return
    var source_hash: String = options["--data-sha256"]
    for character in source_hash:
        if not (character >= "0" and character <= "9") and not (character >= "a" and character <= "f"):
            fail("DATA.MKF source hash must be lowercase SHA-256")
            return
    if source_hash.length() != 64:
        fail("DATA.MKF source hash must be lowercase SHA-256")
        return

    var root: String = options["--resource-root"]
    var uri: String = options["--profile-uri"]
    if FileAccess.file_exists(uri):
        fail("refusing to overwrite existing static profile")
        return
    var packed = ResourceLoader.load(root + "/Scenes/Scene.tscn")
    if not packed is PackedScene:
        fail("prepared static scene PackedScene missing")
        return
    var scene_node = packed.instantiate()
    var data = scene_node.get("Data")
    if data == null or data.get("SceneId") != 4 or data.get("MapId") != 10 or data.get("Objects").size() != 32:
        scene_node.free()
        fail("prepared native scene identity/object count mismatch")
        return
    var owner = null
    for item in data.get("Objects"):
        if item.get("Id") == 60:
            owner = item
            break
    if owner == null or owner.get("X") != 1216 or owner.get("Y") != 1344 or owner.get("WSpriteNum") != 207 or owner.get("SState") != 2 or owner.get("SVanishTime") != 0:
        scene_node.free()
        fail("prepared owner60 identity/initial state mismatch")
        return
    var source_records = data.get("ArchiveSources")
    var data_sources = source_records.filter(func(s): return s.get("name") == "data.mkf")
    if data_sources.size() != 1 or data_sources[0].get("sha256") != source_hash:
        scene_node.free()
        fail("DATA.MKF hash does not match prepared scene source identity")
        return
    var parties = data.get("DefaultParty")
    if parties.size() != 1 or parties[0].get("RoleId") != 0:
        scene_node.free()
        fail("prepared scene does not identify default role0")
        return

    var walk_source = load("res://Scripts/Resources/PalWalkFramesSource.cs").new()
    walk_source.set("Archive", "data.mkf")
    walk_source.set("Sha256", source_hash)
    walk_source.set("Chunk", 3)
    walk_source.set("ByteOffset", 768)
    walk_source.set("RoleId", 0)
    walk_source.set("Field", "rgwWalkFrames")
    var leader = load("res://Scripts/Resources/PalExplorationLeader.cs").new()
    leader.set("RoleId", 0)
    leader.set("WalkFramesWord", walk_word)
    leader.set("WalkFramesSource", walk_source)
    var start = load("res://Scripts/Resources/PalExplorationStart.cs").new()
    start.set("X", 1232)
    start.set("Y", 1336)
    start.set("Direction", 0)
    start.set("Layer", 0)
    start.set("StepPhase", 0)
    var profile = load("res://Scripts/Resources/PalExplorationProfile.cs").new()
    profile.set("FormatVersion", 1)
    profile.set("SceneId", 4)
    profile.set("BundleSha256", data.get("BundleSha256"))
    profile.set("Start", start)
    profile.set("Leader", leader)
    scene_node.free()
    if DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(uri.get_base_dir())) != OK or ResourceSaver.save(profile, uri) != OK:
        fail("native static profile save failed; any partial output is retained")
        return
    var saved = ResourceLoader.load(uri, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP)
    if saved == null or saved.get("SceneId") != 4 or saved.get("BundleSha256") != data.get("BundleSha256") or saved.get("Start").get("X") != 1232 or saved.get("Start").get("Y") != 1336 or saved.get("Leader").get("RoleId") != 0 or saved.get("Leader").get("WalkFramesWord") != walk_word or saved.get("Leader").get("WalkFramesSource").get("Sha256") != source_hash:
        fail("saved static profile identity/source roundtrip mismatch; output is retained")
        return
    print("PAL_STATIC_PROFILE_WRITTEN:" + uri)
    quit(0)

func fail(message: String) -> void:
    push_error(message)
    quit(1)
