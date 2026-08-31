"""Faz 2: clip ve siglip icin evaluate.py ciktilarini okuyup
kisa, karsilastirmali bir report.md uretir.

Kullanim:
    python make_report.py
"""
from __future__ import annotations

from pathlib import Path

from common import load_json

RESULTS_DIR = Path(__file__).parent / "results"
MODELS = ["clip", "siglip"]


def format_pct(value) -> str:
    return f"{value * 100:.0f}%" if value is not None else "n/a"


def format_ms(value) -> str:
    return f"{value * 1000:.0f} ms" if value is not None else "n/a"


def main() -> None:
    results = {}
    for model_key in MODELS:
        path = RESULTS_DIR / f"{model_key}_results.json"
        if not path.exists():
            raise SystemExit(f"'{path}' yok. Once 'python evaluate.py --model {model_key}' calistirin.")
        results[model_key] = load_json(path)["summary"]

    lines = [
        "# Lens Faz 2 - CLIP vs SigLIP Benchmark Sonuclari",
        "",
        "Kucuk olcekli, gosterge niteliginde bir benchmark (11 orijinal gorsel + "
        "sentetik varyasyonlar). Istatistiksel genel gecerlilik iddiasi yoktur.",
        "",
        "## Genel Dogruluk (Top-K)",
        "",
        "| Model | Top-1 | Top-3 | Top-5 | Sorgu sayisi |",
        "|---|---|---|---|---|",
    ]
    for model_key in MODELS:
        s = results[model_key]
        acc = s["accuracy"]
        lines.append(
            f"| {s['hf_id']} | {format_pct(acc['top1'])} | {format_pct(acc['top3'])} | "
            f"{format_pct(acc['top5'])} | {s['total_queries']} |"
        )
    lines.append("")

    lines.append("## Varyasyon Turune Gore Dogruluk (Top-1)")
    lines.append("")
    variation_types = sorted(next(iter(results.values()))["accuracy_by_variation_type"].keys())
    header = "| Varyasyon | " + " | ".join(results[m]["hf_id"] for m in MODELS) + " |"
    lines.append(header)
    lines.append("|---|" + "---|" * len(MODELS))
    for vt in variation_types:
        row = [vt]
        for model_key in MODELS:
            acc = results[model_key]["accuracy_by_variation_type"].get(vt, {})
            row.append(format_pct(acc.get("top1")))
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")

    lines.append("## Zamanlama (CPU)")
    lines.append("")
    lines.append(
        "| Model | Model yukleme (bir kez) | Ort. embedding suresi | "
        "Ort. similarity+sort suresi | Ort. toplam sorgu suresi |"
    )
    lines.append("|---|---|---|---|---|")
    for model_key in MODELS:
        s = results[model_key]
        lines.append(
            f"| {s['hf_id']} | {s['model_load_time_seconds']:.2f} sn | "
            f"{format_ms(s['mean_embed_time_seconds'])} | "
            f"{format_ms(s['mean_similarity_sort_time_seconds'])} | "
            f"{format_ms(s['mean_total_query_time_seconds'])} |"
        )
    lines.append("")
    lines.append(
        "Not: Model yukleme suresi bir kerelik maliyettir, sorgu basina tekrarlanmaz. "
        "\"Toplam sorgu suresi\" = embedding suresi + similarity/sort suresi."
    )

    report_path = RESULTS_DIR / "report.md"
    report_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Rapor yazildi: {report_path}")


if __name__ == "__main__":
    main()
