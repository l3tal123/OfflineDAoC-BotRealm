"""Unpacks the clean BotRealm world database (database/opendaoc-165.sqlite3.zip) to runtime/data/opendaoc-165.sqlite3.db.
Refuses to overwrite an existing world (that is someone's progress) unless --force is given.
Usage (from the repository root): python database/unpack_db.py [--force]"""
import os
import sys
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
ZIP = os.path.join(ROOT, 'database', 'opendaoc-165.sqlite3.zip')
TARGET = os.path.join(ROOT, 'runtime', 'data', 'opendaoc-165.sqlite3.db')

if os.path.exists(TARGET) and '--force' not in sys.argv:
    sys.exit(f'{TARGET} already exists (a world with progress?). Use --force to replace it.')
os.makedirs(os.path.dirname(TARGET), exist_ok=True)
with zipfile.ZipFile(ZIP) as z:
    with z.open('opendaoc-165.sqlite3.db') as src, open(TARGET + '.tmp', 'wb') as dst:
        while chunk := src.read(1 << 20):
            dst.write(chunk)
os.replace(TARGET + '.tmp', TARGET)
print('unpacked', TARGET)
