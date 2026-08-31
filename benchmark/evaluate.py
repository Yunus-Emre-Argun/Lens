"""Faz 2: Bir modelin embed.py ciktilarini kullanarak Top-1/Top-3/Top-5
dogruluk oranlarini, varyasyon-turu kirilimini ve zamanlama ozetini hesaplar.

Ground truth: her varyasyon gorseli, uretildigi orijinal gorselin urunudur
(bkz. data/variations_manifest.json).

Kullanim:
    python evaluate.py --model clip
    python evaluate.py --model siglip
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path

import numpy as np

from common import Timer, cosine_similarity_matrix, load_json, save_json, top_k_indices

DATA_DIR = Path(__file__).parent / "data"
EMBEDDINGS_DIR = Path(__file__).parent / "results" / "embeddings"
RESULTS_DIR = Path(__file__).parent / "results"

TOP_KS = (1, 3, 5)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, choices=["clip", "siglip"])
    args = parser.parse_args()

    embeddings_path = EMBEDDINGS_DIR / f"{args.model}_embeddings.npz"
    timing_path = EMBEDDINGS_DIR / f"{args.model}_timing.json"
    manifest_path = DATA_DIR / "variations_manifest.json"

    if not embeddings_path.exists():
        raise SystemExit(f"'{embeddings_path}' yok. Once 'python embed.py --model {args.model}' calistirin.")
    if not manifest_path.exists():
        raise SystemExit(f"'{manifest_path}' yok. Once 'python make_variations.py' calistirin.")

    data = np.load(embeddings_path, allow_pickle=False)
    raw_names = list(data["raw_names"])
    raw_vectors = data["raw_vectors"]
    var_names = list(data["var_names"])
    var_vectors = data["var_vectors"]

    timing = load_json(timing_path)
    manifest = load_json(manifest_path)

    per_query_results = []
    hits = {k: 0 for k in TOP_KS}
    hits_by_type: dict[str, dict[int, int]] = defaultdict(lambda: {k: 0 for k in TOP_KS})
    counts_by_type: dict[str, int] = defaultdict(int)

    for i, query_name in enumerate(var_names):
        if query_name not in manifest:
            continue
        ground_truth = manifest[query_name]["original"]
        variation_type = manifest[query_name]["variation_type"]
        counts_by_type[variation_type] += 1

        query_vector = var_vectors[i : i + 1]
        with Timer() as t:
            sims = cosine_similarity_matrix(query_vector, raw_vectors)[0]
            ranked_idx = top_k_indices(sims, max(TOP_KS))
        sim_sort_time = t.elapsed

        ranked_names = [raw_names[j] for j in ranked_idx]
        embed_time = timing["variation_embed_times_seconds"].get(query_name)
        total_time = (embed_time or 0.0) + sim_sort_time

        result = {
            "query": query_name,
            "ground_truth": ground_truth,
            "variation_type": variation_type,
            "ranked_top5": ranked_names,
            "embed_time_seconds": embed_time,
            "similarity_sort_time_seconds": sim_sort_time,
            "total_query_time_seconds": total_time,
        }
        for k in TOP_KS:
            is_hit = ground_truth in ranked_names[:k]
            result[f"top{k}_hit"] = is_hit
            if is_hit:
                hits[k] += 1
                hits_by_type[variation_type][k] += 1
        per_query_results.append(result)

    total_queries = len(per_query_results)
    embed_times = [r["embed_time_seconds"] for r in per_query_results if r["embed_time_seconds"] is not None]
    sim_times = [r["similarity_sort_time_seconds"] for r in per_query_results]
    total_times = [r["total_query_time_seconds"] for r in per_query_results]

    summary = {
        "model_key": args.model,
        "hf_id": timing["hf_id"],
        "model_load_time_seconds": timing["load_time_seconds"],
        "total_queries": total_queries,
        "accuracy": {
            f"top{k}": (hits[k] / total_queries if total_queries else None) for k in TOP_KS
        },
        "accuracy_by_variation_type": {
            vt: {f"top{k}": hits_by_type[vt][k] / counts_by_type[vt] for k in TOP_KS}
            for vt in counts_by_type
        },
        "mean_embed_time_seconds": float(np.mean(embed_times)) if embed_times else None,
        "mean_similarity_sort_time_seconds": float(np.mean(sim_times)) if sim_times else None,
        "mean_total_query_time_seconds": float(np.mean(total_times)) if total_times else None,
    }

    save_json(
        {"summary": summary, "per_query": per_query_results},
        RESULTS_DIR / f"{args.model}_results.json",
    )

    if total_queries:
        acc = summary["accuracy"]
        print(
            f"[{args.model}] Top-1={acc['top1']:.2f} Top-3={acc['top3']:.2f} Top-5={acc['top5']:.2f} "
            f"({total_queries} sorgu)"
        )
    else:
        print(f"[{args.model}] Eslesen sorgu bulunamadi.")
    print(f"Sonuclar: {RESULTS_DIR / f'{args.model}_results.json'}")


if __name__ == "__main__":
    main()
