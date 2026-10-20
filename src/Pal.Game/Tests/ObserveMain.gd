extends SceneTree

# Process-level observation of the shipped Main scene and public Godot draw nodes.
func _initialize() -> void:
    call_deferred("observe")

func check(condition: bool, message: String) -> bool:
    if not condition:
        printerr("Main observation failed: " + message)
        quit(1)
        return false
    return true

func observe() -> void:
    var main = load("res://Scenes/Main.tscn").instantiate()
    root.add_child(main)
    await process_frame
    await process_frame
    await process_frame
    if not check(main.get("TestViewpoint") == Vector2i(512, 512), "shipped Main must select the visible T2 test view(512,512), not a party spawn"): return
    if not check(main.get_child_count() == 1, "native map child missing"): return
    var map = main.get_child(0)
    if not check(map.is_inside_tree() and map.is_node_ready(), "map is not ready in tree"): return
    if not check(map.get("TestViewpoint") == Vector2i(512, 512), "shipped Main did not apply its T2 test view"): return
    var sprites = map.get_children()
    if not check(sprites.size() == 392, "two map layers were not drawn"): return
    var at_cell: Array[Sprite2D] = []
    for sprite in sprites:
        if not check(sprite is Sprite2D and not sprite.centered, "expected native top-left Sprite2D"): return
        if sprite.position == Vector2(80, 24): at_cell.append(sprite)
    if not check(at_cell.size() == 2, "both layers missing at non-origin cell"): return
    if not check(at_cell[0].texture.get_width() == 41 and at_cell[1].texture.get_width() == 42, "wrong layer order/texture"): return
    if not check(root.min_size == Vector2i(320, 200), "minimum window permits fractional downscale"): return
    root.size = Vector2i(901, 701)
    await process_frame
    await process_frame
    if not check(root.get_visible_rect().size == Vector2(320, 200), "expanded logical field of view"): return
    var transform = root.get_final_transform()
    if not check(transform.x == Vector2(2, 0) and transform.y == Vector2(0, 2) and transform.origin == Vector2(131, 151), "wrong integer display rectangle/black margins"): return
    print("PAL_MAIN_OBSERVED:392 sprites; viewport320x200; display640x400 at131,151")
    quit(0)
