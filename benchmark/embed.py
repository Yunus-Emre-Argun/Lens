"""Faz 2: Verilen model icin data/raw ve data/variations altindaki tum
gorseller icin embedding cikarir; embedding'leri ve zamanlama bilgisini
results/embeddings/ altina yazar.

Not: Bu script calistigi ilk seferde secilen modelin agirliklarini
Hugging Face Hub'dan indirir (buyuk indirme). Onay alinmadan calistirilmamalidir.

Kullanim:
    python embed.py --model clip
    python embed.py --model siglip
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np

from common import list_images, save_json
from models import embed_image, load_model

DATA_DIR = Path(__file__).parent / "data"
RESULTS_DIR = Path(__file__).parent / "results" / "embeddings"


def embed_directory(loaded, directory: Path) -> tuple[list[str], np.ndarray, list[float]]:
    paths = list_images(directory)
    filenames: list[str] = []
    vectors: list[np.ndarray] = []
    times: list[float] = []
    for path in paths:
        vector, elapsed = embed_image(loaded, path)
        filenames.append(path.name)
        vectors.append(vector)
        times.append(elapsed)
    matrix = np.vstack(vectors) if vectors else np.empty((0, 0), dtype=np.float32)
    return filenames, matrix, times


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True, choices=["clip", "siglip"])
    args = parser.parse_args()

    loaded = load_model(args.model)
    print(f"[{args.model}] model yuklendi: {loaded.hf_id} ({loaded.load_time_seconds:.2f} sn)")

    raw_names, raw_matrix, raw_times = embed_directory(loaded, DATA_DIR / "raw")
    if not raw_names:
        raise SystemExit(f"'{DATA_DIR / 'raw'}' icinde gorsel bulunamadi.")

    var_names, var_matrix, var_times = embed_directory(loaded, DATA_DIR / "variations")
    if not var_names:
        raise SystemExit(
            f"'{DATA_DIR / 'variations'}' icinde gorsel bulunamadi. "
            "Once 'python make_variations.py' calistirilmali."
        )

    RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    np.savez(
        RESULTS_DIR / f"{args.model}_embeddings.npz",
        raw_names=np.array(raw_names),
        raw_vectors=raw_matrix,
        var_names=np.array(var_names),
        var_vectors=var_matrix,
    )
    save_json(
        {
            "model_key": args.model,
            "hf_id": loaded.hf_id,
            "load_time_seconds": loaded.load_time_seconds,
            "raw_embed_times_seconds": dict(zip(raw_names, raw_times)),
            "variation_embed_times_seconds": dict(zip(var_names, var_times)),
        },
        RESULTS_DIR / f"{args.model}_timing.json",
    )
    print(f"[{args.model}] {len(raw_names)} orijinal + {len(var_names)} varyasyon embed edildi.")
    print(f"Sonuclar: {RESULTS_DIR}")


if __name__ == "__main__":
    main()
