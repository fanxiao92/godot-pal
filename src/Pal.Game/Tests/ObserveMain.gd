extends SceneTree

# Read-only observer of shipped Main. Never supplies camera, leader or object overrides.
# Headless checks state/window only; pixel assertions require an actual rendering backend.
func _initialize() -> void:
    call_deferred("observe")

func check(condition: bool, message: String) -> bool:
    if not condition:
        printerr("Main observation failed: " + message)
        quit(1)
        return false
    return true

func object_state(data: Resource) -> Array:
    var result: Array = []
    for object in data.get("Objects"):
        var values: Dictionary = {}
        for property in object.get_property_list():
            if property.usage & PROPERTY_USAGE_STORAGE and property.type in [TYPE_INT, TYPE_BOOL]:
                values[property.name] = object.get(property.name)
        result.append(values)
    return result

# Worked reference literals, not expectations computed from the production draw plan.
# Leader11x19 at155,97; object frame2(21x29) at150,90; layer1 frame1(15x17) at169,102.
# Both height3 covers at144,87(32x16); height0 top at176,104 stays behind objects.
func pixels(image: Image) -> bool:
    var samples = [
        [Vector2i(10,10), Color.RED], [Vector2i(149,104), Color.RED],
        [Vector2i(150,104), Color.CYAN], [Vector2i(154,104), Color.CYAN],
        [Vector2i(155,104), Color.MAGENTA], [Vector2i(165,115), Color.MAGENTA],
        [Vector2i(166,115), Color.CYAN], [Vector2i(155,116), Color.CYAN],
        [Vector2i(150,99), Color.BLUE], [Vector2i(164,99), Color.GREEN],
        [Vector2i(158,101), Color.CYAN], [Vector2i(159,101), Color.MAGENTA],
        [Vector2i(150,103), Color.CYAN], [Vector2i(178,108), Color.YELLOW],
        [Vector2i(183,108), Color.YELLOW], [Vector2i(184,108), Color.BLUE]
    ]
    if not check(image.get_size() == Vector2i(320,200), "GPU logical readback size"): return false
    for sample in samples:
        if not check(image.get_pixelv(sample[0]).is_equal_approx(sample[1]),
            "pixel %s expected %s got %s" % [sample[0], sample[1], image.get_pixelv(sample[0])]): return false
    print("PAL_GPU_PIXELS:16 independent literal anchors/cover/alpha-hole samples passed")
    return true

func observe() -> void:
    var main = load("res://Scenes/Main.tscn").instantiate()
    root.add_child(main)
    for i in range(3): await process_frame
    var maps = main.find_children("*", "Node2D", true, false).filter(func(n): return n.get("Data") != null)
    if not check(maps.size() == 1, "native scene missing"): return
    var map = maps[0]
    var data = map.get("Data")
    if not check(map.is_inside_tree() and map.is_node_ready(), "scene not ready"): return
    if not check(not map.y_sort_enabled and map.scale == Vector2.ONE, "extra sort or scale"): return
    var before = object_state(data)
    if not check(before.size() == 4, "synthetic complete object set"): return
    if not check(before[0].X == 320 and before[0].Y == 320 and before[0].WCurrentFrameNum == 3
        and before[0].NSpriteFrames == 3 and before[0].WDirection == 0 and before[0].SState == 1
        and before[0].SVanishTime == 0 and before[0].SLayer == 0 and before[0].WTriggerScript == 123
        and before[0].WAutoScript == 456 and before[0].NScriptIdleFrame == 7
        and before[0].NSpriteFramesAuto == 9 and before[0].WScriptIdleFrameCountAuto == 11,
        "raw object values were changed at startup"): return
    if not check(before[1].SLayer == 1 and before[1].WCurrentFrameNum == 1
        and before[2].SState == -1 and before[2].SVanishTime == 12
        and before[3].WSpriteNum == 0 and before[3].SState == 2, "hidden/spriteless objects changed"): return
    var evidence = OS.get_environment("PAL_STATIC_GPU_EVIDENCE")
    var first: Image
    if not evidence.is_empty():
        await RenderingServer.frame_post_draw
        first = root.get_texture().get_image()
        first.save_png(evidence.path_join("frame-003.png"))
        if not pixels(first): return
    for i in range(90): await process_frame
    if not check(object_state(data) == before and data.get("WScriptOnEnter") == 100
        and data.get("WScriptOnTeleport") == 200, "render advanced objects/scripts"): return
    if not evidence.is_empty():
        await RenderingServer.frame_post_draw
        var last = root.get_texture().get_image()
        last.save_png(evidence.path_join("frame-093.png"))
        if not pixels(last): return
        if not check(first.get_data() == last.get_data(), "GPU frames advanced static snapshot"): return
        print("PAL_GPU_STATIC:all 64000 pixels identical across 90 process frames")
    if not check(root.min_size == Vector2i(320, 200), "minimum window permits fractional downscale"): return
    root.size = Vector2i(901, 701)
    await process_frame
    await process_frame
    if not check(root.get_visible_rect().size == Vector2(320, 200), "expanded logical field of view"): return
    var transform = root.get_final_transform()
    if not check(transform.x == Vector2(2, 0) and transform.y == Vector2(0, 2)
        and transform.origin == Vector2(131, 151), "integer display rectangle/black margins"): return
    print("PAL_MAIN_OBSERVED:static state unchanged across 90 frames; viewport320x200; display640x400 at131,151")
    quit(0)
