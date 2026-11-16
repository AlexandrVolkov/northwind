extends SceneTree
const Simulation = preload("res://scripts/simulation.gd")
var failures := 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var rules: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/prototype_rules.json"))
	rules.spawn_center = [3, 3]
	rules.units = [{"id": 1, "team": 0, "offset": [0, 0]}, {"id": 2, "team": 1, "offset": [2, 0]}]
	var cells: Array = []
	cells.resize(49)
	cells.fill(0)
	cells[2 * 7 + 3] = 1
	cells[4 * 7 + 3] = 2
	var data := {"width": 7, "height": 7, "cells": cells, "terrain": ["ground", "water", "forest"]}
	var model := Simulation.new()
	model.setup(data, rules)
	check(model.units.size() == 2, "Spawn two units")
	var unit: Dictionary = model.get_unit(1)
	check(model.move_unit(1, Vector2i(3, 2)).is_empty(), "Water blocks movement")
	check(model.move_unit(1, Vector2i(5, 3)).is_empty(), "Cannot move onto another unit")
	check(model.move_unit(1, Vector2i(7, 3)).is_empty(), "Cannot move outside map")
	check(not model.attack(1, 2), "Cannot attack at range")
	check(unit.moves == 6 and unit.cell == Vector2i(3, 3), "Invalid actions must not consume movement")
	var forest_path := model.path_for(unit, Vector2i(3, 4))
	check(model.route_cost(forest_path) == 2, "Forest costs two movement points")
	check(model.move_unit(1, Vector2i(4, 3)).size() == 2, "Adjacent route succeeds")
	check(unit.moves == 5, "Movement budget is deducted")
	check(model.attack(1, 2), "Adjacent hostile attack succeeds")
	check(model.get_unit(2).hp == 7 and unit.hp == 9, "Damage and retaliation match configuration")
	check(not model.attack(1, 2), "Cannot attack twice")
	check(unit.moves == 0, "Attack consumes remaining movement")
	check(model.move_unit(1, Vector2i(4, 4)).is_empty(), "Exhausted unit cannot move")
	model.end_turn()
	check(model.turn == 2 and unit.moves == 6 and not unit.attacked, "New turn resets friendly actions")
	check(unit.hp == 6, "Enemy attacks an adjacent friendly unit")
	model.get_unit(2).hp = 0
	check(model.unit_at(Vector2i(5, 3)).is_empty(), "Dead units do not occupy cells")
	check(not model.outcome().is_empty(), "Victory is reported")
	var previous_turn: int = model.turn
	model.end_turn()
	check(model.turn == previous_turn, "Finished battle does not keep advancing")
	# Complete blockage and friendly fire.
	model.setup(data, rules)
	for y in 7:
		model.grid.set_point_solid(Vector2i(4, y), true)
	check(model.path_for(model.get_unit(1), Vector2i(6, 6)).is_empty(), "Unreachable goal returns no route")
	model.get_unit(2).team = 0
	check(not model.attack(1, 2), "Friendly fire is rejected")
	print("Simulation checks: ", "PASS" if failures == 0 else "FAIL (%d)" % failures)
	quit(0 if failures == 0 else 1)
