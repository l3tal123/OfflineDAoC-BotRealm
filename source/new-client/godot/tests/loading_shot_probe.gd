extends Node
## Offline: a loading screen over the world, as zone loads show it - --region=N (the 1.64 pictures of that region in
## True 1.65 Mode), --name=text, --progress=0..1 - and a shot of it.
##   --skip-login --zone=200 --world-mode=1.65 --probe=loading_shot_probe --region=200 --name="Lough Derg" --out=<dir>
var v: Node


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(4.0).timeout
	var ls := LoadingScreen.new(String(v.args.get("name", "Lough Derg")), int(v.args.get("region", "200")))
	get_tree().root.add_child(ls)
	ls.set_progress(float(v.args.get("progress", "0.6")))
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [String(v.args.get("out", "user://")),
			String(v.args.get("tag", "loading"))])
	print("[loading] shot")
	get_tree().quit()
