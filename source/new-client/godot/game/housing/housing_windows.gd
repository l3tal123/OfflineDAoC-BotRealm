class_name HousingWindows
extends Node
## The 1.64 client's housing windows, laid out as its game.dll builds them (research 2026-11-02, scratchpad
## housing/ui164.md - window-local pixels, the frame bitmap's size): House External Options and House Internal
## Options (options.bmp 384 x 420), House Garden Placement (phousing garden.bmp 375 x 417), House Friend Level
## Permissions and House Permissions List (options.bmp), Rotate Object (leftbot.bmp 128 x 96). Image buttons from
## buttons / buttons2 / buttons3.bmp (58 x 16 cells, normal then highlight), check boxes listicon1.bmp.
## They talk to the server in its 1.127 protocol (DaocClient's housing requests): the 1.64 client's own opcodes and
## menu numbers differ, the server keeps both - 1.127 menus: 0 garden shop, 1 decoration shop, 3 exterior options,
## 4 NPC / 5 vault / 6 tools / 7 bindstone hookpoint lists, 8 interior options, 9 house information.
## The interior lock is left out: 1.64 has no control that reaches it (dead code there).

const LEVELS := ["Visitor", "Guest", "Resident", "Tenant", "Acquaintance", "Associate", "Friend", "Ally", "Partner"]
const TYPES := ["N/A", "Player", "Guild", "Guild Rank", "Account", "Everyone", "Alliance"]
const EXT_ROWS := [["Roof Material", 59, "roof"], ["Wall Material", 60, "wall"], ["Door Material", 61, "door"],
		["Wood Color", 63, "truss"], ["Shutter Material", 64, "window"], ["Porch Material", 62, "porch_material"],
		["Tent Color", 56, "porch_roof_color"], ["Guild Banner", 57, "banner"], ["Guild Shield", 58, "shield"]]
const INT_ROWS := [["1st Floor Carpets", 52, 0], ["2nd Floor Carpets", 69, 1], ["3rd Floor Carpets", 70, 2],
		["4th Floor Carpets", 71, 3], ["Banners", 66, "banner"], ["Shields", 67, "shield"]]
## server merchant window types (eMerchantWindowType) -> the 1.64 store titles (game.dll 0x414e60)
const STORE_TITLES := {3: "House Store", 5: "House Merchant Store", 6: "House Decoration Store", 7: "House Garden Store",
		8: "House Vault Store", 9: "House Tools Store", 10: "House Bindstone Store"}

var ui: Node                                  # GameUI (a CanvasLayer)
var housing: Node
var house_view: Node
var windows := {}                             # id -> ClassicWindow
var _rows := []                               # the open options window's rows: {type, field, items, sel, cur}
var _options_house := 0
var _options_inside := false


static var current: Node                      # (the inventory's coins open the Money Selector through it)


func setup(game_ui: Node, h: Node, view: Node) -> void:
	current = self
	ui = game_ui
	housing = h
	house_view = view
	h.permissions.connect(_open_permissions)
	h.users.connect(_open_users)
	h.rotate_allowed.connect(_open_rotate)
	h.left.connect(func(_n): _close_inside())
	h.garden_changed.connect(func(n): if windows.has("garden") and windows.garden.visible and n == _options_house: _paint_garden())


# ---------------------------------------------------------------- shared pieces

## An image button from a 1.64 button sheet: index -> row index/2, even indexes columns 0/1, odd 2/3 (normal,
## highlight), 58 x 16 cells scaled to `size` (so the 16 x 16 arrows are squeezed as in the client).
static func sprite_button(sheet: String, index: int, at: Vector2, size: Vector2, on_press: Callable) -> TextureButton:
	var b := TextureButton.new()
	var y := float(index / 2) * 16.0
	var x := 0.0 if index % 2 == 0 else 116.0
	b.texture_normal = UiArt.region(sheet, Rect2(x, y, 58, 16))
	b.texture_hover = UiArt.region(sheet, Rect2(x + 58, y, 58, 16))
	b.texture_pressed = b.texture_hover
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_SCALE
	b.position = at
	b.size = size
	b.focus_mode = Control.FOCUS_NONE
	b.pressed.connect(on_press)
	return b


func _label(w: Control, text: String, at: Vector2, color := UiArt.CREAM, size := 9, width := 0.0,
		align := HORIZONTAL_ALIGNMENT_LEFT) -> Label:
	var l := UiArt.label(text, size, color)
	l.position = at
	l.size = Vector2(width if width > 0.0 else 360.0, 14)
	l.horizontal_alignment = align
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	w.add_child(l)
	return l


func _window(id: String, frame: String, title: String, at: Vector2) -> ClassicWindow:
	if windows.has(id):
		return windows[id]
	var w := ClassicWindow.new()
	w.setup(id, frame, Rect2(), ui.client_pos(at.x, at.y))
	w.visible = false
	ui.root.add_child(w)
	# (the 1.64 default places are for 800 x 600 - the garden's 200, 500 puts most of it under the screen's edge)
	w.position = w.position.clamp(Vector2.ZERO, (ui.area() - w.size).max(Vector2.ZERO))
	_label(w, title, Vector2(0, 4), UiArt.GOLD_HI, 10, w.size.x, HORIZONTAL_ALIGNMENT_CENTER)
	windows[id] = w
	return w


func _clear(w: Control, keep := 2) -> void:
	# (keep the frame and the title)
	var kids := w.get_children()
	for i in range(kids.size()):
		if i >= keep and not (kids[i] == (w as ClassicWindow).frame):
			kids[i].queue_free()


static func money(copper: int) -> String:
	return PlayerData.money_string(copper) if copper > 0 else "0c"


func _send(method: String, args: Array) -> void:
	if Server.client:
		Server.client.callv(method, args)


# ---------------------------------------------------------------- the server's housing merchant windows

## A server merchant window (0x17) of a housing type: 4 / 11 the exterior / interior options, 7 the garden store
## and the garden placement window, the others the merchant window under its 1.64 store title. True if taken.
## pages: page -> [ItemDb runtime ids "srvm:<page>:<slot>"] (ServerWorld._merchant).
func merchant(window_type: int, pages: Dictionary) -> bool:
	match window_type:
		4:
			_open_options(false, pages)
			return true
		11:
			_open_options(true, pages)
			return true
		7:
			_open_garden()
			return false                                  # (the store itself opens as a merchant window too)
	return false


static func store_title(window_type: int) -> String:
	return String(STORE_TITLES.get(window_type, ""))


# ---------------------------------------------------------------- External / Internal Options

func _house_number() -> int:
	if housing.inside != 0:
		return housing.inside
	return _options_house


## The options window over the house's current look: per row the menu items of its object type (pages of 30: the
## HouseEdit index is page * 30 + slot), stepped with the arrows - a local preview only; BUY sends the changed rows.
func _open_options(inside: bool, pages: Dictionary) -> void:
	_options_inside = inside
	var id := "int_options" if inside else "ext_options"
	var w := _window(id, "options", "House Internal Options" if inside else "House External Options",
			Vector2(200, 200) if inside else Vector2(300, 200))
	_clear(w)
	var n := _house_number()
	var h: Dictionary = housing.house(n)
	var specs: Array = INT_ROWS if inside else EXT_ROWS
	_rows = []
	_label(w, "Purchase Interior Upgrades/Options:" if inside else "Purchase Exterior Upgrades/Options:",
			Vector2(10, 32), UiArt.DIM)
	var floors := ((int(h.get("model", housing.interior.get("model", 1))) - 1) % 4) + 1
	for i in specs.size():
		var spec: Array = specs[i]
		var list := []
		for pg in pages:
			for iid in pages[pg]:
				var it: Dictionary = ItemDb.item(String(iid))
				if int(it.get("object_type", -1)) == int(spec[1]):
					var slot := int(String(iid).get_slice(":", 2))
					list.append({"index": int(pg) * 30 + slot, "name": String(it.name),
							"value": int(it.get("dps_af", 0)), "price": int(it.get("price", 0))})
		var cur := _current(spec[2], h)
		var sel := -1
		for k in list.size():
			if int(list[k].value) == cur:
				sel = k
		var disabled := inside and i < 4 and i >= floors           # (a carpet row above the house's floors: n/a)
		var y := 50.0 + 18.0 * i
		var row := {"type": int(spec[1]), "field": spec[2], "items": list, "sel": sel, "start": sel, "disabled": disabled}
		_label(w, String(spec[0]), Vector2(0, y + 1), UiArt.CREAM, 9, 150.0, HORIZONTAL_ALIGNMENT_RIGHT)
		row["value"] = _label(w, "", Vector2(176, y + 1), UiArt.DIM, 9, 164.0, HORIZONTAL_ALIGNMENT_CENTER)
		var r := _rows.size()
		w.add_child(sprite_button("buttons", 10, Vector2(160, y), Vector2(16, 16), func(): _step(r, -1)))
		w.add_child(sprite_button("buttons", 11, Vector2(340, y), Vector2(16, 16), func(): _step(r, 1)))
		_rows.append(row)
	var base := 50.0 + 18.0 * specs.size()
	var total := _label(w, "Total cost:", Vector2(10, base + 1))
	w.set_meta("total", total)
	w.add_child(sprite_button("buttons", 0, Vector2(132, base + 18), Vector2(58, 16), func(): _buy_options()))
	w.add_child(sprite_button("buttons", 0xd, Vector2(196, base + 18), Vector2(58, 16), func(): _close_options()))
	if inside:
		_label(w, "Buy Interior Decorations:", Vector2(10, 212), UiArt.DIM)
		w.add_child(sprite_button("buttons2", 0x1b, Vector2(250, 209), Vector2(58, 16), func(): _send("HouseMenu", [n, 1, 0])))
		_label(w, "House Information:", Vector2(10, 239), UiArt.DIM)
		w.add_child(sprite_button("buttons2", 0x17, Vector2(250, 236), Vector2(58, 16), func(): _send("HouseMenu", [n, 9, 0])))
		_label(w, "Show/Hide Hookpoints:", Vector2(10, 266), UiArt.DIM)
		w.add_child(sprite_button("buttons3", 0, Vector2(250, 263), Vector2(58, 16), func(): Server.command("/housepoints toggle")))
		_label(w, "(right-click Hookpoints to purchase Hookpoint Items)", Vector2(10, 287), UiArt.DIM, 8)
		_label(w, "View House Friend Level Permission:", Vector2(10, 314), UiArt.DIM)
		w.add_child(sprite_button("buttons3", 4, Vector2(250, 311), Vector2(58, 16), func():
			if windows.has("perms") and windows.perms.visible:
				windows.perms.hide_window()
			else:
				_send("HousePermsRequest", [n])))
		_label(w, "View House Friend Permissions List:", Vector2(10, 341), UiArt.DIM)
		w.add_child(sprite_button("buttons", 0x1e, Vector2(250, 338), Vector2(58, 16), func():
			if windows.has("users") and windows.users.visible:
				windows.users.hide_window()
			else:
				_send("HouseUsersRequest", [n])))
	else:
		_label(w, "Purchase/Place Garden Objects:", Vector2(10, 266), UiArt.DIM)
		w.add_child(sprite_button("buttons2", 0x19, Vector2(250, 263), Vector2(58, 16), func(): _send("HouseMenu", [n, 0, 0])))
		_label(w, "House Information:", Vector2(10, 302), UiArt.DIM)
		w.add_child(sprite_button("buttons2", 0x17, Vector2(250, 299), Vector2(58, 16), func(): _send("HouseMenu", [n, 9, 0])))
	_paint_rows()
	w.show_window()


func _current(field, h: Dictionary) -> int:
	if field is int:                                       # an interior carpet: its rug colour
		var rugs: Array = housing.interior.get("rugs", [0, 0, 0, 0])
		return int(rugs[int(field)]) if int(field) < rugs.size() else 0
	if String(field) in ["banner", "shield"]:
		var src: Dictionary = housing.interior if _options_inside else h
		return 1 if bool(src.get(String(field), false)) else 0
	return int(h.get(String(field), 0))


func _step(r: int, d: int) -> void:
	var row: Dictionary = _rows[r]
	var n: int = (row.items as Array).size()
	if n == 0 or bool(row.disabled):
		return
	row.sel = (int(row.sel) + d + n) % n if int(row.sel) >= 0 else (0 if d > 0 else n - 1)
	_paint_rows()
	if not _options_inside:                                   # the live preview (nothing sent)
		var over := {}
		for rw in _rows:
			if int(rw.sel) >= 0 and String(rw.field) in ["roof", "wall", "door", "truss", "window", "porch_material"]:
				over[rw.field] = int(rw.items[rw.sel].value)       # (the item's DPS_AF = the material byte)
		house_view.preview(_house_number(), over)


func _paint_rows() -> void:
	var cost := 0
	for row in _rows:
		var l: Label = row.value
		if bool(row.disabled):
			l.text = "n/a"
			l.add_theme_color_override("font_color", UiArt.DIM)
			continue
		var sel := int(row.sel)
		l.text = String(row.items[sel].name) if sel >= 0 else "none"
		var changed := sel >= 0 and sel != int(row.start)
		l.add_theme_color_override("font_color", UiArt.CREAM if changed else UiArt.DIM)
		if changed:
			cost += int(row.items[sel].price)
	var w: ClassicWindow = windows.get("int_options" if _options_inside else "ext_options")
	if w and w.has_meta("total"):
		(w.get_meta("total") as Label).text = "Total cost:  %s" % money(cost)


func _buy_options() -> void:
	var picks := []
	for row in _rows:
		if int(row.sel) >= 0 and int(row.sel) != int(row.start) and not bool(row.disabled):
			picks.append(int(row.items[row.sel].index))
	if not picks.is_empty():
		_send("HouseEdit", [picks])
	_close_options()


func _close_options() -> void:
	for id in ["ext_options", "int_options"]:
		if windows.has(id) and windows[id].visible:
			windows[id].hide_window()
	if not _options_inside and house_view:
		house_view.preview(_house_number(), {})               # the house's real look back


func _close_inside() -> void:
	for id in ["int_options", "perms", "users", "rotate", "garden"]:
		if windows.has(id) and windows[id].visible:
			windows[id].hide_window()


## Shift + right-click on a house from outside (its exterior) or inside (a wall / floor): its options window.
func ask_options(number: int, inside: bool) -> void:
	_options_house = number
	_send("HouseMenu", [number, 8 if inside else 3, 0])


# ---------------------------------------------------------------- House Garden Placement

var _garden_rot := 0
var _garden_cells: Array = []                  # 18 x 18 cell rects (window-local)


func _open_garden() -> void:
	var w := _window("garden", "garden", "House Garden Placement", Vector2(200, 500))
	_clear(w)
	var grid := GardenGrid.new()
	grid.owner_windows = self
	grid.position = Vector2(7, 24)
	grid.size = Vector2(360, 360)
	w.add_child(grid)
	w.set_meta("grid", grid)
	_label(w, "Left-click to place or remove", Vector2(7, 400), UiArt.CREAM, 8)
	_label(w, "SHIFT+Right-click to rotate", Vector2(222, 400), UiArt.CREAM, 8)
	_paint_garden()
	w.show_window()


func _paint_garden() -> void:
	var w: ClassicWindow = windows.get("garden")
	if w and w.has_meta("grid"):
		(w.get_meta("grid") as Control).queue_redraw()


## cell (col 0..17 from the RIGHT, row 0..17 from the BOTTOM - game.dll 0x4753e7); the lot's 16 x 16 inside the ring
func garden_state(col: int, row: int) -> int:
	if col < 1 or col > 16 or row < 1 or row > 16:
		return 0
	var pos := (row - 1) * 16 + (col - 1)
	var n := _house_number()
	if house_view and not house_view.has_garden_point(n, pos):
		return 0
	for e in (housing.house(n).get("garden", {}) as Dictionary).values():
		if int(e.position) == pos:
			return 2
	return 1


func garden_drop(col: int, row: int, slot: int) -> void:
	if garden_state(col, row) != 1:
		PlayerData.message.emit("You may not place that here.", "error")
		return
	_send("HousePlace", [slot, _house_number(), (row - 1) * 16 + (col - 1), 1, _garden_rot, 0, 0])


func garden_click(col: int, row: int) -> void:
	if garden_state(col, row) == 2:
		_send("HousePickup", [_house_number(), (row - 1) * 16 + (col - 1), 1])


func garden_rotate() -> void:
	_garden_rot = (_garden_rot + 1) % 4
	PlayerData.message.emit("Garden object rotation: %d degrees." % (_garden_rot * 90), "system")


class GardenGrid extends Control:
	var owner_windows: Node

	func _cell(p: Vector2) -> Vector2i:
		return Vector2i(17 - int(p.x / 20.0), 17 - int(p.y / 20.0))

	func _draw() -> void:
		for col in 18:
			for row in 18:
				var st: int = owner_windows.garden_state(col, row)
				var at := Vector2(340 - 20 * col, 340 - 20 * row)
				if st == 0:
					draw_texture_rect(UiArt.tex("ctrlpoint_none"), Rect2(at, Vector2(20, 20)), false)
				elif st == 2:
					draw_texture_rect(UiArt.tex("ctrlpoint_full"), Rect2(at, Vector2(20, 20)), false)

	func _can_drop_data(_p: Vector2, data: Variant) -> bool:
		return InventorySheet.is_item(data) and int(PlayerData.item_at(int(data.slot)).get("object_type", 0)) == 49

	func _drop_data(p: Vector2, data: Variant) -> void:
		var c := _cell(p)
		owner_windows.garden_drop(c.x, c.y, int(data.slot))

	func _gui_input(e: InputEvent) -> void:
		if e is InputEventMouseButton and e.pressed:
			if e.button_index == MOUSE_BUTTON_LEFT:
				var c := _cell(e.position)
				owner_windows.garden_click(c.x, c.y)
				accept_event()
			elif e.button_index == MOUSE_BUTTON_RIGHT and e.shift_pressed:
				owner_windows.garden_rotate()
				accept_event()


# ---------------------------------------------------------------- House Friend Level Permissions

var _perm_levels := {}                          # level -> {enter, vault1..4, appearance, interior, garden, ...}
var _perm_shown := 1
var _perm_boxes := {}


## The server's 0x05: every friend level's rights - shown one level at a time (the level box picks it).
func _open_permissions(number: int, levels: Array) -> void:
	_options_house = number
	_perm_levels = {}
	for l in levels:
		_perm_levels[int(l.level)] = (l as Dictionary).duplicate()
	var w := _window("perms", "options", "House Friend Level Permissions", Vector2(100, 100))
	_clear(w)
	_perm_boxes = {}
	_label(w, "Friend Level", Vector2(20, 32))
	var combo := OptionButton.new()
	for i in LEVELS.size():
		combo.add_item(LEVELS[i], i + 1)
	combo.position = Vector2(225, 27)
	combo.size = Vector2(140, 20)
	combo.select(_perm_shown - 1)
	UiArt.themed(combo)
	combo.item_selected.connect(func(i: int):
		_accept_perms()                                     # (the level shown before is saved, as the client)
		_perm_shown = i + 1
		_paint_perms())
	w.add_child(combo)
	_label(w, "Vault Options", Vector2(20, 64))
	for h in [["View", 230], ["Add", 280], ["Remove", 330]]:
		_label(w, h[0], Vector2(float(h[1]) - 30, 80), UiArt.CREAM, 9, 60.0, HORIZONTAL_ALIGNMENT_CENTER)
	for v in 4:
		var y := 96.0 + 16.0 * v
		_label(w, "Vault %d" % (v + 1), Vector2(40, y + 1))
		for b in [[4, 225], [1, 275], [2, 325]]:
			_box(w, "vault%d" % (v + 1), int(b[0]), Vector2(float(b[1]), y + 4))
	_label(w, "Decorations", Vector2(20, 176))
	_label(w, "Add", Vector2(200, 192), UiArt.CREAM, 9, 60.0, HORIZONTAL_ALIGNMENT_CENTER)
	_label(w, "Remove", Vector2(250, 192), UiArt.CREAM, 9, 60.0, HORIZONTAL_ALIGNMENT_CENTER)
	for d in [["Interior", "interior", 208], ["Garden", "garden", 224]]:
		_label(w, d[0], Vector2(40, float(d[2]) + 1))
		_box(w, d[1], 1, Vector2(225, float(d[2]) + 4))
		_box(w, d[1], 2, Vector2(275, float(d[2]) + 4))
	_label(w, "Access", Vector2(20, 256))
	_label(w, "Enable", Vector2(200, 272), UiArt.CREAM, 9, 60.0, HORIZONTAL_ALIGNMENT_CENTER)
	var access := [["Use Tools", "tools"], ["Use Merchants", "merchants"], ["Enter House", "enter"],
			["Ability to Banish", "banish"], ["Use Bind Stones", "bind"], ["Change External Appearance", "appearance"]]
	for i in access.size():
		var y := 288.0 + 16.0 * i
		_label(w, access[i][0], Vector2(40, y + 1))
		_box(w, access[i][1], 0, Vector2(225, y + 4))
	w.add_child(sprite_button("buttons", 0xc, Vector2(133, 396), Vector2(58, 16), func():
		_accept_perms()
		_send("HousePermsRequest", [_options_house])))
	w.add_child(sprite_button("buttons", 0xd, Vector2(193, 396), Vector2(58, 16), func(): w.hide_window()))
	_paint_perms()
	w.show_window()


## A check box (listicon1.bmp: checked top half, hollow bottom); bit 0 = a yes / no right, else that bit of the byte.
func _box(w: Control, key: String, bit: int, at: Vector2) -> void:
	var b := TextureButton.new()
	b.toggle_mode = true
	b.texture_normal = UiArt.region("listicon1", Rect2(0, 10, 10, 10))
	b.texture_pressed = UiArt.region("listicon1", Rect2(0, 0, 10, 10))
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_SCALE
	b.position = at
	b.size = Vector2(10, 10)
	b.focus_mode = Control.FOCUS_NONE
	b.toggled.connect(func(on: bool):
		var l: Dictionary = _perm_levels.get(_perm_shown, {})
		var v := int(l.get(key, 0))
		if bit == 0:
			v = 1 if on else 0
		else:
			v = (v | bit) if on else (v & ~bit)
		l[key] = v
		_perm_levels[_perm_shown] = l)
	w.add_child(b)
	_perm_boxes["%s/%d" % [key, bit]] = b


func _paint_perms() -> void:
	var l: Dictionary = _perm_levels.get(_perm_shown, {})
	for k in _perm_boxes:
		var parts := String(k).split("/")
		var v := int(l.get(parts[0], 0))
		var bit := int(parts[1])
		(_perm_boxes[k] as TextureButton).set_pressed_no_signal(v != 0 if bit == 0 else (v & bit) != 0)


func _accept_perms() -> void:
	var l: Dictionary = _perm_levels.get(_perm_shown, {})
	if l.is_empty():
		return
	var order := ["enter", "vault1", "vault2", "vault3", "vault4", "appearance", "interior", "garden", "banish",
			"merchants", "tools", "bind", "consignment", "pay_rent"]
	var bytes := PackedByteArray()
	for k in order:
		bytes.append(int(l.get(k, 0)) & 0xFF)
	_send("HousePermsSet", [_options_house, _perm_shown, bytes])


# ---------------------------------------------------------------- House Permissions List

var _users: Array = []


func _open_users(number: int, list: Array) -> void:
	_options_house = number
	_users = []
	for e in list:
		var u: Dictionary = (e as Dictionary).duplicate()
		u["start"] = int(u.level)
		u["remove"] = false
		_users.append(u)
	var w := _window("users", "options", "House Permissions List", Vector2(100, 100))
	_clear(w)
	_label(w, "Type", Vector2(8, 28))
	_label(w, "Name", Vector2(75, 28))
	_label(w, "Level", Vector2(206, 28), UiArt.CREAM, 9, 76.0, HORIZONTAL_ALIGNMENT_CENTER)
	for k in 20:
		var y := 45.0 + 17.0 * k
		var row := {"type": _label(w, "", Vector2(8, y + 2), UiArt.CREAM, 8),
				"name": _label(w, "", Vector2(75, y + 2), UiArt.CREAM, 8),
				"level": _label(w, "", Vector2(206, y + 2), UiArt.CREAM, 8, 76.0, HORIZONTAL_ALIGNMENT_CENTER)}
		w.set_meta("row%d" % k, row)
		if k < _users.size():
			w.add_child(sprite_button("buttons", 10, Vector2(190, y), Vector2(16, 16), func(): _user_level(k, 1)))
			w.add_child(sprite_button("buttons", 11, Vector2(282, y), Vector2(16, 16), func(): _user_level(k, -1)))
			w.add_child(sprite_button("buttons3", 2, Vector2(300, y), Vector2(58, 16), func():
				_users[k].remove = not bool(_users[k].remove)
				_paint_users()))
	w.add_child(sprite_button("buttons", 0xc, Vector2(133, 399), Vector2(58, 16), func(): _accept_users()))
	w.add_child(sprite_button("buttons", 0xd, Vector2(193, 399), Vector2(58, 16), func(): w.hide_window()))
	_paint_users()
	w.show_window()


func _user_level(k: int, d: int) -> void:
	var lv := (int(_users[k].level) + d + 10) % 10           # (as game.dll 0x476b4c: the left arrow counts up)
	_users[k].level = lv
	_paint_users()


func _paint_users() -> void:
	var w: ClassicWindow = windows.get("users")
	if w == null:
		return
	for k in 20:
		var row: Dictionary = w.get_meta("row%d" % k)
		if k >= _users.size():
			(row.type as Label).text = ""
			(row.name as Label).text = ""
			(row.level as Label).text = "N/A"
			for l in row.values():
				(l as Label).add_theme_color_override("font_color", UiArt.DIM)
			continue
		var u: Dictionary = _users[k]
		(row.type as Label).text = TYPES[clampi(int(u.type), 0, TYPES.size() - 1)]
		(row.name as Label).text = String(u.name)
		var lv := int(u.level)
		(row.level as Label).text = "None" if lv <= 0 else LEVELS[clampi(lv - 1, 0, 8)]
		var c := UiArt.RED if bool(u.remove) else (UiArt.GOLD_HI if lv != int(u.start) else UiArt.CREAM)
		for l in row.values():
			(l as Label).add_theme_color_override("font_color", c)


func _accept_users() -> void:
	for u in _users:                                         # (the first changed entry, as the client)
		if bool(u.remove):
			_send("HouseUserSet", [_options_house, int(u.slot), 100])
			break
		if int(u.level) != int(u.start):
			_send("HouseUserSet", [_options_house, int(u.slot), int(u.level)])
			break
	_send("HouseUsersRequest", [_options_house])


# ---------------------------------------------------------------- Rotate Object

var _rotate_index := -1


## Shift + right-click on a placed decoration asked to turn it; the server's 0x18 says yes.
func _open_rotate(number: int, index: int) -> void:
	_options_house = number
	_rotate_index = index
	var w := _window("rotate", "leftbot", "Rotate Object", Vector2(100, 100))
	_clear(w)
	for b in [[10, 12, 270], [10, 46, 345], [11, 66, 15], [11, 84, 90]]:
		var angle := int(b[2])
		w.add_child(sprite_button("buttons", int(b[0]), Vector2(float(b[1]), 32), Vector2(16, 16), func():
			_send("HouseRotate", [_options_house, _rotate_index, angle])))
	w.add_child(sprite_button("buttons", 0x13, Vector2(35, 56), Vector2(58, 16), func(): w.hide_window()))
	w.show_window()


# ---------------------------------------------------------------- Money Selector (rent)

## The 1.64 help: "click out the number of coins you need and then drop them on to the outside of the house" - a
## click on a coin amount in the inventory opens the Money Selector (game.dll UI 0x27, minipet.bmp 128 x 128): that
## coin, how many (the arrows: 1, Shift 10), and the coins themselves to drag onto your house (the server's
## HousePlaceItem with the money slot and the amount; it asks "Pay rent with ...?").
const COIN_SLOTS := {"platinum": 245, "gold": 246, "silver": 247, "copper": 248}
var _money_coin := "gold"
var _money_amount := 1


static func open_money(coin: String) -> void:
	if current and is_instance_valid(current) and COIN_SLOTS.has(coin):
		current._open_money(coin)


func _open_money(coin: String) -> void:
	_money_coin = coin
	_money_amount = clampi(_money_amount, 1, maxi(1, mini(255, int(PlayerData.money.get(coin, 0)))))
	var w := _window("money", "minipet", "Money Selector", Vector2(400, 300))
	_clear(w)
	_label(w, coin.to_upper(), Vector2(0, 24), UiArt.CREAM, 9, w.size.x, HORIZONTAL_ALIGNMENT_CENTER)
	var amount := _label(w, "", Vector2(24, 46), UiArt.GOLD_HI, 10, 80.0, HORIZONTAL_ALIGNMENT_CENTER)
	var show := func(): amount.text = str(_money_amount)
	for d in [-1, 1]:
		w.add_child(sprite_button("buttons", 10 if d < 0 else 11, Vector2(8 if d < 0 else 104, 44), Vector2(16, 16), func():
			var have := mini(255, int(PlayerData.money.get(_money_coin, 0)))
			var step := 10 if Input.is_key_pressed(KEY_SHIFT) else 1
			_money_amount = clampi(_money_amount + d * step, 1, maxi(1, have))
			show.call()))
	var coins := CoinDrag.new()
	coins.owner_windows = self
	coins.position = Vector2(14, 72)
	coins.size = Vector2(100, 18)
	w.add_child(coins)
	_label(w, "DRAG ONTO YOUR HOUSE", Vector2(0, 94), UiArt.DIM, 7, w.size.x, HORIZONTAL_ALIGNMENT_CENTER)
	w.add_child(sprite_button("buttons", 0x13, Vector2(35, 106), Vector2(58, 16), func(): w.hide_window()))
	show.call()
	w.show_window()


class CoinDrag extends Control:
	var owner_windows: Node

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		var l := UiArt.label("COINS", 9, UiArt.GOLD_HI)
		l.size = size
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		l.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(l)
		tooltip_text = "DRAG THE COINS ONTO THE OUTSIDE OF YOUR HOUSE TO PAY ITS RENT"

	func _get_drag_data(_p: Vector2) -> Variant:
		var coin: String = owner_windows._money_coin
		var n: int = owner_windows._money_amount
		var prev := UiArt.label("%d %s" % [n, coin.to_upper()], 9, UiArt.GOLD_HI)
		set_drag_preview(prev)
		return {"kind": "money", "slot": int(COIN_SLOTS[coin]), "amount": n}
