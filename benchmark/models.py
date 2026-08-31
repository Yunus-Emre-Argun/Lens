"""Faz 2 benchmark: CLIP / SigLIP model kaydi ve embedding cikarma yardimcilari.

DEVICE kasitli olarak "cpu" sabitlenmistir: bu gelistirme makinesinde bir GPU
bulunsa bile, PoC hedefi "guclu GPU garanti olmayan ofis bilgisayari" oldugu
icin benchmark CPU uzerinde calismalidir (bkz. docs/ARCHITECTURE_PROPOSAL.md).
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch
from PIL import Image
from transformers import CLIPModel, CLIPProcessor, SiglipModel, SiglipProcessor

from common import Timer, l2_normalize

DEVICE = "cpu"

MODEL_REGISTRY = {
    "clip": "openai/clip-vit-base-patch16",
    "siglip": "google/siglip-base-patch16-224",
}


@dataclass
class LoadedModel:
    key: str
    hf_id: str
    model: object
    processor: object
    load_time_seconds: float


def load_model(key: str) -> LoadedModel:
    if key not in MODEL_REGISTRY:
        raise ValueError(f"Bilinmeyen model anahtari: {key}. Secenekler: {list(MODEL_REGISTRY)}")
    hf_id = MODEL_REGISTRY[key]

    with Timer() as t:
        if key == "clip":
            processor = CLIPProcessor.from_pretrained(hf_id)
            model = CLIPModel.from_pretrained(hf_id)
        else:  # siglip
            processor = SiglipProcessor.from_pretrained(hf_id)
            model = SiglipModel.from_pretrained(hf_id)
        model.to(DEVICE)
        model.eval()

    return LoadedModel(key=key, hf_id=hf_id, model=model, processor=processor, load_time_seconds=t.elapsed)


def embed_image(loaded: LoadedModel, image_path: Path) -> tuple[np.ndarray, float]:
    """Bir gorsel icin L2-normalize embedding vektoru ve gecen sureyi (sn) dondurur.

    Sure, on-isleme (resize/normalize) + model forward pass'ini kapsar; disk
    okuma (Image.open) haric tutulur — bu, "CPU embedding suresi" metrigi
    icin olculmek istenen asil maliyettir.
    """
    image = Image.open(image_path).convert("RGB")

    with Timer() as t:
        inputs = loaded.processor(images=image, return_tensors="pt")
        with torch.no_grad():
            outputs = loaded.model.get_image_features(**inputs)

    # Bu transformers surumunde get_image_features() pooled vektor yerine
    # BaseModelOutputWithPooling dondurur; asil (projeksiyonlu) goruntu
    # embedding'i pooler_output alanindadir (CLIP icin visual_projection
    # uygulanmis, SigLIP icin vision tower'in pooled ciktisi).
    vector = outputs.pooler_output[0].numpy().astype(np.float32)
    return l2_normalize(vector), t.elapsed
