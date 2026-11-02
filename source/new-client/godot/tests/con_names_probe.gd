extends Node
## Offline: item names in their con colour while highlighted (owner 2026-11-02) - a level 5 character, backpack items
## of levels 1 / 3 / 5 / 6 / 7 / 9 / 20, each selected in turn (one shot each of the backpack), plus the colours as text.
##   --skip-login --zone=N --world-mode=1.65 --ui-test --probe=con_names_probe --out=<dir>


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var out := String(v.args.get("out", "user://"))
	var ui: GameUI = v.ui
	PlayerData.level = 5
	var base: Dictionary = ItemDb.item("rawhide_roman_leather_gloves")
	var slots := []
	for lv in [1, 3, 5, 6, 7, 9, 20]:
		var id := "probe_con_%d" % lv
		var it := base.duplicate()
		it["id"] = id
		it["name"] = "level %d gloves" % lv
		it["level"] = lv
		ItemDb.add_runtime(id, it)
		PlayerData.give_item(id, "", true)
	for s in PlayerData.inventory:
		if PlayerData.is_backpack(int(s)) and String(PlayerData.entry(int(s)).id).begins_with("probe_con_"):
			slots.append(int(s))
	slots.sort()
	v.ui.show_sheet("inventory")
	await get_tree().create_timer(0.5).timeout
	for s in slots:
		var it := PlayerData.item_at(s)
		print("[con] level %d -> %s" % [int(it.level), ItemTooltip.name_color(it, true).to_html(false)])
	for i in [0, 2, 4, 6]:
		PlayerData.select_slot(slots[i])
		await get_tree().create_timer(0.5).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/con_sel%d.png" % [out, i])
		var t := v.ui.windows.info.get_node("Title") as Label
		print("[con] right-click box: %s in %s" % [t.text, t.get_theme_color("font_color").to_html(false)])
	print("[con] shots in ", out)
	get_tree().quit()
