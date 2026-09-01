"""Faz 3C: genisletilmis veri seti stres testi - GECICI Python capraz-dogrulama.

ONEMLI: Bu script C#/.NET Lens.Core pipeline'inin YERINE GECMEZ. Mevcut
makinede Smart App Control, bu oturumda yeni/degismis .NET derlemelerinin
calistirilmasini engelledigi icin (bkz. proje notlari), ayni CLIP modeli ve
ayni pipeline mantigiyla (embedding -> cosine similarity -> Top-K) bu olcumu
Python tarafinda tekrarliyoruz. C# stress-test kodu (src/Lens.AiProof,
"stresstest" modu) SILINMEDI/DEGISTIRILMEDI; yonetici makinesinde ayrica
calistirilip capraz dogrulanacak.

Kullanim:
    python expanded_stress_test.py
"""
from __future__ import annotations

import json
import time
from collections import Counter
from pathlib import Path

import numpy as np

from common import list_images, save_json
from models import embed_image, load_model

DATA_DIR = Path(__file__).parent / "data"
RAW_DIR = DATA_DIR / "raw"
DISTRACTORS_DIR = DATA_DIR / "distractors"
VARIATIONS_DIR = DATA_DIR / "variations"
MANIFEST_PATH = DATA_DIR / "variations_manifest.json"
RESULTS_DIR = Path(__file__).parent / "results"
REPORT_PATH = RESULTS_DIR / "expanded_stress_test.md"
RAW_JSON_PATH = RESULTS_DIR / "expanded_stress_test_raw.json"

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


def list_distractor_images() -> list[Path]:
    # DISTRACTORS_DIR icinde dogrudan duran gorseller (rejected/ alt klasoru
    # ve sources.csv haric) = kullanicinin son manuel dataset'i.
    return sorted(
        p for p in DISTRACTORS_DIR.iterdir()
        if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS
    )


def embed_gallery(loaded, paths: list[Path]) -> tuple[list[str], np.ndarray, list[float]]:
    names, vectors, times = [], [], []
    for p in paths:
        vec, elapsed = embed_image(loaded, p)
        names.append(p.name)
        vectors.append(vec)
        times.append(elapsed)
    matrix = np.vstack(vectors) if vectors else np.empty((0, 0), dtype=np.float32)
    return names, matrix, times


def rank_of(query_vec: np.ndarray, gallery_matrix: np.ndarray, gallery_names: list[str], ground_truth: str) -> tuple[int, list[tuple[str, float]]]:
    sims = gallery_matrix @ query_vec
    order = np.argsort(-sims)
    ranked_names = [gallery_names[i] for i in order]
    rank = ranked_names.index(ground_truth) + 1 if ground_truth in ranked_names else -1
    top5 = [(gallery_names[i], float(sims[i])) for i in order[:5]]
    return rank, top5


def main() -> None:
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))

    print("[1] Model yukleniyor (CLIP)...")
    t0 = time.perf_counter()
    loaded = load_model("clip")
    print(f"    Model yuklendi: {time.perf_counter() - t0:.2f} sn")

    raw_paths = list_images(RAW_DIR)
    distractor_paths = list_distractor_images()
    print(f"[2] Ground truth (raw): {len(raw_paths)} gorsel")
    print(f"[3] Distractor: {len(distractor_paths)} gorsel (kullanicinin son manuel seciminden)")

    t0 = time.perf_counter()
    raw_names, raw_matrix, raw_times = embed_gallery(loaded, raw_paths)
    raw_embed_elapsed = time.perf_counter() - t0
    print(f"    Raw embed: {raw_embed_elapsed:.2f} sn ({raw_embed_elapsed/len(raw_paths)*1000:.0f} ms/gorsel)")

    t0 = time.perf_counter()
    distractor_names, distractor_matrix, distractor_times = embed_gallery(loaded, distractor_paths)
    distractor_embed_elapsed = time.perf_counter() - t0
    avg_distractor_ms = (distractor_embed_elapsed / len(distractor_paths) * 1000) if distractor_paths else 0
    print(f"    Distractor embed: {distractor_embed_elapsed:.2f} sn ({avg_distractor_ms:.0f} ms/gorsel)")

    baseline_names = raw_names
    baseline_matrix = raw_matrix
    full_names = raw_names + distractor_names
    full_matrix = np.vstack([raw_matrix, distractor_matrix]) if len(distractor_names) else raw_matrix
    print(f"[4] Havuzlar hazir: baseline={len(baseline_names)}, full={len(full_names)}\n")

    variation_files = sorted(
        p for p in VARIATIONS_DIR.iterdir()
        if p.is_file() and p.name in manifest
    )
    print(f"[5] {len(variation_files)} query test edilecek\n")

    baseline_results = []
    full_results = []
    for qp in variation_files:
        info = manifest[qp.name]
        ground_truth = info["original"]
        variation_type = info["variation_type"]

        t0 = time.perf_counter()
        query_vec, _ = embed_image(loaded, qp)
        embed_ms = (time.perf_counter() - t0) * 1000

        t0 = time.perf_counter()
        rank_b, top5_b = rank_of(query_vec, baseline_matrix, baseline_names, ground_truth)
        sort_ms_b = (time.perf_counter() - t0) * 1000
        baseline_results.append({
            "query": qp.name, "variation_type": variation_type, "ground_truth": ground_truth,
            "rank": rank_b, "top5": top5_b, "embed_ms": embed_ms, "sort_ms": sort_ms_b,
        })

        t0 = time.perf_counter()
        rank_f, top5_f = rank_of(query_vec, full_matrix, full_names, ground_truth)
        sort_ms_f = (time.perf_counter() - t0) * 1000
        full_results.append({
            "query": qp.name, "variation_type": variation_type, "ground_truth": ground_truth,
            "rank": rank_f, "top5": top5_f, "embed_ms": embed_ms, "sort_ms": sort_ms_f,
            "pool_size": len(full_names),
        })

    save_json({"baseline": baseline_results, "full": full_results}, RAW_JSON_PATH)
    write_report(baseline_results, full_results, len(baseline_names), len(full_names),
                 raw_names, distractor_embed_elapsed, avg_distractor_ms, len(distractor_paths))
    print(f"\n=== Bitti === Rapor: {REPORT_PATH}")


def accuracy(results: list[dict]) -> tuple[float, float, float]:
    n = len(results)
    t1 = sum(1 for r in results if r["rank"] == 1) * 100.0 / n
    t3 = sum(1 for r in results if 1 <= r["rank"] <= 3) * 100.0 / n
    t5 = sum(1 for r in results if 1 <= r["rank"] <= 5) * 100.0 / n
    return t1, t3, t5


def write_report(baseline_results, full_results, baseline_size, full_size, raw_names,
                  distractor_embed_elapsed, avg_distractor_ms, distractor_count) -> None:
    raw_name_set = set(raw_names)
    b1, b3, b5 = accuracy(baseline_results)
    f1, f3, f5 = accuracy(full_results)
    avg_query_ms_baseline = sum(r["embed_ms"] + r["sort_ms"] for r in baseline_results) / len(baseline_results)
    avg_query_ms_full = sum(r["embed_ms"] + r["sort_ms"] for r in full_results) / len(full_results)

    lines = []
    lines.append("# Lens Faz 3C - Genisletilmis Veri Seti Stres Testi")
    lines.append("")
    lines.append("**Yontem notu:** Bu turun sonuclari, makinedeki Smart App Control kisitlamasi "
                  "nedeniyle C#/.NET pipeline'i bu oturumda calistirilamadigi icin **gecici olarak "
                  "Python uzerinden**, ayni model (openai/clip-vit-base-patch16) ve ayni mantikla "
                  "(embedding -> cosine similarity -> Top-K, brute-force) uretildi. C# stress-test "
                  "kodu (`src/Lens.AiProof`, `stresstest` modu) degistirilmedi/silinmedi; yonetici "
                  "makinesinde C# ile capraz dogrulanacak.")
    lines.append("")
    lines.append(f"Distractor kaynagi: Openverse (acik lisansli), kullanicinin manuel son "
                  f"duzenlemesiyle **{distractor_count}** gorsel. Toplam aday havuzu: **{full_size}**.")
    lines.append("")
    lines.append("## Ozet Tablo")
    lines.append("")
    lines.append("| Dataset | Top-1 | Top-3 | Top-5 | Avg Query (embed+sort) |")
    lines.append("|---|---:|---:|---:|---:|")
    lines.append(f"| {baseline_size} urun (baseline) | {b1:.1f}% | {b3:.1f}% | {b5:.1f}% | {avg_query_ms_baseline:.0f} ms |")
    lines.append(f"| ~{full_size} urun (+distractor) | {f1:.1f}% | {f3:.1f}% | {f5:.1f}% | {avg_query_ms_full:.0f} ms |")
    lines.append("")

    lines.append(f"## Varyasyon Turune Gore Top-5 (genisletilmis havuz, ~{full_size} aday)")
    lines.append("")
    lines.append("| Varyasyon | Top-1 | Top-3 | Top-5 | Sorgu sayisi |")
    lines.append("|---|---:|---:|---:|---:|")
    variation_types = sorted(set(r["variation_type"] for r in full_results))
    for vt in variation_types:
        group = [r for r in full_results if r["variation_type"] == vt]
        n = len(group)
        t1 = sum(1 for r in group if r["rank"] == 1) * 100.0 / n
        t3 = sum(1 for r in group if 1 <= r["rank"] <= 3) * 100.0 / n
        t5 = sum(1 for r in group if 1 <= r["rank"] <= 5) * 100.0 / n
        lines.append(f"| {vt} | {t1:.0f}% | {t3:.0f}% | {t5:.0f}% | {n} |")
    lines.append("")

    lines.append("## Indexing (Embedding) Performansi")
    lines.append("")
    lines.append(f"- Distractor embedding: {distractor_count} gorsel, {distractor_embed_elapsed:.1f} sn toplam, "
                  f"ort. {avg_distractor_ms:.0f} ms/gorsel (CPU, tek seferlik - bu Python turunda cache yok, "
                  f"her calistirmada yeniden hesaplaniyor; C# tarafinda persistent cache ile bu maliyet "
                  f"yalnizca ilk calistirmada olusur, bkz. ana rapor).")
    lines.append("")

    lines.append("## En Kotu 10 Query (genisletilmis havuzda)")
    lines.append("")
    worst = sorted(full_results, key=lambda r: -r["rank"])[:10]
    for r in worst:
        flag = " (Top-5 DISINDA)" if r["rank"] > 5 else ""
        lines.append(f"### `{r['query']}` ({r['variation_type']})")
        lines.append(f"- Dogru urun: `{r['ground_truth']}`, sirasi: **{r['rank']}** / {r['pool_size']}{flag}")
        lines.append("- Top-5:")
        for name, score in r["top5"]:
            marker = " <- dogru urun" if name == r["ground_truth"] else ""
            is_distractor = " (distractor)" if name not in raw_name_set else ""
            lines.append(f"  - {score:.4f}  `{name}`{is_distractor}{marker}")
        lines.append("")

    dropped = [r for r in full_results if r["rank"] > 5]
    lines.append("## Genel Gozlemler")
    lines.append("")
    dropped_baseline = sum(1 for r in baseline_results if r["rank"] > 5)
    lines.append(f"- {len(full_results)} sorgudan **{len(dropped)}** tanesinde dogru urun Top-5 disina dustu "
                  f"(baseline'da bu sayi {dropped_baseline} idi).")
    if dropped:
        by_vt = Counter(r["variation_type"] for r in dropped)
        lines.append("- Top-5 disina dusenlerin varyasyon turune dagilimi: "
                      + ", ".join(f"{k}={v}" for k, v in by_vt.most_common()))

    # Yanlisikla one cikan distractor'lar: top1 distractor iken dogru urun degilse
    wrong_top1_distractors = Counter(
        r["top5"][0][0] for r in full_results
        if r["top5"] and r["top5"][0][0] != r["ground_truth"] and r["top5"][0][0] not in raw_name_set
    )
    if wrong_top1_distractors:
        lines.append("- Top-1'de yanlislikla one cikan distractor'lar (kac sorguda): "
                      + ", ".join(f"`{k}`={v}" for k, v in wrong_top1_distractors.most_common(5)))
    lines.append("")
    lines.append("_Not: Bu rapor CLIP'in olculen davranisini belgeler; ~189 adaylik bir gozlemdir, "
                  "\"1000+ urunde de boyle calisir\" sonucu cikarilmamalidir. Python uzerinden uretildi "
                  "(gecici capraz-dogrulama yontemi); C# ile yonetici makinesinde ayrica dogrulanacak._")

    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
