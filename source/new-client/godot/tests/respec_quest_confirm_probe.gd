extends Node
## Offline shots (owner 2026-11-02): the trainer window's RESPEC button beside TRAIN, and REMOVE QUEST's YES / NO
## question in the quest journal.
##   --skip-login --zone=N --world-mode=1.65 --ui-test --probe=respec_quest_confirm_probe --out=<dir>


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var out := String(v.args.get("out", "user://"))
	var ui = v.ui
	Server.active = true
	Server.trainer_specs = [{"index": 0, "name": "Sword", "level": 20}, {"index": 1, "name": "Parry", "level": 12},
			{"index": 2, "name": "Shields", "level": 15}]
	ui.trainer.open()
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/trainer_respec.png")
	print("[respec] button visible ", ui.trainer._respec.visible, " at ", ui.trainer._respec.global_position)
	ui.trainer.hide()
	Server.active = false
	ui.journal.show_window()
	await get_tree().create_timer(0.5).timeout
	ui.journal.selected = "test"
	ui.journal._remove.disabled = false
	ui.journal._remove.pressed.emit()
	await get_tree().create_timer(0.8).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(out + "/quest_remove_confirm.png")
	print("[respec] journal confirm shot")
	get_tree().quit()
