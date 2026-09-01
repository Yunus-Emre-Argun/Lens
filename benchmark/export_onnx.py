"""Faz 3A: CLIP vision encoder'ini (openai/clip-vit-base-patch16) ONNX'e export eder.

Bu script bir engineering/one-time asset-generation aracidir; Lens Desktop
uygulamasinin runtime'inin bir parcasi degildir (Lens.exe Python calistirmaz).
Cikti .onnx dosyasi, .NET tarafinda Microsoft.ML.OnnxRuntime ile yuklenir.

CLIPVisionModelWithProjection kullanilir cunku ciktisi (image_embeds), Faz 2
benchmarkinda CLIPModel.get_image_features() ile elde edilen projected image
embedding ile matematiksel olarak aynidir (vision_model -> pooler_output ->
visual_projection). Boylece Python ve .NET tarafi ayni embedding'i uretir.

Kullanim:
    python export_onnx.py
"""
from __future__ import annotations

from pathlib import Path

import torch
from transformers import CLIPVisionModelWithProjection

HF_ID = "openai/clip-vit-base-patch16"
OUTPUT_PATH = Path(__file__).parent.parent / "models" / "clip-vision-b16-openai.onnx"


def main() -> None:
    model = CLIPVisionModelWithProjection.from_pretrained(HF_ID)
    model.eval()

    dummy_input = torch.zeros(1, 3, 224, 224, dtype=torch.float32)

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)

    torch.onnx.export(
        model,
        (dummy_input,),
        str(OUTPUT_PATH),
        input_names=["pixel_values"],
        output_names=["image_embeds"],
        dynamic_axes={
            "pixel_values": {0: "batch"},
            "image_embeds": {0: "batch"},
        },
        opset_version=17,
        dynamo=False,
    )

    size_mb = OUTPUT_PATH.stat().st_size / (1024 * 1024)
    print(f"ONNX modeli yazildi: {OUTPUT_PATH} ({size_mb:.1f} MB)")


if __name__ == "__main__":
    main()
