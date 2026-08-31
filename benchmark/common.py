"""Faz 2 benchmark icin paylasilan yardimci fonksiyonlar."""
from __future__ import annotations

import json
import time
from pathlib import Path

import numpy as np

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


def list_images(directory: Path) -> list[Path]:
    if not directory.exists():
        return []
    return sorted(
        p for p in directory.iterdir()
        if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS
    )


def save_json(data, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def l2_normalize(vec: np.ndarray) -> np.ndarray:
    norm = np.linalg.norm(vec)
    if norm == 0:
        return vec
    return vec / norm


class Timer:
    """Wall-clock süreyi saniye cinsinden `elapsed` alaninda tutan context manager."""

    def __enter__(self) -> "Timer":
        self._start = time.perf_counter()
        return self

    def __exit__(self, *exc) -> None:
        self.elapsed = time.perf_counter() - self._start


def cosine_similarity_matrix(queries: np.ndarray, gallery: np.ndarray) -> np.ndarray:
    """queries: (Q, D), gallery: (G, D) — ikisi de L2-normalize kabul edilir. Donus: (Q, G)."""
    return queries @ gallery.T


def top_k_indices(similarities: np.ndarray, k: int) -> np.ndarray:
    k = min(k, similarities.shape[0])
    idx = np.argpartition(-similarities, k - 1)[:k]
    return idx[np.argsort(-similarities[idx])]
