import sys, os, hashlib
sys.dont_write_bytecode = True
sys.path.insert(0, r"D:\000_Logiciel\DeezerMusic")
from deekeep.db import sqlite_store
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
store = sqlite_store.SqliteStore({"root": out})
data = bytes((i * 7 + 3) % 256 for i in range(300000))
sha = hashlib.sha1(data).hexdigest()
key = "deekeep/audio/source/%s/%s.mp3" % (sha[:2], sha)
store.put(key, data)
store.close()
print(store.path, key, sha, len(data))
