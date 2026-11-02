# v0.2 baseline

`package.sha256` is the SHA-256 of every file a v0.2 install has, exactly as the release shipped it (made by
`release/make_release_true165.py`). Compare an install with it to find any file that is missing or was changed on that PC.
The update patch is built against every released baseline (`release/make_patch_true165.py --base`).

Never edit this file.
