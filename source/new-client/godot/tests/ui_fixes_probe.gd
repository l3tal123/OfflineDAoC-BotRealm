extends Node
## Offline shots of the 2026-11-02 player-report fixes: the windows at Alpha 40% with the right-click Alpha panel open
## over the chat box, and the Destroy Item question on a selected backpack item.
##   --skip-login --zone=N --world-mode=1.65 --ui-test --probe=ui_fixes_probe --out=<dir>


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var out := String(v.args.get("out", "user://"))
	var ui: GameUI = v.ui
	ui.show_sheet("inventory") if ui.has_method("show_sheet") else null
	ClassicWindow.set_interface_alpha(40.0, get_tree())
	var chat: ChatWindow = ui.chat
	chat._alpha_panel(chat.get_global_rect().get_center())
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/alpha40.png")
	for c in chat.get_parent().get_children():                # (close the panel: its click catcher)
		if c is Control and c.get_child_count() == 1 and c.get_child(0) is PanelContainer:
			c.queue_free()
	ClassicWindow.set_interface_alpha(100.0, get_tree())
	PlayerData.give_item("rawhide_roman_leather_gloves", "", true)
	var slot := -1
	for s in PlayerData.inventory:
		if PlayerData.is_backpack(int(s)):
			slot = int(s)
			break
	print("[uifix] backpack slot ", slot, " ", PlayerData.item_at(slot).get("name", ""))
	if slot >= 0:
		PlayerData.selected_slot = slot
		ui._destroy_selected()
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/destroy.png")
	print("[uifix] shots in ", out)
	get_tree().quit()
