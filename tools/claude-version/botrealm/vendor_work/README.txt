TRUE 1.65 VENDOR WORK (2026-10-31) - how the merchant stock content in runtime\true165\ was made

Sources: the period Allakhazam item pages and merchant pages (Wayback, <= Nov 2003; crawl complete), CapnBry positions.
Results (copied into ..\ and runtime\true165\): add-vendoritems165, add-deaditems165, add-goodsitems165 (items),
add-merchantstock165, add-merchantlists165, add-missingmerchantstock165, add-merchantgoods165 (stock),
add-missingmerchants165 (7 spawns), and the "1.65 period merchant ..." updates in true165-rules.json.

Rerun order (each script reads world165_snapshot.db = a read-only copy of the True 1.65 world taken BEFORE any of this
content; make one with sqlite3's backup API from runtime\data\opendaoc-165.sqlite3.db of a world without this content):
  extract_pages.py, merchant_parse.py, merchant_match.py, merchant_report_data.py, merchant_pass2.py,
  build_vendoritems165.py, build_deaditems165.py, make_vendor_content.py, make_missing_merchants.py,
  merchant_goods_pass.py, build_goodsitems165.py, make_goods_content.py
The scripts find the install folder from where they sit (no fixed PC path); they read and write this folder.
Reports: merchant_report.md, vendor_items_report.md, deaditems_report.md, bot_coin_report.md.
