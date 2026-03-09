"""
reset_all.py
────────────────────────────────────────────────
完全清空所有實驗資料，重新開始。
執行方式：python reset_all.py

⚠️  執行前請確認 Flask 已停止（Ctrl+C）
⚠️  這個操作無法復原
"""

import os
import json
import glob
from pymongo import MongoClient
from config import Config

# ── 確認 ──────────────────────────────────────
print("⚠️  這將清空所有 MongoDB 資料和 FAISS index")
confirm = input("確定要重置嗎？輸入 yes 繼續：").strip().lower()
if confirm != "yes":
    print("取消。")
    exit()

# ── MongoDB ───────────────────────────────────
print("\n[1/3] 清空 MongoDB...")
client = MongoClient(Config.MONGO_URI)
db     = client[Config.DB_NAME]

collections = [
    "eval_logs",
    "observation_logs",
    "exp_checkpoints",
    "activity_sequences",
    "conversation_logs",
    "dynamic_objects",
    "scene_snapshots",
    "semantic_memories",
    "navigation_logs",
]

for col in collections:
    count = db[col].count_documents({})
    db[col].delete_many({})
    print(f"  ✓ {col}: 刪除 {count} 筆")

# ── FAISS ─────────────────────────────────────
print("\n[2/3] 清空 FAISS index...")
faiss_files = (
    glob.glob("*.index") +
    glob.glob("*.pkl") +
    glob.glob("*_meta.json") +
    glob.glob("faiss_*")
)

if faiss_files:
    for f in faiss_files:
        os.remove(f)
        print(f"  ✓ 刪除 {f}")
else:
    print("  ✓ 無 FAISS 檔案")

# ── debug_images ──────────────────────────────
print("\n[3/3] 清空 debug_images...")
if os.path.exists("debug_images"):
    files = os.listdir("debug_images")
    for f in files:
        os.remove(os.path.join("debug_images", f))
    print(f"  ✓ 刪除 {len(files)} 張圖片")
else:
    print("  ✓ 無 debug_images 資料夾")

# ── 驗收 ──────────────────────────────────────
print("\n── 驗收 ──────────────────────────────────")
for col in collections:
    n = db[col].count_documents({})
    status = "✅" if n == 0 else "❌ 還有資料！"
    print(f"  {status} {col}: {n} 筆")

print("\n✅ 重置完成！現在可以重新啟動 Flask：python app.py")
