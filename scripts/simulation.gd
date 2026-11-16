extends RefCounted
## Deterministic prototype rules. Nothing here claims original-game fidelity.

var width: int
var height: int
var cells: Array
var terrain: Array
var rules: Dictionary
var units: Array[Dictionary] = []
var turn := 1
var events: Array[String] = []
var grid := AStarGrid2D.new()

func setup(map: Dictionary, configuration: Dictionary) -> void:
	width = int(map.width)
	height = int(map.height)
	cells = map.cells
	terrain = map.terrain
	rules = configuration.duplicate(true)
	grid.region = Rect2i(0, 0, width, height)
	grid.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
	grid.default_compute_heuristic = AStarGrid2D.HEURISTIC_MANHATTAN
	grid.default_estimate_heuristic = AStarGrid2D.HEURISTIC_MANHATTAN
	grid.update()
	for y in height:
		for x in width:
			var cell := Vector2i(x, y)
			grid.set_point_solid(cell, not passable(cell))
			grid.set_point_weight_scale(cell, movement_cost(cell))
	units.clear()
	events.clear()
	turn = 1
	var center := Vector2i(int(rules.spawn_center[0]), int(rules.spawn_center[1]))
	for specification in rules.units:
		var desired := center + Vector2i(int(specification.offset[0]), int(specification.offset[1]))
		var position := find_spawn(desired)
		if position.x < 0:
			continue
		units.append({"id": int(specification.id), "team": int(specification.team),
			"cell": position, "hp": int(rules.unit.max_hp),
			"moves": int(rules.unit.movement), "attacked": false})
	log_event("Экспериментальные правила. Ваш ход.")

func inside(cell: Vector2i) -> bool:
	return cell.x >= 0 and cell.y >= 0 and cell.x < width and cell.y < height

func terrain_at(cell: Vector2i) -> String:
	if not inside(cell):
		return "outside"
	return str(terrain[int(cells[cell.y * width + cell.x]) & 0xffff])

func passable(cell: Vector2i) -> bool:
	return inside(cell) and terrain_at(cell) not in ["water", "rock"]

func movement_cost(cell: Vector2i) -> int:
	if terrain_at(cell) == "forest":
		return int(rules.movement.forest_cost)
	return int(rules.movement.ground_cost)

func unit_at(cell: Vector2i) -> Dictionary:
	for unit in units:
		if int(unit.hp) > 0 and unit.cell == cell:
			return unit
	return {}

func get_unit(id: int) -> Dictionary:
	for unit in units:
		if int(unit.id) == id and int(unit.hp) > 0:
			return unit
	return {}

func find_spawn(desired: Vector2i) -> Vector2i:
	for radius in range(maxi(width, height)):
		for y in range(desired.y - radius, desired.y + radius + 1):
			for x in range(desired.x - radius, desired.x + radius + 1):
				var cell := Vector2i(x, y)
				if passable(cell) and unit_at(cell).is_empty():
					return cell
	return Vector2i(-1, -1)

func path_for(unit: Dictionary, target: Vector2i) -> Array[Vector2i]:
	var empty: Array[Vector2i] = []
	if unit.is_empty() or not passable(target) or not unit_at(target).is_empty():
		return empty
	for other in units:
		if int(other.hp) > 0 and int(other.id) != int(unit.id):
			grid.set_point_solid(other.cell, true)
	var result := grid.get_id_path(unit.cell, target)
	for other in units:
		if int(other.hp) > 0 and int(other.id) != int(unit.id):
			grid.set_point_solid(other.cell, not passable(other.cell))
	return result

func route_cost(path: Array[Vector2i]) -> int:
	var total := 0
	for i in range(1, path.size()):
		total += movement_cost(path[i])
	return total

func move_unit(id: int, target: Vector2i) -> Array[Vector2i]:
	var unit := get_unit(id)
	var path := path_for(unit, target)
	if path.size() < 2 or route_cost(path) > int(unit.moves):
		return []
	unit.moves = int(unit.moves) - route_cost(path)
	unit.cell = target
	log_event("Отряд %d: переход, осталось %d ОД." % [id, int(unit.moves)])
	return path

func adjacent(a: Vector2i, b: Vector2i) -> bool:
	return absi(a.x - b.x) + absi(a.y - b.y) == 1

func attack(attacker_id: int, defender_id: int) -> bool:
	var attacker := get_unit(attacker_id)
	var defender := get_unit(defender_id)
	if attacker.is_empty() or defender.is_empty():
		return false
	if attacker.team == defender.team or bool(attacker.attacked) or int(attacker.moves) < 1:
		return false
	if not adjacent(attacker.cell, defender.cell):
		return false
	defender.hp = maxi(0, int(defender.hp) - int(rules.unit.damage))
	attacker.attacked = true
	attacker.moves = 0
	if int(defender.hp) > 0:
		attacker.hp = maxi(0, int(attacker.hp) - int(rules.unit.retaliation))
	log_event("Отряд %d → %d: урон %d; ответ %d." % [attacker_id, defender_id,
		int(rules.unit.damage), int(rules.unit.retaliation) if int(defender.hp) > 0 else 0])
	return true

func outcome() -> String:
	var friendly := 0
	var enemy := 0
	for unit in units:
		if int(unit.hp) <= 0:
			continue
		if int(unit.team) == 0:
			friendly += 1
		else:
			enemy += 1
	if friendly == 0:
		return "Поражение. R — начать заново."
	if enemy == 0:
		return "Победа. R — начать заново."
	return ""

func end_turn() -> Array[Dictionary]:
	var actions: Array[Dictionary] = []
	if not outcome().is_empty():
		return actions
	for enemy in units:
		if int(enemy.team) != 1 or int(enemy.hp) <= 0:
			continue
		enemy.moves = int(rules.unit.movement)
		enemy.attacked = false
		var candidates: Array[Dictionary] = []
		for friend in units:
			if int(friend.team) == 0 and int(friend.hp) > 0:
				candidates.append(friend)
		candidates.sort_custom(func(a: Dictionary, b: Dictionary):
			return Vector2(enemy.cell).distance_squared_to(a.cell) < Vector2(enemy.cell).distance_squared_to(b.cell))
		var best: Array[Vector2i] = []
		for friend in candidates:
			if adjacent(enemy.cell, friend.cell):
				attack(int(enemy.id), int(friend.id))
				break
			for direction in [Vector2i.LEFT, Vector2i.RIGHT, Vector2i.UP, Vector2i.DOWN]:
				var route := path_for(enemy, friend.cell + direction)
				if route.size() > 1 and (best.is_empty() or route_cost(route) < route_cost(best)):
					best = route
		if not bool(enemy.attacked) and best.size() > 1:
			var affordable: Array[Vector2i] = [best[0]]
			var spent := 0
			for i in range(1, best.size()):
				spent += movement_cost(best[i])
				if spent > int(enemy.moves):
					break
				affordable.append(best[i])
			if affordable.size() > 1:
				var moved := move_unit(int(enemy.id), affordable.back())
				actions.append({"id": int(enemy.id), "path": moved})
			for friend in candidates:
				if int(friend.hp) > 0 and adjacent(enemy.cell, friend.cell):
					attack(int(enemy.id), int(friend.id))
					break
	turn += 1
	for friend in units:
		if int(friend.team) == 0 and int(friend.hp) > 0:
			friend.moves = int(rules.unit.movement)
			friend.attacked = false
	log_event("Ход %d. Ваши отряды готовы." % turn)
	return actions

func log_event(message: String) -> void:
	events.append(message)
	if events.size() > 6:
		events.pop_front()
