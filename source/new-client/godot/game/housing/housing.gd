class_name Housing
extends Node
## Player housing (1.64 Foundations) on the server: what the server has told us about the houses around you and the
## one you are in. The server's packets (DaocClient: kinds "house", "garden", "house_occupied", "house_enter",
## "house_exit", "furniture", "house_points", "house_rotate_ok", "house_perms", "house_users") come in through
## ServerWorld; the drawing (HouseView) and the windows listen to the signals below. Protocol spec (2026-11-02):
## scratchpad housing\server_protocol.md - DBHouse lots 1-1800 Albion (region 2), 2001-3800 Midgard (102), 4001-5800
## Hibernia (202); models 1-4 Albion cottage / house / villa / mansion, 5-8 Midgard, 9-12 Hibernia; the interior is
## at the house's X / Y, Z 25000, axis-aligned.

signal house_changed(number: int)            # created / rebuilt / materials / porch / banners (0xD1)
signal house_removed(number: int)
signal garden_changed(number: int)
signal occupied_changed(number: int)
signal entered(number: int)                  # you are inside (0x08)
signal left(number: int)
signal furniture_changed(number: int)
signal points_toggled(number: int)           # /housepoints: the hookpoint markers on / off
signal rotate_allowed(number: int, index: int)
signal permissions(number: int, levels: Array)
signal users(number: int, list: Array)

const VIEW := 10120.0                        # units: the server sends houses within this and never removes them again
const MODEL_NAMES := ["", "cottage", "house", "villa", "mansion"]

## number -> {number, x, y, z, heading (degrees), model, roof, wall, door, truss, porch_material, window, porch,
## porch_roof_color, banner, shield, emblem, name, garden: {key: {model, position, rotation}}, occupied}
var houses := {}
var inside := 0                              # the house you are in (0 = outside)
var interior := {}                           # its 0x08: model, door, rugs [4], banner, shield, emblem
var furniture := {}                          # index -> {model, color?, emblem?, x, y, rotation, size, position, mode}
var points_shown := false


## Realm of a model / lot: 1 Albion, 2 Midgard, 3 Hibernia.
static func realm_of_model(model: int) -> int:
	return 0 if model <= 0 else (model - 1) / 4 + 1


## cottage / house / villa / mansion.
static func size_name(model: int) -> String:
	return "" if model <= 0 else MODEL_NAMES[(model - 1) % 4 + 1]


func on_packet(kind: String, d: Dictionary) -> void:
	var n := int(d.get("number", 0))
	match kind:
		"house":
			if int(d.model) == 0 and String(d.get("name", "")) == "":
				# SendRemoveHouse (the 168 layout: model 0, no owner name) - the lot is free again; an owned lot with no
				# house yet is model 0 WITH its owner's name
				if houses.erase(n):
					house_removed.emit(n)
				return
			var h: Dictionary = houses.get(n, {"garden": {}, "occupied": false})
			for k in d:
				if k != "kind":
					h[k] = d[k]
			houses[n] = h
			house_changed.emit(n)
		"garden":
			var h: Dictionary = houses.get(n, {"number": n, "garden": {}, "occupied": false})
			if bool(d.full):
				h.garden = {}
			for e in d.items:
				h.garden[int(e.key)] = {"model": int(e.model), "position": int(e.position), "rotation": int(e.rotation)}
			houses[n] = h
			garden_changed.emit(n)
		"house_occupied":
			if houses.has(n):
				houses[n].occupied = bool(d.occupied)
				occupied_changed.emit(n)
		"house_enter":
			inside = n
			interior = d.duplicate()
			furniture = {}
			points_shown = false
			entered.emit(n)
		"house_exit":
			var was := inside
			inside = 0
			interior = {}
			furniture = {}
			points_shown = false
			left.emit(was if was != 0 else n)
		"furniture":
			if bool(d.full):
				furniture = {}
			for e in d.items:
				if int(e.model) == 0:
					furniture.erase(int(e.index))
				else:
					furniture[int(e.index)] = e
			furniture_changed.emit(n)
		"house_points":
			points_shown = not points_shown
			points_toggled.emit(n)
		"house_rotate_ok":
			rotate_allowed.emit(n, int(d.index))
		"house_perms":
			permissions.emit(n, d.levels)
		"house_users":
			users.emit(n, d.users)


## The houses the server no longer keeps you posted on (out of its 10120 units): forgotten (it never sends removes).
func cull(me_x: float, me_y: float) -> void:
	var gone := []
	for n in houses:
		var h: Dictionary = houses[n]
		if h.has("x") and Vector2(float(h.x) - me_x, float(h.y) - me_y).length() > VIEW * 1.1:
			gone.append(n)
	for n in gone:
		houses.erase(n)
		house_removed.emit(n)


## The house you are in, or the one you face: the lot's own record.
func house(number: int) -> Dictionary:
	return houses.get(number, {})
