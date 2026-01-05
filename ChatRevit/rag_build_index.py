# rag_build_index.py
# 依存: pip install chromadb
import os, json, glob, requests
import chromadb
from chromadb.config import Settings

LOG_DIR = "./logs"
COLLECTION_NAME = "revitmcp_rag"
OLLAMA_EMBED_URL = "http://localhost:11434/api/embed"
EMBED_MODEL = "nomic-embed-text"  # 例

def ollama_embed(texts):
    r = requests.post(OLLAMA_EMBED_URL, json={"model": EMBED_MODEL, "input": texts}, timeout=300)
    r.raise_for_status()
    data = r.json()
    return data["embeddings"]  # list[list[float]]

def iter_records():
    for path in sorted(glob.glob(os.path.join(LOG_DIR, "*_mcp.jsonl"))):
        with open(path, "r", encoding="utf-8") as f:
            for line in f:
                try:
                    yield json.loads(line)
                except:
                    continue

def record_to_doc(rec):
    # 重要情報をテキスト化（RAG向きに）
    method = rec.get("method","")
    path = rec.get("path","")
    req = rec.get("req", {})
    res = rec.get("res", {})
    status = rec.get("status", 0)
    msg = res.get("result", {}).get("msg") if isinstance(res.get("result"), dict) else None
    ok  = res.get("result", {}).get("ok") if isinstance(res.get("result"), dict) else None

    # 要約ぽく整形（編集）
    parts = []
    parts.append(f"[RPC] {method} {path} status={status} ok={ok}")
    if isinstance(req, dict):
        method_name = req.get("method")
        if method_name:
            parts.append(f"method: {method_name}")
        if "params" in req:
            parts.append("params:\n" + json.dumps(req["params"], ensure_ascii=False, indent=2))
    # 代表的なレスポンス抜粋
    if isinstance(res, dict):
        if "result" in res:
            try:
                # 大きすぎるとノイジーなので 800 文字で切る
                snippet = json.dumps(res["result"], ensure_ascii=False)[:800]
                parts.append("result:\n" + snippet)
            except:
                pass
        elif "error" in res:
            parts.append("error:\n" + json.dumps(res["error"], ensure_ascii=False))

    return "\n".join(parts)

def main():
    client = chromadb.Client(Settings(is_persistent=True, persist_directory="./chroma_db"))
    if COLLECTION_NAME in [c.name for c in client.list_collections()]:
        col = client.get_collection(COLLECTION_NAME)
        col.delete(where={})  # 全入れ替え
    else:
        col = client.create_collection(COLLECTION_NAME)

    docs, ids = [], []
    for i, rec in enumerate(iter_records()):
        doc = record_to_doc(rec)
        if not doc.strip(): 
            continue
        docs.append(doc)
        ids.append(f"rec-{i}")

        # バッチで小分けに投入（速度最適）
        if len(docs) >= 64:
            vecs = ollama_embed(docs)
            col.add(ids=ids, documents=docs, embeddings=vecs)
            docs, ids = [], []

    if docs:
        vecs = ollama_embed(docs)
        col.add(ids=ids, documents=docs, embeddings=vecs)

    print(f"Indexed into collection '{COLLECTION_NAME}'. Total records added.")

if __name__ == "__main__":
    main()
