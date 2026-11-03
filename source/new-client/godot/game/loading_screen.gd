class_name LoadingScreen
extends CanvasLayer
## Loading screen (owner 2026-10-09): the original Dark Age of Camelot box art as the full-screen background, "ENTERING BOTREALM", the client's bronze splash bar (data/splashloadbar1 empty, splashloadbar2 full) filling as the zone
## builds, and one of the client's own loading tips (data/loading/tips.dat, 1.65-appropriate ones) under the bar,
## changing every few seconds on long loads. Fades out when the world is ready.

const UI := "res://content/ui"
const GOLD := Color(0.86, 0.71, 0.38)

var _bar: TextureProgressBar
var _root: Control
var _tip: Label
var _tips: Array = []
var _tip_time := 0.0
# True 1.65 Mode: the 1.64 client's loading screen (owner 2026-11-01: "use the classic loading bar") - read from the
# 1.64 game.dll by the original-client research: the picture centred at its own size (800 x 600), the bar (LOADBAR1
# empty, LOADBAR2 full, 82 x 19) 10 px in from its bottom right corner, filled by revealing LOADBAR2 left to right, and
# one line "Loading..." (1.64 builds "Loading %s..." from a name buffer it never fills) in fonts/stone20 row 0, ending
# 20 px left of the bar, top aligned with it. No title, no tips. Scaled with the picture to the window.
const PIC_W := 800.0
const PIC_H := 600.0
const BAR := Vector2(82, 19)
var _classic := false
var _art: TextureRect
var _line: StoneText


## True 1.65 Mode: the real 1.64 loading pictures of the region (pipeline/build_loading_165.py from the
## original client's loading165.dat), full height at their own 4:3, as the original client shows them there;
## a region without 1.64 pictures keeps the box art. "" when there is none.
static func picture_165(region: int) -> String:
	if Server.world_mode != "1.65" or region < 0:
		return ""
	var t = JSON.parse_string(FileAccess.get_file_as_string(UI + "/loading165/loading165.json")) 			if FileAccess.file_exists(UI + "/loading165/loading165.json") else null
	var pics: Array = t.get("regions", {}).get(str(region), []) if t is Dictionary else []
	return String(pics[randi() % pics.size()]) if pics.size() > 0 else ""


func _init(zone_name: String, region := -1) -> void:
	layer = 100
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_root)
	var black := ColorRect.new()
	black.color = Color.BLACK
	black.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.add_child(black)
	# the box art as the one full-screen background (owner: not shown twice), with a dark band at the bottom so the
	# zone name, bar and tip stay readable
	var art := TextureRect.new()
	var pic := picture_165(region)
	var pic_tex: Texture2D = null
	if pic != "":
		var img := Image.load_from_file(ProjectSettings.globalize_path("%s/loading165/%s.png" % [UI, pic]))
		if img:
			img.generate_mipmaps()
			pic_tex = ImageTexture.create_from_image(img)
	art.texture = pic_tex if pic_tex else _tex("loading_box_art")
	art.set_anchors_preset(Control.PRESET_FULL_RECT)
	art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED if pic_tex else TextureRect.STRETCH_KEEP_ASPECT_COVERED
	_root.add_child(art)
	_art = art
	if Server.world_mode == "1.65" and _classic_bar(zone_name):
		return
	var shade := TextureRect.new()
	var grad := Gradient.new()
	grad.set_color(0, Color(0, 0, 0, 0))
	grad.set_color(1, Color(0, 0, 0, 0.85))
	var gt := GradientTexture2D.new()
	gt.gradient = grad
	gt.fill_from = Vector2(0, 0)
	gt.fill_to = Vector2(0, 1)
	shade.texture = gt
	shade.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	shade.offset_top = -260
	shade.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	shade.stretch_mode = TextureRect.STRETCH_SCALE
	_root.add_child(shade)
	var label := Label.new()
	label.text = "ENTERING BOTREALM"                 # owner 2026-10-09 (not the zone name)
	var font := FontFile.new()
	font.load_dynamic_font(ProjectSettings.globalize_path(UI + "/fonts/minionpro-bold.ttf"))
	label.add_theme_font_override("font", font)
	label.add_theme_font_size_override("font_size", 28)
	label.add_theme_color_override("font_color", GOLD)
	label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.9))
	label.add_theme_constant_override("shadow_offset_x", 2)
	label.add_theme_constant_override("shadow_offset_y", 2)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	label.offset_top = -170
	label.offset_bottom = -130
	_root.add_child(label)
	_bar = TextureProgressBar.new()
	_bar.texture_under = _tex("splashloadbar1")
	_bar.texture_progress = _tex("splashloadbar2")
	_bar.nine_patch_stretch = true
	_bar.max_value = 1.0
	_bar.step = 0.0
	_bar.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	_bar.offset_left = -392
	_bar.offset_right = 392
	_bar.offset_top = -128
	_bar.offset_bottom = -86
	_root.add_child(_bar)
	var tips = JSON.parse_string(FileAccess.get_file_as_string(UI + "/loading_tips.json"))
	_tips = tips.get("tips", []) if tips is Dictionary else []
	_tip = Label.new()
	var tip_font := FontFile.new()
	tip_font.load_dynamic_font(ProjectSettings.globalize_path(UI + "/fonts/myriadpro-semibold.ttf"))
	_tip.add_theme_font_override("font", tip_font)
	_tip.add_theme_font_size_override("font_size", 22)
	_tip.add_theme_color_override("font_color", Color(0.93, 0.89, 0.78))
	_tip.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.95))
	_tip.add_theme_constant_override("shadow_offset_x", 2)
	_tip.add_theme_constant_override("shadow_offset_y", 2)
	_tip.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_tip.autowrap_mode = TextServer.AUTOWRAP_WORD
	_tip.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_tip.offset_left = 160
	_tip.offset_right = -160
	_tip.offset_top = -78
	_tip.offset_bottom = -10
	_root.add_child(_tip)
	_next_tip()


## The 1.64 bar and its line (True 1.65 Mode); false when the bar's pictures aren't built (build_loadbar_164.py).
func _classic_bar(zone_name: String) -> bool:
	var empty := _bar_tex("loadbar1")
	var full := _bar_tex("loadbar2")
	if empty == null or full == null:
		return false
	_classic = true
	_art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED      # (the picture at its own 4:3, as the 1.64 client)
	_bar = TextureProgressBar.new()
	_bar.texture_under = empty
	_bar.texture_progress = full
	_bar.fill_mode = TextureProgressBar.FILL_LEFT_TO_RIGHT           # (revealed, never stretched)
	_bar.max_value = 1.0
	_bar.step = 0.0
	_bar.size = empty.get_size()
	_bar.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
	_root.add_child(_bar)
	_line = StoneText.make("Loading...")
	if _line:
		_root.add_child(_line)
	_place_classic()
	return true


func _bar_tex(name: String) -> Texture2D:
	for set_name in ["x4plus", "original"]:
		var img := Image.load_from_file(ProjectSettings.globalize_path("%s/loading165/bar/%s/%s.png" % [UI, set_name, name]))
		if img:
			return ImageTexture.create_from_image(img)
	return null


## Where the 800 x 600 picture is drawn in the window, and the bar and line on it, as the 1.64 client placed them.
func _place_classic() -> void:
	var vp := _root.get_viewport_rect().size if _root.is_inside_tree() else Vector2(PIC_W, PIC_H)
	var k := minf(vp.x / PIC_W, vp.y / PIC_H)
	var origin := (vp - Vector2(PIC_W, PIC_H) * k) * 0.5
	var bar_at := origin + Vector2(PIC_W - BAR.x - 10.0, PIC_H - BAR.y - 10.0) * k
	_bar.position = bar_at
	_bar.scale = Vector2.ONE * (BAR.x * k / _bar.size.x)
	if _line:
		_line.scale = Vector2.ONE * k
		_line.position = Vector2(bar_at.x - (20.0 + _line.width) * k, bar_at.y)


## A line in the 1.64 client's stone bitmap font (fonts/stone20.bmp row 0, pipeline/build_loadbar_164.py): each glyph
## its own run of columns, no letter spacing; drawn at the font's own 20 px (scaled by its node).
class StoneText extends Control:
	var text := ""
	var width := 0.0
	var _tex: Texture2D
	var _cells := {}
	var _k := 1.0                                       # the texture's size over the font's own (x4plus: 4)

	static func make(t: String) -> StoneText:
		var dir := "res://content/ui/loading165/bar"
		var info = JSON.parse_string(FileAccess.get_file_as_string(dir + "/stone20.json")) \
				if FileAccess.file_exists(dir + "/stone20.json") else null
		if not info is Dictionary:
			return null
		var st := StoneText.new()
		for set_name in ["x4plus", "original"]:
			var img := Image.load_from_file(ProjectSettings.globalize_path("%s/%s/stone20.png" % [dir, set_name]))
			if img:
				st._tex = ImageTexture.create_from_image(img)
				st._k = float(img.get_height()) / float(info.get("height", 20))
				break
		if st._tex == null:
			return null
		st._cells = info.cells
		st.text = t
		for c in t:
			var cell = st._cells.get(c, st._cells.get("*"))
			st.width += float(cell[1]) - float(cell[0])
		st.size = Vector2(st.width, float(info.get("height", 20)))
		st.mouse_filter = Control.MOUSE_FILTER_IGNORE
		st.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
		return st

	func _draw() -> void:
		var x := 0.0
		var h := size.y
		for c in text:
			var cell = _cells.get(c, _cells.get("*"))
			var w := float(cell[1]) - float(cell[0])
			if c != " ":
				draw_texture_rect_region(_tex, Rect2(x, 0, w, h), Rect2(float(cell[0]) * _k, 0, w * _k, h * _k))
			x += w


func _tex(name: String) -> Texture2D:
	var img := Image.load_from_file(ProjectSettings.globalize_path("%s/x4plus/%s.png" % [UI, name]))
	if img == null:
		img = Image.load_from_file(ProjectSettings.globalize_path("%s/original/%s.png" % [UI, name]))
	if img == null:
		return null
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


func _next_tip() -> void:
	if not _tips.is_empty():
		_tip.text = "TIP: " + String(_tips[randi() % _tips.size()])


func _process(delta: float) -> void:
	if _classic:
		_place_classic()                               # (the window may be resized while loading)
		return
	_tip_time += delta
	if _tip_time > 8.0:
		_tip_time = 0.0
		_next_tip()


func set_progress(v: float) -> void:
	_bar.value = clampf(v, 0.0, 1.0)


func finish() -> void:
	set_progress(1.0)
	var tw := create_tween()
	tw.tween_interval(0.25)
	tw.tween_property(_root, "modulate:a", 0.0, 0.6)
	tw.tween_callback(queue_free)
