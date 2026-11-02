class_name ClassicWindow
extends Control
## A window of the 1.65 interface: one of the client's frame bitmaps as its background, sized in the client's own
## pixels (the whole UI is laid out in 800x600-era units and scaled by GameUI). The client's frames carry a move
## handle in the top-left corner and a close button in the top-right corner: drag the handle (or the top band where
## nothing else sits) to move, click the cross to close. Resizable windows (chat) drag their top or right edge.
## Positions (and sizes) are saved per window and per screen shape, as the client kept a [Panels-800x600] section
## per resolution: user://settings.cfg [ui_layout_<w>x<h>] in layout units.

signal closed
signal resized_by_user

const HANDLE := Rect2(0, 0, 13, 13)          # the four-arrow move handle painted in the frames' top-left corner
const EDGE := 4.0

var window_id := ""
var movable := true
var closable := true
var resizable := false
var min_size := Vector2(160, 64)
var max_size := Vector2(900, 500)
var frame: Control:                          # the frame bitmap (TextureRect, or a nine-patch when resizable)
	set(v):
		frame = v
		if v:
			v.modulate.a = interface_alpha()
## The interface's Alpha (the 1.64 client's Options "Alpha: N%", saved as [Panels] Alpha): how solid every window's
## frame / background is - its text, icons and buttons stay solid (a player report: chat transparency).
static var _alpha := -1.0
var frame_name := ""
var patch := [8, 8, 8, 8]                    # resizable frames: border sizes left / top / right / bottom (art px)
var _drag := false
var _resize := ""                            # "", "top", "right", "topright"
var _drag_from := Vector2.ZERO
var _start_rect := Rect2()


func setup(id: String, frame_bitmap: String, frame_rect := Rect2(), pos := Vector2.ZERO) -> ClassicWindow:
	window_id = id
	name = id
	position = pos
	add_to_group("classic_windows")
	mouse_filter = Control.MOUSE_FILTER_STOP
	set_frame(frame_bitmap, frame_rect)
	return self


static func interface_alpha() -> float:
	if _alpha < 0.0:
		var cfg := ConfigFile.new()
		cfg.load(UiArt.settings_path)
		_alpha = clampf(float(cfg.get_value("interface", "alpha", 100)) / 100.0, 0.1, 1.0)
	return _alpha


## A new Alpha (percent) on every open window's frame and its other backing art.
static func set_interface_alpha(percent: float, tree: SceneTree) -> void:
	_alpha = clampf(percent / 100.0, 0.1, 1.0)
	for w in tree.get_nodes_in_group("classic_windows"):
		var cw := w as ClassicWindow
		for f in [cw.frame] + cw.backing:
			if f and is_instance_valid(f):
				(f as Control).modulate.a = _alpha


## Art inside the window that is its background too (the character panel's sheet bitmap and tab strip): it fades
## with the frame at the interface's Alpha.
var backing: Array = []


func add_backing(c: Control) -> void:
	backing.append(c)
	c.modulate.a = interface_alpha()


## The right-click Alpha panel: "ALPHA: N%" between two arrows (10% a click), the same setting as Options' ALPHA;
## a click anywhere else closes it.
func _alpha_panel(at: Vector2) -> void:
	var host := get_parent() as Control
	if host == null:
		return
	var catcher := Control.new()                          # (catches the click that closes it)
	catcher.set_anchors_preset(Control.PRESET_FULL_RECT)
	catcher.mouse_filter = Control.MOUSE_FILTER_STOP
	host.add_child(catcher)
	var box := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.06, 0.05, 0.04, 0.95)
	style.border_color = UiArt.GOLD.darkened(0.3)
	style.set_border_width_all(1)
	style.set_content_margin_all(3)
	box.add_theme_stylebox_override("panel", style)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	box.add_child(row)
	var label := UiArt.label("", 9, UiArt.CREAM)
	label.custom_minimum_size = Vector2(70, 16)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var show := func(): label.text = "ALPHA: %d%%" % roundi(interface_alpha() * 100.0)
	for step in [-10, 10]:
		var b := UiArt.button("<" if step < 0 else ">", Vector2(16, 16), 9)
		b.pressed.connect(func():
			var pct := clampf(roundf(interface_alpha() * 10.0) * 10.0 + step, 10.0, 100.0)
			var cfg := ConfigFile.new()
			cfg.load(UiArt.settings_path)
			cfg.set_value("interface", "alpha", pct)
			cfg.save(UiArt.settings_path)
			set_interface_alpha(pct, get_tree())
			show.call())
		row.add_child(b)
		if step < 0:
			row.add_child(label)
	show.call()
	catcher.add_child(box)
	box.position = (at - host.get_global_rect().position) / host.get_global_transform().get_scale() - Vector2(8, 8)
	catcher.gui_input.connect(func(e: InputEvent):
		if e is InputEventMouseButton and e.pressed:
			catcher.queue_free())


## Swap the frame bitmap (the quickbar turning vertical / horizontal); the window takes the bitmap's size.
func set_frame(frame_bitmap: String, frame_rect := Rect2()) -> void:
	frame_name = frame_bitmap
	if frame:
		frame.queue_free()
	var bg := UiArt.picture(frame_bitmap, frame_rect)
	if resizable:
		# a nine-patch at the art's x4 resolution inside a 1/4-scale holder, so the 8 px border stays 8 layout units
		var keep := size if size != Vector2.ZERO else bg.size
		var holder := Control.new()
		holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
		holder.scale = Vector2(0.25, 0.25)
		var np := NinePatchRect.new()
		np.texture = UiArt.tex(frame_bitmap)
		if frame_rect.size != Vector2.ZERO:
			np.region_rect = Rect2(frame_rect.position * UiArt.K, frame_rect.size * UiArt.K)
		var sides := [SIDE_LEFT, SIDE_TOP, SIDE_RIGHT, SIDE_BOTTOM]
		for i in 4:
			np.set_patch_margin(sides[i], int(patch[i] * UiArt.K))
		np.mouse_filter = Control.MOUSE_FILTER_IGNORE
		holder.add_child(np)
		bg.free()
		frame = holder
		add_child(frame)
		move_child(frame, 0)
		size = keep
		_fit_frame()
	else:
		size = bg.size
		frame = bg
		add_child(frame)
		move_child(frame, 0)


func _fit_frame() -> void:
	if resizable and frame and frame.get_child_count() > 0:
		frame.size = size * 4.0
		(frame.get_child(0) as Control).size = size * 4.0


func set_window_size(s: Vector2) -> void:
	size = s.clamp(min_size, max_size)
	_fit_frame()
	if is_inside_tree():
		_clamp_to_screen()
	resized_by_user.emit()


func _edge_at(p: Vector2) -> String:
	if not resizable or HANDLE.has_point(p):
		return ""
	var top := p.y < EDGE
	var right := p.x > size.x - EDGE
	if top and right:
		return "topright"
	return "top" if top else ("right" if right else "")


func _gui_input(event: InputEvent) -> void:
	# the 1.64 help: "You can also adjust the transparency of any interface window by right-clicking on it with the
	# mouse. A panel will be displayed with two arrows that allow you to adjust the alpha" (a player report: chat alpha)
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT \
			and not event.shift_pressed and not event.ctrl_pressed:
		_alpha_panel(get_global_mouse_position())
		accept_event()
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			var p: Vector2 = event.position
			if closable and p.x > size.x - 16 and p.y < 14:
				hide_window()
				accept_event()
				return
			var edge := _edge_at(p)
			if edge != "":
				_resize = edge
				_drag_from = get_global_mouse_position()
				_start_rect = Rect2(position, size)
				move_to_front()
				accept_event()
				return
			if movable and (HANDLE.has_point(p) or p.y < 14):
				_drag = true
				_drag_from = p
				move_to_front()
				accept_event()
		elif _drag or _resize != "":
			_drag = false
			_resize = ""
			save_position()
	elif event is InputEventMouseMotion:
		if _drag:
			position += event.position - _drag_from
			_clamp_to_screen()
			accept_event()
		elif _resize != "":
			var d := (get_global_mouse_position() - _drag_from) / get_global_transform().get_scale()
			var r := _start_rect
			var w := r.size.x + (d.x if _resize in ["right", "topright"] else 0.0)
			var h := r.size.y - (d.y if _resize in ["top", "topright"] else 0.0)
			var ns := Vector2(w, h).clamp(min_size, max_size)
			position.y = r.position.y + r.size.y - ns.y            # the top edge moves, the bottom stays put
			set_window_size(ns)
			accept_event()
		else:
			var edge := _edge_at(event.position)
			mouse_default_cursor_shape = {"top": Control.CURSOR_VSIZE, "right": Control.CURSOR_HSIZE,
					"topright": Control.CURSOR_BDIAGSIZE}.get(edge, Control.CURSOR_ARROW)


func _clamp_to_screen() -> void:
	var area := get_parent_area_size()
	position = position.clamp(Vector2(-size.x + 24, 0), area - Vector2(24, 16))
	if resizable:                                        # a resized window keeps its bottom edge on the screen
		position.y = minf(position.y, maxf(0.0, area.y - size.y))


func show_window() -> void:
	visible = true
	move_to_front()
	save_position()


func hide_window() -> void:
	visible = false
	save_position()
	closed.emit()


func toggle_window() -> void:
	if visible:
		hide_window()
	else:
		show_window()


func _section() -> String:
	var a := get_parent_area_size()
	return "ui_layout_%dx%d" % [roundi(a.x), roundi(a.y)]


func save_position() -> void:
	if window_id == "" or not is_inside_tree():
		return
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	cfg.set_value(_section(), window_id, [position.x, position.y, visible, size.x, size.y])
	cfg.set_value("ui_layout", window_id, [position.x, position.y, visible, size.x, size.y])   # game-wide fallback
	cfg.save(UiArt.settings_path)


## Restore a saved position / size / visibility; otherwise keep the default.
func restore(default_visible: bool) -> void:
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	var v = cfg.get_value(_section(), window_id) if cfg.has_section_key(_section(), window_id) else null
	if v == null and cfg.has_section_key("ui_layout", window_id):
		v = cfg.get_value("ui_layout", window_id)              # another window shape: the last layout, kept on screen
	if v is Array and v.size() >= 3:
		position = Vector2(v[0], v[1])
		visible = bool(v[2])
		if resizable and v.size() >= 5:
			set_window_size(Vector2(v[3], v[4]))
	else:
		visible = default_visible
	_clamp_to_screen.call_deferred()
