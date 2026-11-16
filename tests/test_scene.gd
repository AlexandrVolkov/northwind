extends SceneTree
## Integration smoke test. Requires imported game assets.
## With a rendering display, also saves build/smoke.png.
var failures := 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func click_cell(scene: Node2D, cell: Vector2i, button: MouseButton) -> void:
	var screen_position: Vector2 = scene.get_viewport().get_canvas_transform() * scene.center_of(cell)
	Input.warp_mouse(screen_position)
	await process_frame
	var event := InputEventMouseButton.new()
	event.position = screen_position
	event.button_index = button
	event.pressed = true
	Input.parse_input_event(event)
	await process_frame
	event = event.duplicate()
	event.pressed = false
	Input.parse_input_event(event)
	await process_frame

func run() -> void:
	root.size = Vector2i(1280, 800)
	var scene: Node2D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	for i in 5: await process_frame
	check(scene.ready_to_play, "Game scene loads imported assets")
	if not scene.ready_to_play:
		quit(1)
		return
	check(scene.model.units.size() == 4, "Four prototype units spawn")
	var unit: Dictionary = scene.model.get_unit(2)
	await click_cell(scene, unit.cell, MOUSE_BUTTON_LEFT)
	check(scene.selected == 2, "Mouse click selects friendly unit")
	var destination := Vector2i(-1, -1)
	for direction in [Vector2i.RIGHT, Vector2i.DOWN, Vector2i.LEFT, Vector2i.UP]:
		var target: Vector2i = unit.cell + direction
		var route: Array[Vector2i] = scene.model.path_for(unit, target)
		if route.size() > 1 and scene.model.route_cost(route) <= int(unit.moves):
			destination = target
			break
	check(destination.x >= 0, "Spawn has a reachable adjacent cell")
	if destination.x >= 0:
		await click_cell(scene, destination, MOUSE_BUTTON_RIGHT)
		check(unit.cell == destination, "Right click commits movement")
		for i in 10: scene._process(1.0)
		check(scene.animations.is_empty(), "Movement animation completes")
		check(Vector2(scene.positions[2]).distance_to(scene.center_of(destination)) < 0.1, "Sprite arrives at model position")
	scene.end_button.pressed.emit()
	check(scene.model.turn == 2, "End-turn button advances the simulation")
	for i in 100: scene._process(1.0)
	var reset := InputEventKey.new()
	reset.physical_keycode = KEY_R
	reset.pressed = true
	scene._unhandled_input(reset)
	check(scene.model.turn == 1 and scene.animations.is_empty(), "R resets battle and animations")
	for i in 5: await process_frame
	if DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		DirAccess.make_dir_recursive_absolute("res://build")
		var error := root.get_texture().get_image().save_png("res://build/smoke.png")
		check(error == OK, "Save rendered scene screenshot")
	print("Scene integration checks: ", "PASS" if failures == 0 else "FAIL (%d)" % failures)
	quit(0 if failures == 0 else 1)
