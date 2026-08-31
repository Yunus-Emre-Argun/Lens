"""Faz 2: Orijinal 11 gorselden kontrollu sentetik query varyasyonlari uretir.

Varyasyon turleri (hepsi hafif/kontrollu; agresif renk/hue/saturation YOK):
- crop:               kenarlardan %15 kirpma (ortadaki %70 kalir)
- downscale_upscale:  %25'e kucultup orijinal boyuta geri buyutme (dusuk cozunurluk simulasyonu)
- jpeg_quality:       JPEG kalite 30 ile yeniden kaydetme (sikistirma artefakti)
- brightness:         PIL ImageEnhance.Brightness, faktor 0.85 (hafif karartma)
- contrast:           PIL ImageEnhance.Contrast, faktor 1.2 (hafif kontrast artisi)

Cikti: benchmark/data/variations/<orijinal_stem>__<varyasyon_turu>.jpg
Ground truth eslemesi: benchmark/data/variations_manifest.json
  {"<varyasyon_dosyasi>": {"original": "<orijinal_dosyasi>", "variation_type": "<tur>"}}

Kullanim:
    python make_variations.py
"""
from __future__ import annotations

import io
from pathlib import Path

from PIL import Image, ImageEnhance

from common import list_images, save_json

RAW_DIR = Path(__file__).parent / "data" / "raw"
VARIATIONS_DIR = Path(__file__).parent / "data" / "variations"
MANIFEST_PATH = Path(__file__).parent / "data" / "variations_manifest.json"

CROP_FRACTION = 0.15
DOWNSCALE_FACTOR = 0.25
JPEG_QUALITY = 30
BRIGHTNESS_FACTOR = 0.85
CONTRAST_FACTOR = 1.2


def make_crop(image: Image.Image) -> Image.Image:
    w, h = image.size
    left, top = int(w * CROP_FRACTION), int(h * CROP_FRACTION)
    right, bottom = w - left, h - top
    return image.crop((left, top, right, bottom))


def make_downscale_upscale(image: Image.Image) -> Image.Image:
    w, h = image.size
    small_size = (max(1, int(w * DOWNSCALE_FACTOR)), max(1, int(h * DOWNSCALE_FACTOR)))
    small = image.resize(small_size, Image.BILINEAR)
    return small.resize((w, h), Image.BILINEAR)


def make_jpeg_quality(image: Image.Image) -> Image.Image:
    buffer = io.BytesIO()
    image.convert("RGB").save(buffer, format="JPEG", quality=JPEG_QUALITY)
    buffer.seek(0)
    return Image.open(buffer).convert("RGB")


def make_brightness(image: Image.Image) -> Image.Image:
    return ImageEnhance.Brightness(image).enhance(BRIGHTNESS_FACTOR)


def make_contrast(image: Image.Image) -> Image.Image:
    return ImageEnhance.Contrast(image).enhance(CONTRAST_FACTOR)


VARIATION_FUNCS = {
    "crop": make_crop,
    "downscale_upscale": make_downscale_upscale,
    "jpeg_quality": make_jpeg_quality,
    "brightness": make_brightness,
    "contrast": make_contrast,
}


def main() -> None:
    originals = list_images(RAW_DIR)
    if not originals:
        raise SystemExit(
            f"'{RAW_DIR}' icinde gorsel bulunamadi. "
            "Once 11 orijinal nevresim gorselini bu klasore ekleyin."
        )

    VARIATIONS_DIR.mkdir(parents=True, exist_ok=True)
    manifest: dict[str, dict[str, str]] = {}

    for original_path in originals:
        image = Image.open(original_path).convert("RGB")
        for variation_type, func in VARIATION_FUNCS.items():
            varied = func(image)
            out_name = f"{original_path.stem}__{variation_type}.jpg"
            out_path = VARIATIONS_DIR / out_name
            varied.convert("RGB").save(out_path, format="JPEG", quality=95)
            manifest[out_name] = {
                "original": original_path.name,
                "variation_type": variation_type,
            }

    save_json(manifest, MANIFEST_PATH)
    print(f"{len(originals)} orijinal gorselden {len(manifest)} varyasyon uretildi.")
    print(f"Varyasyonlar: {VARIATIONS_DIR}")
    print(f"Manifest: {MANIFEST_PATH}")


if __name__ == "__main__":
    main()
