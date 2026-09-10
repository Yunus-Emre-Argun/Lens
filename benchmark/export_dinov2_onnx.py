"""[PILOT] DINOv2 ViT-B/14 (facebook/dinov2-base) vision encoder'ini ONNX'e export eder.

Bu script bir GELISTIRME/asset-uretim aracidir; Lens Desktop uygulamasinin
runtime'inin parcasi DEGILDIR. Son kullanicida Python GEREKMEZ - uretilen
.onnx dosyasi .NET tarafinda Microsoft.ML.OnnxRuntime ile yuklenir.

Neden Dinov2Model + CLS token?
    DINOv2 self-supervised bir encoder'dir; CLIP'teki gibi bir projection
    head'i YOKTUR. Global goruntu temsili, son katmanin CLS token'idir
    (last_hidden_state[:, 0]). Asagidaki sarmalayici tam olarak bunu doner.

L2 normalizasyon BILEREK grafige DAHIL EDILMEDI: normalizasyon .NET tarafinda
(Lens.Core.Ai.EmbeddingVector.L2NormalizeChecked) yapilir - boylece NaN/
Infinity/sifir-norm gibi bozuk ciktilar sessizce gecmek yerine acik hataya
donusur. Mevcut CLIP export'u da ayni deseni kullanir.

ONNX sozlesmesi (Lens.Core.Ai.DinoV2BaseProfile ile BIREBIR ayni olmalidir):
    girdi   : "pixel_values"  float32 [batch, 3, 224, 224]
    cikti   : "image_embeds"   float32 [batch, 768]
    opset   : 17
    batch   : dinamik

Kullanim:
    python export_dinov2_onnx.py            # export + PyTorch/ONNX dogrulamasi
    python export_dinov2_onnx.py --verify-only
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

import numpy as np
import torch
from PIL import Image
from transformers import Dinov2Model

# Resmi kaynak ve KESIN revision - "main" gibi hareketli bir referans
# KULLANILMAZ, aksi halde uretilen dosya zamanla sessizce degisebilir.
HF_ID = "facebook/dinov2-base"
HF_REVISION = "f9e44c814b77203eaa57a6bdbbd535f21ede1415"

OUTPUT_PATH = Path(__file__).parent.parent / "models" / "dinov2-base.onnx"

# On isleme sabitleri - modelin KENDI preprocessor_config.json'indan.
# CLIP'in sabitleri buraya TASINMAZ (bkz. docs/MODEL_BENCHMARK.md).
RESIZE_SHORTEST_EDGE = 256
CROP_SIZE = 224
IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)

EMBEDDING_DIM = 768
OPSET = 17
INPUT_NAME = "pixel_values"
OUTPUT_NAME = "image_embeds"


class DinoV2ClsEncoder(torch.nn.Module):
    """Dinov2Model'i saran, YALNIZCA CLS token'i donduren minimal modul."""

    def __init__(self, backbone: Dinov2Model) -> None:
        super().__init__()
        self.backbone = backbone

    def forward(self, pixel_values: torch.Tensor) -> torch.Tensor:
        outputs = self.backbone(pixel_values=pixel_values)
        # last_hidden_state: [batch, 1 + num_patches, hidden]; index 0 = CLS
        return outputs.last_hidden_state[:, 0]


def load_encoder() -> DinoV2ClsEncoder:
    """Resmi agirliklari (pinlenmis revision ile) yerel HF onbelleginden yukler."""
    backbone = Dinov2Model.from_pretrained(HF_ID, revision=HF_REVISION)
    backbone.eval()
    encoder = DinoV2ClsEncoder(backbone)
    encoder.eval()
    return encoder


def preprocess(image_path: Path) -> np.ndarray:
    """DINOv2 on islemesi: bicubic shortest-edge 256 -> center crop 224 -> ImageNet normalize.

    .NET tarafindaki Lens.Core.Ai.ImagePreprocessor (DinoV2 profili) ile AYNI
    adimlari uygular; bu, PyTorch/ONNX karsilastirmasinin ayni girdi uzerinde
    yapilmasini saglar.
    """
    image = Image.open(image_path).convert("RGB")

    width, height = image.size
    scale = RESIZE_SHORTEST_EDGE / min(width, height)
    new_width = max(CROP_SIZE, round(width * scale))
    new_height = max(CROP_SIZE, round(height * scale))
    image = image.resize((new_width, new_height), Image.BICUBIC)

    left = max(0, (new_width - CROP_SIZE) // 2)
    top = max(0, (new_height - CROP_SIZE) // 2)
    image = image.crop((left, top, left + CROP_SIZE, top + CROP_SIZE))

    array = np.asarray(image, dtype=np.float32) / 255.0
    array = (array - IMAGENET_MEAN) / IMAGENET_STD
    # HWC -> CHW, batch ekle
    return np.transpose(array, (2, 0, 1))[np.newaxis, ...].astype(np.float32)


def l2_normalize(vector: np.ndarray) -> np.ndarray:
    norm = np.linalg.norm(vector)
    if norm == 0:
        raise ValueError("Sifir normlu embedding - normalize edilemez")
    return vector / norm


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def export(encoder: DinoV2ClsEncoder) -> None:
    dummy_input = torch.zeros(1, 3, CROP_SIZE, CROP_SIZE, dtype=torch.float32)
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)

    torch.onnx.export(
        encoder,
        (dummy_input,),
        str(OUTPUT_PATH),
        input_names=[INPUT_NAME],
        output_names=[OUTPUT_NAME],
        dynamic_axes={
            INPUT_NAME: {0: "batch"},
            OUTPUT_NAME: {0: "batch"},
        },
        opset_version=OPSET,
        dynamo=False,
    )


def collect_sample_images(limit: int = 8) -> list[Path]:
    """Dogrulama icin yerel gorseller toplar (repoya/pakete HICBIRI eklenmez)."""
    candidates: list[Path] = []
    for folder in (
        Path(__file__).parent / "data" / "distractors",
        Path(__file__).parent / "data" / "variations",
        Path(__file__).parent.parent / "nevresim",
    ):
        if not folder.is_dir():
            continue
        for pattern in ("*.jpg", "*.jpeg", "*.png"):
            candidates.extend(sorted(folder.glob(pattern)))
        if len(candidates) >= limit:
            break
    return candidates[:limit]


def verify(encoder: DinoV2ClsEncoder) -> bool:
    """PyTorch ile ONNX ciktisini AYNI girdiler uzerinde karsilastirir."""
    import onnxruntime as ort

    images = collect_sample_images()
    if not images:
        print("[HATA] Dogrulama icin yerel gorsel bulunamadi.", file=sys.stderr)
        return False

    session = ort.InferenceSession(str(OUTPUT_PATH), providers=["CPUExecutionProvider"])

    worst_cosine = 1.0
    worst_abs_diff = 0.0
    for image_path in images:
        tensor = preprocess(image_path)

        with torch.no_grad():
            torch_raw = encoder(torch.from_numpy(tensor)).numpy()[0]
        onnx_raw = session.run([OUTPUT_NAME], {INPUT_NAME: tensor})[0][0]

        if onnx_raw.shape != (EMBEDDING_DIM,):
            print(f"[HATA] ONNX cikti boyutu {onnx_raw.shape}, beklenen ({EMBEDDING_DIM},)", file=sys.stderr)
            return False

        abs_diff = float(np.max(np.abs(torch_raw - onnx_raw)))
        cosine = float(np.dot(l2_normalize(torch_raw), l2_normalize(onnx_raw)))
        worst_abs_diff = max(worst_abs_diff, abs_diff)
        worst_cosine = min(worst_cosine, cosine)
        print(f"    {image_path.name[:52]:<52} cos={cosine:.8f} maxAbsDiff={abs_diff:.3e}")

    print(f"\n  Dogrulanan gorsel sayisi : {len(images)}")
    print(f"  En kotu cosine           : {worst_cosine:.8f}")
    print(f"  En buyuk mutlak fark     : {worst_abs_diff:.3e}")

    # Esik: float32 birikim farki bu buyuklukte beklenir; bunun otesi gercek
    # bir export uyusmazligina isaret eder ve model KULLANILMAMALIDIR.
    if worst_cosine < 0.999999 or worst_abs_diff > 1e-3:
        print("\n[HATA] PyTorch/ONNX uyusmazligi - bu model KULLANILMAMALIDIR.", file=sys.stderr)
        return False

    print("\n  [OK] PyTorch ve ONNX ciktilari sayisal olarak esdeger.")
    return True


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify-only", action="store_true",
                        help="Yeniden export etmeden mevcut .onnx dosyasini dogrular.")
    args = parser.parse_args()

    print(f"Model     : {HF_ID}")
    print(f"Revision  : {HF_REVISION}")
    print(f"Cikti     : {OUTPUT_PATH}")
    print()

    encoder = load_encoder()

    if not args.verify_only:
        print("[1] ONNX export ediliyor...")
        export(encoder)
    elif not OUTPUT_PATH.exists():
        print(f"[HATA] Dosya yok: {OUTPUT_PATH}", file=sys.stderr)
        return 1

    size_mb = OUTPUT_PATH.stat().st_size / (1024 * 1024)
    digest = sha256_of(OUTPUT_PATH)
    print(f"[2] Dosya  : {size_mb:.1f} MB")
    print(f"    SHA-256: {digest}")
    print()
    print("[3] PyTorch <-> ONNX sayisal dogrulamasi:")

    if not verify(encoder):
        return 1

    print()
    print("Bu SHA-256 degeri docs/MODEL_CARD.md icine kaydedilmelidir.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
