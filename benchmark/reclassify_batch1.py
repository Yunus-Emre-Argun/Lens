"""Faz 3C - tek seferlik yardimci script: batch 1'in ilk otomatik-kabul
listesini, manuel/heuristik 'makul rakip urun mu?' incelemesinden gecirir.
Reddedilenleri distractors/rejected/ altina tasir, sources.csv'yi gunceller.

Bu script kalici bir arac degildir, tek seferlik temizlik icin yazildi.
"""
from __future__ import annotations

import csv
from pathlib import Path

DISTRACTORS_DIR = Path(__file__).parent / "data" / "distractors"
REJECTED_DIR = DISTRACTORS_DIR / "rejected"
SOURCES_CSV = DISTRACTORS_DIR / "sources.csv"

# Manuel inceleme sonucu KABUL edilen dosyalar (kalan hepsi reddedildi).
ACCEPTED_FILENAMES = {
    "b1_0001_pimpernel-9-365.jpg": "",
    "b1_0002_duvet-cover-detail.jpg": "",
    "b1_0003_bed-linen.jpg": "",
    "b1_0023_striped-linen.jpg": "",
    "b1_0024_bed-sheet.jpg": "",
    "b1_0028_037-optical-after-effect.jpg": "",
    "b1_0029_webtreatsetc-abstract-warped-dots-seamle.jpg": "",
    "b1_0030_webtreats-seamless-retro-grunge-abstract.jpg": "",
    "b1_0031_african-textile-pattern.jpg": "",
    "b1_0032_chiyo-gami.jpg": "",
    "b1_0033_image-plate-from-owen-jones-1853-classic.jpg": "",
    "b1_0034_vintage-woodblock-print-of-japanese-text.jpg": "",
    "b1_0039_silver-pixel-pattern-paper.jpg": "",
    "b1_0041_zebra-print.jpg": "",
    "b1_0044_zentangle-4-16-12.jpg": "",
    "b1_0046_webtreats-tileable-organic-op-art-photos.jpg": "",
    "b1_0047_blue-and-green-handmade-batik-paper.jpg": "",
    "b1_0049_halftone-pattern.jpg": "",
    "b1_0050_isotropic-spherical-maze-ii.jpg": "",
    "b1_0051_webtreats-seamless-retro-grunge-abstract.jpg": "",
    "b1_0053_sample-patterns.jpg": "",
    "b1_0057_from-harpel-s-typograph.jpg": "",
    "b1_0062_detail-of-mixed-tape-quilt.jpg": "",
    "b1_0063_sixties-seventies-era-floral-print-wallp.jpg": "",
    "b1_0066_florentine-swirl-pattern-paper.jpg": "",
    "b1_0067_pink-damask-paper.jpg": "",
    "b1_0069_wallpaper-samples.jpg": "",
    "b1_0071_textile-texture-3-dots.jpg": "",
    "b1_0073_wallpaper.jpg": "",
    "b1_0074_owls-and-butterflies.jpg": "",
    "b1_0079_brentano-filigree-varsity-portfolio-coll.jpg": "",
    "b1_0080_textures-for-textiles.jpg": "",
    "b1_0084_d-cor-de-la-chambre-d-alep-mus-e-d-art-i.jpg": "",
    "b1_0088_vintage-woodblock-print-of-japanese-text.jpg": "",
    "b1_0092_song4-detail.jpg": "",
    "b1_0093_emboss-dhamadka-kutch.jpg": "",
    "b1_0101_pattern-of-red-roses.jpg": "",
    "b1_0111_bed-hanging-with-pegasus-and-the-nine-mu.jpg": "",
    "b1_0114_wla-vanda-quilted-bedcover-coromandel-co.jpg": "",
    "b1_0144_needlework-bed-hanging-in-the-bizarre-st.jpg": "",
}


def main() -> None:
    REJECTED_DIR.mkdir(parents=True, exist_ok=True)

    with SOURCES_CSV.open("r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        fieldnames = reader.fieldnames
        rows = list(reader)

    moved = 0
    kept = 0
    for row in rows:
        if row["status"] != "accepted":
            continue
        filename = row["filename"]
        if filename in ACCEPTED_FILENAMES:
            kept += 1
            continue

        # manuel inceleme: reddedildi
        src = DISTRACTORS_DIR / filename
        if src.exists():
            dest = REJECTED_DIR / filename
            src.rename(dest)
            moved += 1
        row["status"] = "rejected"
        row["rejection_reason"] = "manual-review:not-product-pattern-focused"

    with SOURCES_CSV.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    print(f"Manuel incelemede kabul edilen: {kept}")
    print(f"Reddedilip rejected/ klasorune tasinan: {moved}")


if __name__ == "__main__":
    main()
