extends SceneTree

# Independent expectations for M1Bundle.cs, through ResourceLoader/PackedScene only.
func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if args.size() != 2 or args[0] != "--native-root":
        fail("expected --native-root")
        return
    var packed = load(args[1] + "/Scenes/Scene.tscn")
    if not packed is PackedScene:
        fail("native PackedScene missing")
        return
    # Preparation verifies native data/textures, not presentation. Runtime tree entry
    # requires a checked profile and InitializeScene; never invoke _Ready here.
    var scene = packed.instantiate()
    var data = scene.get("Data")
    if data == null or data.get("SceneId") != 2 or data.get("MapId") != 12:
        fail("scene data/identity missing")
        return
    var map = data.get("Map")
    if map.get("Width") != 64 or map.get("Height") != 128 or map.get("HalfRows") != 2 or map.get("RawCells").size() != 16384:
        fail("fixed map grid lost")
        return
    var fields = ["RawCells", "BottomFrames", "TopFrames", "BottomHeights", "TopHeights", "Blocked"]
    for field in fields:
        if map.get(field).size() != 16384:
            fail("map array size lost: " + field)
            return
    # Hand-worked [y,x,h] indices: [1,3,0] -> 134; [3,1,1] -> 387;
    # [5,7,1] -> 655. Their h-neighbors and x/y transposes must remain distinct.
    # Each literal row is [index, raw, bottom, top, bottomHeight, topHeight, blocked].
    var expected_cells = [
        [0, 0x03022500, 0, 1, 5, 3, 1],
        [1, 0, 0, -1, 0, 0, 0],
        [134, 0x07012401, 1, 0, 4, 7, 1],
        [135, 0, 0, -1, 0, 0, 0],
        [386, 0, 0, -1, 0, 0, 0],
        [387, 0x02020900, 0, 1, 9, 2, 0],
        [654, 0, 0, -1, 0, 0, 0],
        [655, 0x00000c01, 1, -1, 12, 0, 0]
    ]
    for expected in expected_cells:
        for column in range(fields.size()):
            if map.get(fields[column])[expected[0]] != expected[column + 1]:
                fail("map [y,x,h] array order/value lost: %s[%d]" % [fields[column], expected[0]])
                return
    var frames = map.get("Tiles").get("Frames")
    if frames.size() != 2 or frames[0].get("Indices") != PackedByteArray([0, 1, 0, 2]) or frames[0].get("Alpha") != PackedByteArray([255, 255, 0, 255]):
        fail("frame data lost")
        return
    var image = frames[0].get("Texture").get_image()
    image.convert(Image.FORMAT_RGBA8)
    if image.get_data() != PackedByteArray([0,20,80,255,40,20,80,255,0,20,80,0,80,20,80,255]):
        fail("opaque index0/independent alpha/native texture changed")
        return
    if data.get("WScriptOnEnter") != 33 or data.get("WScriptOnTeleport") != 44 or data.get("ArchiveSources").size() != 6 or data.get("BundleSha256").length() != 64:
        fail("scene provenance lost")
        return
    var objects = data.get("Objects")
    if objects.size() != 2 or objects[0].get("X") != 65520 or objects[0].get("SVanishTime") != -3 or objects[0].get("SLayer") != -2 or objects[0].get("SState") != 0 or objects[0].get("InitiallyVisible") or objects[0].get("InitialFrameIndex") != 2 or objects[0].get("Sprite").get("SpriteId") != 9 or objects[1].get("Sprite") != null or objects[1].get("SState") != 2:
        fail("hidden/no-sprite object initial state lost")
        return
    var party = data.get("DefaultParty")
    if party.size() != 1 or party[0].get("RoleId") != 0 or party[0].get("Sprite").get("SpriteId") != 7 or party[0].get("Source")["spriteByteOffset"] != 24:
        fail("defaultParty source/dependency lost")
        return
    if data.get("Palette").get("Lighting") != "night" or data.get("Palette").get("SelectedLighting") != "day":
        fail("legal night fallback changed")
        return
    for sprite in data.get("Sprites"):
        for frame in sprite.get("Frames"):
            if frame.get("Texture") == null or frame.get("Texture").get_image().is_empty():
                fail("sprite texture not loadable")
                return
    if map.get("Preview").get_width() != 2048:
        fail("preview native reference missing")
        return
    print("PAL_NATIVE_VERIFIED")
    scene.free()
    quit(0)

func fail(message: String) -> void:
    push_error(message)
    quit(1)
