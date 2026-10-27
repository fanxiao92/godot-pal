extends SceneTree

# Artificial M1Bundle fixture preparation only; Main never authors profiles.
func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if (args.size() != 2 and args.size() != 3) or args[0] != "--native-root" or (args.size() == 3 and args[2] != "--first-battle"):
        fail("expected --native-root and optional --first-battle")
        return
    var first_battle := args.size() == 3
    var uri: String = args[1] + "/Profiles/Startup.tres"
    if FileAccess.file_exists(uri):
        fail("refusing to overwrite a profile")
        return
    var packed = load(args[1] + "/Scenes/Scene.tscn")
    if not packed is PackedScene:
        fail("native fixture scene missing")
        return
    var scene = packed.instantiate()
    var data = scene.get("Data")
    var sources = data.get("ArchiveSources").filter(func(s): return s["name"] == "data.mkf")
    if sources.size() != 1 or data.get("DefaultParty").size() != 1 or data.get("DefaultParty")[0].get("RoleId") != 0:
        scene.free()
        fail("artificial fixture provenance/role0 missing")
        return
    var source = load("res://Scripts/Resources/PalWalkFramesSource.cs").new()
    source.set("Archive", "data.mkf")
    source.set("Sha256", sources[0]["sha256"])
    source.set("Chunk", 3)
    source.set("ByteOffset", 768)
    source.set("RoleId", 0)
    source.set("Field", "rgwWalkFrames")
    var leader = load("res://Scripts/Resources/PalExplorationLeader.cs").new()
    leader.set("RoleId", 0)
    # Explicit artificial raw WORD, not an observation of original DATA.
    leader.set("WalkFramesWord", 0)
    leader.set("WalkFramesSource", source)
    var start = load("res://Scripts/Resources/PalExplorationStart.cs").new()
    start.set("X", 1264 if first_battle else 320)
    start.set("Y", 248 if first_battle else 320)
    start.set("Direction", 3 if first_battle else 0)
    start.set("Layer", 0)
    start.set("StepPhase", 0)
    var profile = load("res://Scripts/Resources/PalExplorationProfile.cs").new()
    profile.set("FormatVersion", 1)
    profile.set("SceneId", data.get("SceneId"))
    profile.set("BundleSha256", data.get("BundleSha256"))
    profile.set("Start", start)
    profile.set("Leader", leader)
    scene.free() # Data verification does not enter the uninitialized scene tree.
    if DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(uri.get_base_dir())) != OK or ResourceSaver.save(profile, uri) != OK:
        fail("native fixture profile save failed")
        return
    var loaded = ResourceLoader.load(uri, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP)
    if loaded == null or loaded.get("Start").get("X") != (1264 if first_battle else 320) or loaded.get("Leader").get("WalkFramesWord") != 0:
        fail("native fixture profile roundtrip failed")
        return
    print("PAL_STARTUP_PROFILE:" + uri)
    quit(0)

func fail(message: String) -> void:
    push_error(message)
    quit(1)
