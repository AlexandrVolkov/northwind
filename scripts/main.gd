extends Node2D

const Simulation = preload("res://scripts/simulation.gd")
const TILE := 32
const PANEL := 302.0
var model := Simulation.new()
var map_data: Dictionary
var configuration: Dictionary
var terrain_texture: Texture2D
var warrior_texture: Texture2D
var mini_texture: Texture2D
var camera := Camera2D.new()
var selected := -1
var hover := Vector2i(-1, -1)
var route: Array[Vector2i] = []
var show_grid := false
var ready_to_play := false
var clock := 0.0
var positions: Dictionary = {}
var animations: Dictionary = {}
var ui: CanvasLayer
var turn_label: Label
var selection_label: Label
var log_label: Label
var cell_label: Label
var end_button: Button
var minimap: TextureRect
var dragging := false
var minimap_marker: Control
var scenario_dialog: AcceptDialog
var scenario_text: RichTextLabel

func _ready() -> void:
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	add_child(camera)
	camera.position_smoothing_enabled = false
	camera.enabled = true
	build_ui()
	if not FileAccess.file_exists("res://assets/imported/map.json"):
		selection_label.text = "Нужны ресурсы игры\n\nЗапустите импорт:\npython tools/import_game.py\nпуть/к/Horde.ISO\n\nЗатем перезапустите проект.\nПодробности — в README."
		end_button.disabled = true
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string("res://assets/imported/map.json"))
	if not parsed is Dictionary or int(parsed.get("format_version", 0)) != 1:
		selection_label.text = "Неверный формат map.json.\nПовторите импорт ресурсов."
		end_button.disabled = true
		return
	map_data = parsed
	configuration = JSON.parse_string(FileAccess.get_file_as_string("res://data/prototype_rules.json"))
	terrain_texture = load_external_texture("terrain.png")
	warrior_texture = load_external_texture("warrior.png")
	mini_texture = load_external_texture("minimap.png")
	if terrain_texture == null or warrior_texture == null or mini_texture == null:
		selection_label.text = "Не удалось загрузить PNG.\nПовторите импорт ресурсов."
		end_button.disabled = true
		return
	minimap.texture = mini_texture
	ready_to_play = true
	restart()

func load_external_texture(filename: String) -> Texture2D:
	return load("res://assets/imported/" + filename) as Texture2D

func restart() -> void:
	model.setup(map_data, configuration)
	positions.clear()
	animations.clear()
	for unit in model.units:
		positions[unit.id] = center_of(unit.cell)
	selected = int(model.units[0].id) if not model.units.is_empty() else -1
	focus_selected()
	refresh_ui()
	queue_redraw()

func center_of(cell: Vector2i) -> Vector2:
	return Vector2(cell * TILE) + Vector2.ONE * TILE / 2.0

func focus_selected() -> void:
	var unit := model.get_unit(selected)
	if not unit.is_empty():
		# Offset the camera so the unit is centered in the map area, excluding the panel.
		camera.position = center_of(unit.cell) - Vector2(PANEL / (2.0 * camera.zoom.x), 0)
	clamp_camera()

func _process(delta: float) -> void:
	if not ready_to_play:
		return
	clock += delta
	var direction := Vector2.ZERO
	if Input.is_physical_key_pressed(KEY_A) or Input.is_physical_key_pressed(KEY_LEFT): direction.x -= 1
	if Input.is_physical_key_pressed(KEY_D) or Input.is_physical_key_pressed(KEY_RIGHT): direction.x += 1
	if Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP): direction.y -= 1
	if Input.is_physical_key_pressed(KEY_S) or Input.is_physical_key_pressed(KEY_DOWN): direction.y += 1
	camera.position += direction.normalized() * 650 * delta / camera.zoom.x
	clamp_camera()
	for id in animations.keys():
		var steps: Array = animations[id]
		if steps.is_empty():
			animations.erase(id)
			continue
		var destination := center_of(steps[0])
		positions[id] = Vector2(positions[id]).move_toward(destination, delta * 180)
		if Vector2(positions[id]).distance_to(destination) < 0.1:
			steps.pop_front()
	var new_hover := Vector2i((get_global_mouse_position() / TILE).floor())
	if new_hover != hover:
		hover = new_hover
		route = model.path_for(model.get_unit(selected), hover) if animations.is_empty() else []
		refresh_ui()
	end_button.disabled = not animations.is_empty() or not model.outcome().is_empty()
	queue_redraw()
	minimap_marker.queue_redraw()

func clamp_camera() -> void:
	var half := get_viewport_rect().size / camera.zoom / 2.0
	var world := Vector2(model.width * TILE, model.height * TILE)
	camera.position.x = clampf(camera.position.x, minf(half.x, world.x / 2), maxf(world.x - half.x, world.x / 2))
	camera.position.y = clampf(camera.position.y, minf(half.y, world.y / 2), maxf(world.y - half.y, world.y / 2))

func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_MIDDLE and not event.pressed:
		dragging = false

func _unhandled_input(event: InputEvent) -> void:
	if not ready_to_play:
		return
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_MIDDLE:
			dragging = event.pressed
		if not event.pressed:
			return
		if event.button_index == MOUSE_BUTTON_WHEEL_UP or event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			var before := get_global_mouse_position()
			var factor := 1.2 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 1.0 / 1.2
			camera.zoom = Vector2.ONE * clampf(camera.zoom.x * factor, 0.35, 2.5)
			camera.position += before - get_global_mouse_position()
			clamp_camera()
		if not animations.is_empty() or not model.outcome().is_empty():
			return
		var world_position: Vector2 = get_viewport().get_canvas_transform().affine_inverse() * event.position
		var cell := Vector2i((world_position / TILE).floor())
		var target := model.unit_at(cell)
		if event.button_index == MOUSE_BUTTON_LEFT:
			if not target.is_empty() and int(target.team) == 0:
				selected = int(target.id)
			elif not target.is_empty() and int(target.team) == 1:
				attack_target(target)
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			if not target.is_empty() and int(target.team) == 1:
				attack_target(target)
			else:
				var path := model.move_unit(selected, cell)
				if path.is_empty():
					model.log_event("Маршрут недоступен или не хватает очков движения.")
				else:
					animate_route(selected, path)
		route = model.path_for(model.get_unit(selected), cell)
		refresh_ui()
	elif event is InputEventMouseMotion and dragging:
		camera.position -= event.relative / camera.zoom
		clamp_camera()
	elif event is InputEventKey and event.pressed and not event.echo:
		match event.physical_keycode:
			KEY_SPACE: finish_turn()
			KEY_I: open_scenario()
			KEY_G: show_grid = not show_grid
			KEY_F: focus_selected()
			KEY_R: restart()
			KEY_ESCAPE: selected = -1; route.clear(); refresh_ui()

func animate_route(id: int, path: Array[Vector2i]) -> void:
	var steps: Array = []
	for index in range(1, path.size()): steps.append(path[index])
	animations[id] = steps
	route.clear()

func attack_target(target: Dictionary) -> void:
	if not model.attack(selected, int(target.id)):
		model.log_event("Для атаки встаньте на соседнюю клетку; нужен 1 ОД и неиспользованная атака.")

func finish_turn() -> void:
	if not ready_to_play or not animations.is_empty():
		return
	for action in model.end_turn():
		animate_route(int(action.id), action.path)
	refresh_ui()

func _draw() -> void:
	if not ready_to_play:
		return
	var half := get_viewport_rect().size / camera.zoom / 2.0
	var top_left := camera.position - half
	var bottom_right := camera.position + half
	var first := Vector2i((top_left / TILE).floor()).max(Vector2i.ZERO)
	var last := Vector2i((bottom_right / TILE).ceil()).min(Vector2i(model.width, model.height))
	for y in range(first.y, last.y):
		for x in range(first.x, last.x):
			var tile: int = int(model.cells[y * model.width + x]) & 0xffff
			var source := Rect2(Vector2(tile % 32, tile / 32) * TILE, Vector2.ONE * TILE)
			draw_texture_rect_region(terrain_texture, Rect2(x * TILE, y * TILE, TILE, TILE), source)
			if show_grid:
				draw_rect(Rect2(x * TILE, y * TILE, TILE, TILE), Color(1, 1, 1, 0.12), false)
	var selected_unit := model.get_unit(selected)
	if model.inside(hover) and get_viewport().get_mouse_position().x > PANEL:
		var tint := Color(1, 0.75, 0.4, 0.8) if model.passable(hover) else Color(1, 0.2, 0.2, 0.8)
		draw_rect(Rect2(Vector2(hover * TILE), Vector2.ONE * TILE), tint, false, 1.5)
	if not selected_unit.is_empty() and route.size() > 1 and animations.is_empty():
		var spent := 0
		for i in range(1, route.size()):
			spent += model.movement_cost(route[i])
			var tint := Color(0.5, 1, 0.55, 0.85) if spent <= int(selected_unit.moves) else Color(1, 0.35, 0.25, 0.65)
			draw_line(center_of(route[i - 1]), center_of(route[i]), tint, 2)
			draw_circle(center_of(route[i]), 2.5, tint)
	var ordered := model.units.duplicate()
	ordered.sort_custom(func(a: Dictionary, b: Dictionary): return Vector2(positions[a.id]).y < Vector2(positions[b.id]).y)
	for unit in ordered:
		if int(unit.hp) <= 0:
			continue
		var point: Vector2 = positions[unit.id]
		var friendly := int(unit.team) == 0
		var tint := Color("75dcce") if friendly else Color("f58c74")
		draw_set_transform(point, 0, Vector2(1, 0.45))
		draw_circle(Vector2.ZERO, 13, Color(tint, 0.2))
		draw_arc(Vector2.ZERO, 13, 0, TAU, 32, tint, 2)
		if int(unit.id) == selected:
			draw_arc(Vector2.ZERO, 17, 0, TAU, 32, Color("f2d895"), 2)
		draw_set_transform(Vector2.ZERO)
		var frame := int(clock * 8) % 8 if animations.has(unit.id) else 0
		draw_texture_rect_region(warrior_texture, Rect2(point - Vector2(31, 45), Vector2(62, 62)), Rect2(frame * 62, 0, 62, 62))
		draw_rect(Rect2(point + Vector2(-14, 17), Vector2(28, 4)), Color(0, 0, 0, 0.8))
		draw_rect(Rect2(point + Vector2(-13, 18), Vector2(26.0 * int(unit.hp) / int(configuration.unit.max_hp), 2)), tint)

func add_label(parent: Node, text: String, size: int, color: Color) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(label)
	return label

func build_ui() -> void:
	ui = CanvasLayer.new()
	add_child(ui)
	var panel := PanelContainer.new()
	panel.set_anchors_and_offsets_preset(Control.PRESET_LEFT_WIDE)
	panel.offset_right = PANEL
	var style := StyleBoxFlat.new()
	style.bg_color = Color("14201e")
	style.border_color = Color("42584d")
	style.border_width_right = 1
	style.content_margin_left = 20
	style.content_margin_right = 20
	style.content_margin_top = 22
	style.content_margin_bottom = 18
	panel.add_theme_stylebox_override("panel", style)
	ui.add_child(panel)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	panel.add_child(column)
	add_label(column, "СЕВЕРНЫЙ ВЕТЕР", 22, Color("f0dfb7"))
	add_label(column, "ОРДА / ИССЛЕДОВАТЕЛЬСКИЙ ПРОТОТИП", 10, Color("91a59b"))
	column.add_child(HSeparator.new())
	turn_label = add_label(column, "Карта 01", 18, Color("e3eadd"))
	minimap = TextureRect.new()
	minimap.custom_minimum_size = Vector2(256, 190)
	minimap.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	minimap.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	minimap.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	minimap.gui_input.connect(minimap_input)
	column.add_child(minimap)
	minimap_marker = Control.new()
	minimap_marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	minimap_marker.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	minimap_marker.draw.connect(draw_minimap_marker)
	minimap.add_child(minimap_marker)
	selection_label = add_label(column, "Загрузка…", 15, Color("e3eadd"))
	selection_label.custom_minimum_size.y = 66
	cell_label = add_label(column, "", 12, Color("91a59b"))
	end_button = Button.new()
	end_button.text = "ЗАВЕРШИТЬ ХОД    [ПРОБЕЛ]"
	end_button.custom_minimum_size.y = 42
	end_button.focus_mode = Control.FOCUS_NONE
	end_button.pressed.connect(finish_turn)
	column.add_child(end_button)
	var scenario_button := Button.new()
	scenario_button.text = "ДАННЫЕ СЦЕНАРИЯ    [I]"
	scenario_button.focus_mode = Control.FOCUS_NONE
	scenario_button.pressed.connect(open_scenario)
	column.add_child(scenario_button)
	add_label(column, "ЛКМ — выбрать / атаковать\nПКМ — идти / атаковать\nWASD, стрелки, СКМ — камера\nКолесо — масштаб · F — к отряду\nG — сетка · R — начать заново", 12, Color("aebfb1"))
	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(spacer)
	log_label = add_label(column, "", 12, Color("cfbd94"))
	add_label(column, "Оригинальные карта и графика.\nДвижение, бой и ИИ — экспериментальные;\nсоответствие оригиналу не проверено.", 11, Color("99a59a"))

func minimap_rect() -> Rect2:
	var side := minf(minimap.size.x, minimap.size.y)
	return Rect2((minimap.size - Vector2.ONE * side) / 2, Vector2.ONE * side)

func minimap_input(event: InputEvent) -> void:
	if not ready_to_play:
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		var rect := minimap_rect()
		if rect.has_point(event.position):
			camera.position = (event.position - rect.position) / rect.size * Vector2(model.width, model.height) * TILE
			clamp_camera()

func draw_minimap_marker() -> void:
	if not ready_to_play:
		return
	var rect := minimap_rect()
	var world := Vector2(model.width * TILE, model.height * TILE)
	var view := get_viewport_rect().size / camera.zoom
	var visible := Rect2(rect.position + (camera.position - view / 2) / world * rect.size, view / world * rect.size)
	minimap_marker.draw_rect(visible, Color(1, 0.9, 0.6, 0.85), false, 1)
	for unit in model.units:
		if int(unit.hp) > 0:
			var tint := Color("75dcce") if int(unit.team) == 0 else Color("f58c74")
			minimap_marker.draw_circle(rect.position + center_of(unit.cell) / world * rect.size, 2, tint)

func refresh_ui() -> void:
	if not ready_to_play:
		return
	turn_label.text = "Карта %02d  ·  Ход %d" % [int(map_data.map_number), model.turn]
	var unit := model.get_unit(selected)
	if not model.outcome().is_empty():
		selection_label.text = model.outcome()
	elif unit.is_empty():
		selection_label.text = "Выберите свой отряд\nБирюзовый круг — ваши войска."
	else:
		selection_label.text = "%s · отряд %d\nЗдоровье %d/%d  ·  Движение %d\nАтака: %s" % [str(configuration.unit.name),
			int(unit.id), int(unit.hp), int(configuration.unit.max_hp), int(unit.moves), "использована" if bool(unit.attacked) else "готова"]
	var names := {"ground": "земля", "forest": "лес", "water": "вода", "rock": "скалы", "outside": "за картой"}
	cell_label.text = "Клетка %d, %d · %s" % [hover.x, hover.y, names[model.terrain_at(hover)]]
	if route.size() > 1:
		cell_label.text += "\nСтоимость маршрута: %d ОД" % model.route_cost(route)
	log_label.text = "\n".join(model.events.slice(-4))

func open_scenario() -> void:
	if scenario_dialog == null:
		scenario_dialog = AcceptDialog.new()
		scenario_dialog.title = "Исходные данные сценария"
		scenario_dialog.min_size = Vector2i(740, 520)
		scenario_dialog.ok_button_text = "Закрыть"
		ui.add_child(scenario_dialog)
		scenario_text = RichTextLabel.new()
		scenario_text.custom_minimum_size = Vector2(700, 450)
		scenario_text.bbcode_enabled = false
		scenario_text.selection_enabled = true
		scenario_dialog.add_child(scenario_text)
	var path := "res://assets/imported/scenario.json"
	if not FileAccess.file_exists(path):
		scenario_text.text = "Данные сценария ещё не импортированы.\nПовторите импорт обновлённым tools/import_game.py."
	else:
		var scenario: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not scenario is Dictionary or not scenario.has("slots"):
			scenario_text.text = "Неверный scenario.json. Повторите импорт."
		else:
			var lines: Array[String] = ["Карта: %s · записей: %d" % [str(scenario.map_file), int(scenario.slot_count)],
				"Имена и ссылки прочитаны из SCN. Порядок записей не определяет команду.",
				"Размер OBE — число байтов, не число объектов. Расстановка в бою пока демонстрационная.", ""]
			for slot in scenario.slots:
				var leader := str(slot.leader_name) if not str(slot.leader_name).is_empty() else "(имя не задано)"
				lines.append("Запись %d · %s" % [int(slot.slot_index) + 1, leader])
				lines.append("Поселение: %s" % str(slot.settlement_name))
				lines.append("%s · %d байт · смещение SCN: %d\n" % [str(slot.object_file), int(slot.object_bytes), int(slot.file_offset)])
			scenario_text.text = "\n".join(lines)
	scenario_dialog.popup_centered(Vector2i(760, 540))
