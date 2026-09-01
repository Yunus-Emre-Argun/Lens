"""Faz 3C: Openverse API uzerinden acik lisansli "distractor" (rakip urun)
gorselleri indirir - nevresim/bedding/textile pattern temali.

Bu script bir engineering/veri-hazirlama aracidir; Lens Desktop runtime'inin
parcasi degildir. Sadece Python stdlib kullanir (urllib, hashlib, json,
csv) - yeni bir dependency eklenmedi. Gorsel siniflandirma icin yeni bir AI
modeli KULLANILMIYOR; uygunluk kontrolu basit anahtar-kelime (title/tags)
heuristigi ile yapiliyor.

Onemli tasarim karari: uygunsuz adaylar (fosil/coral/oda/insan/film vb.)
INDIRILMEDEN ONCE, Openverse'un dondurdugu title/tags metadata'sina
bakilarak elenir. Boylece bant genisligi/zaman israf edilmez. Elenen
adaylar da (dosya indirilmeden) sources.csv'ye status=rejected olarak
kaydedilir - seffaflik icin.

Indirilen gorseller GERCEK URUN DEGILDIR; sadece "kalabalik" olusturmak icin
kullanilan rakip/benzer desenli gorsellerdir. GitHub'a commit edilmez
(.gitignore korumasi), sadece sources.csv (metadata) izlenir.

Kullanim:
    python download_distractors.py --batch 1 --target 100
    python download_distractors.py --batch 2 --target 100
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# Konsol kod sayfasi (orn. cp1254) bazi Unicode basliklardaki karakterleri
# basamayabilir; crash yerine '?' ile degistir.
sys.stdout.reconfigure(errors="replace")

DATA_DIR = Path(__file__).parent / "data"
RAW_DIR = DATA_DIR / "raw"
DISTRACTORS_DIR = DATA_DIR / "distractors"
SOURCES_CSV = DISTRACTORS_DIR / "sources.csv"

USER_AGENT = "LensMVP-research/1.0 (internal PoC dataset build; local test use only)"
OPENVERSE_BASE = "https://api.openverse.org/v1/images/"
REUSABLE_LICENSE_TYPES = "commercial,modification"  # cc0/pdm/by/by-sa agirlikli sonuc verir

# Turkce + Ingilizce sorgular. Tek kelimelik asiri genis sorgulardan
# (pattern, linen, texture, bedding) kacinildi - cok fazla alakasiz sonuc
# getiriyorlar.
QUERIES = [
    # Turkce
    "nevresim deseni",
    "desenli nevresim",
    "çiçekli nevresim",
    "geometrik nevresim",
    "kelebek desenli nevresim",
    "modern nevresim deseni",
    "baskılı nevresim",
    "nevresim kumaş deseni",
    "tekstil desenleri",
    "çiçek desenli kumaş",
    "geometrik kumaş deseni",
    "yatak tekstili desen",
    "çocuk nevresim deseni",
    "çift kişilik nevresim deseni",
    "dijital baskı kumaş deseni",
    # English
    "duvet cover pattern",
    "floral duvet cover",
    "geometric duvet cover",
    "printed duvet cover",
    "bedding fabric pattern",
    "textile print pattern",
    "floral bedding pattern",
    "repeating textile pattern",
    "butterfly duvet cover",
    "abstract duvet cover pattern",
    "block print textile",
    "ikat fabric pattern",
    "paisley fabric pattern",
    "toile fabric pattern",
    "digital print fabric pattern",
]

# Uygunluk heuristigi Faz 3C kullanici geri bildirimi sonrasi sertlestirildi
# (ilk turda %39 gibi dusuk bir "gercekten uygun" orani gozlendi - coral/
# fossil, para, yemek, mimari, cicek bahcesi fotograflari sizmisti).
#
# 1) STRONG_ACCEPT: title'da gecerse dogrudan kabul (guclu urun/desen sinyali)
# 2) BLOCKLIST: title'da gecerse dogrudan red (bilinen alakasiz kategoriler)
# 3) Ikisi de degilse: GENERIC_PATTERN_WORDS'ten biri gecmeli VE
#    WEAK_REJECT'ten hicbiri gecmemeli (orn. "pattern" kelimesi tek basina
#    yeterli degil - "topographic sandbox calibration ... pattern" gibi
#    alakasiz sonuclari da eleyebilmek icin)
STRONG_ACCEPT = [
    "duvet cover", "duvet", "bed linen", "bedding fabric", "bed sheet",
    "bedcover", "bed cover", "quilt", "nevresim", "damask", "batik",
    "chiyo gami", "grammar of ornament", "wallpaper pattern",
    "wallpaper sample", "textile pattern", "textile print", "textile texture",
    "fabric pattern", "fabric print", "seamless pattern", "repeating pattern",
    "floral print wallpaper", "bed hanging", "quilted bedcover",
    "block print", "ikat", "toile de jouy", "chintz", "paisley",
    "zentangle", "striped linen",
]
BLOCKLIST = [
    "fossil", "coral", "geologic", "geological", "limestone", "outcrop",
    "bedrock", "borings", "eolianite", "calcarenit", "stromatolite",
    "dolomite", "paleoproterozoic", "mineral",
    "harry potter", "movie", "film still", "cinema", "non sequitur",
    "bedroom", "living room", "interior design", "house tour",
    "landscape", "mountain", "skyline", "building facade", "pavillon",
    "biennale", "camii", "mosque", "economist building",
    "portrait", " actor", "actress", " people ", " man ", " woman ",
    "dog", "cat", "animal", "vehicle", " car ", "sculpture", "museum exhibit",
    "mattress", "pillow", "furniture", "basketball", "leather texture",
    "safari", "grass clipping", "sandbox", "topographic", "scanner profile",
    "picture of atoms", "macro economics", "qr code", "paper money",
    "banknote", "currency", "kidney beans", "pot roast", "baking powder",
    "recipe", " beef ", "sock monkey", "origami", "yarn_", " yarn ",
    "letter to", "encyclopedic dictionary", "essai sur", "page detail",
    "typograph", "book of the dead", "iconography of zelda", "notebook",
    "flower bed", "wild flower", "flowers /", "tulipa", "hibiscus",
    "orchid", " hoya", "coleus", "ageratum", "daisy", "little daisy",
    "rice paper swans", "inicial ", "dagger sp", "cloaked minor",
    "pliegue en cofre",
]
GENERIC_PATTERN_WORDS = [
    "pattern", "desen", "texture", "print", "printed", "baskı", "baski",
    "motif", "ornament", "floral", "çiçek", "cicek", "geometric",
    "geometrik", "abstract", "kumaş", "kumas", "fabric", "textile",
]

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png"}
CSV_FIELDS = [
    "filename", "batch", "status", "rejection_reason", "source_url",
    "foreign_landing_url", "source_site", "title", "creator", "license",
    "license_version", "openverse_id",
]


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_ground_truth_hashes() -> set[str]:
    hashes = set()
    for p in RAW_DIR.glob("*"):
        if p.suffix.lower() in IMAGE_EXTENSIONS:
            hashes.add(sha256_of(p))
    return hashes


def load_existing_sources() -> tuple[set[str], set[str], int]:
    """Var olan sources.csv'yi okur. Donus: (kullanilmis URL seti, kullanilmis
    dosya-hash seti, mevcut satir sayisi)."""
    if not SOURCES_CSV.exists():
        return set(), set(), 0
    urls = set()
    n = 0
    with SOURCES_CSV.open("r", encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            urls.add(row["source_url"])
            n += 1
    hashes = set()
    for p in DISTRACTORS_DIR.glob("*"):
        if p.suffix.lower() in IMAGE_EXTENSIONS:
            hashes.add(sha256_of(p))
    return urls, hashes, n


def open_csv_writer():
    is_new = not SOURCES_CSV.exists()
    f = SOURCES_CSV.open("a", encoding="utf-8", newline="")
    writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
    if is_new:
        writer.writeheader()
    return f, writer


def search_openverse(query: str, page: int) -> list[dict]:
    params = (
        f"q={urllib.parse.quote(query)}"
        f"&license_type={REUSABLE_LICENSE_TYPES}"
        f"&page_size=20&page={page}&mature=false"
    )
    url = f"{OPENVERSE_BASE}?{params}"
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=30) as resp:
        data = json.load(resp)
    return data.get("results", [])


def slugify(text: str, max_len: int = 40) -> str:
    text = re.sub(r"[^a-zA-Z0-9]+", "-", text).strip("-").lower()
    return text[:max_len] or "image"


def check_acceptability(result: dict) -> tuple[bool, str]:
    """Basit anahtar-kelime heuristigi (3 katmanli). Donus: (kabul mu, sebep).

    Yalnizca TITLE'a bakilir - Openverse'un tags alani cok gevsek/otomatik
    etiketleniyor (orn. bir cicek bahcesi fotosuna "pattern" tag'i
    eklenebiliyor) ve ilk turda pek cok yanlis-pozitife yol acti.
    """
    title = " " + (result.get("title") or "").lower() + " "

    for bad in BLOCKLIST:
        if bad in title:
            return False, f"blocklist:{bad.strip()}"

    for strong in STRONG_ACCEPT:
        if strong in title:
            return True, ""

    if any(word in title for word in GENERIC_PATTERN_WORDS):
        return True, ""

    return False, "no-positive-keyword-match"


def download_image(url: str, dest: Path) -> tuple[bool, str]:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            content = resp.read()
    except (urllib.error.URLError, TimeoutError, ConnectionError, OSError) as e:
        return False, f"download-error:{e}"

    if len(content) < 5000:
        return False, "file-too-small"

    header = content[:12]
    is_jpeg = header[:2] == b"\xff\xd8"
    is_png = header[:8] == b"\x89PNG\r\n\x1a\n"
    if not (is_jpeg or is_png):
        return False, "not-a-valid-jpg-png"

    dest.write_bytes(content)
    return True, ""


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--batch", type=int, required=True, choices=[1, 2])
    parser.add_argument("--target", type=int, default=100)
    args = parser.parse_args()

    DISTRACTORS_DIR.mkdir(parents=True, exist_ok=True)
    ground_truth_hashes = load_ground_truth_hashes()
    used_urls, used_hashes, existing_row_count = load_existing_sources()
    csv_file, csv_writer = open_csv_writer()

    print(f"[batch {args.batch}] hedef: {args.target} KABUL EDILEN gorsel. "
          f"Mevcut sources.csv satiri: {existing_row_count}")

    accepted_count = 0
    rejected_count = 0
    reject_reasons: dict[str, int] = {}
    file_counter = existing_row_count
    query_hit_counts: dict[str, int] = {q: 0 for q in QUERIES}

    # Sabit bir is listesi: her sorgu icin sayfa 1..MAX_PAGES. Bir sorgunun
    # bir sayfasi bos donerse o sorgunun sonraki sayfalarina gecilmez (bos
    # sonuc dondurmeye devam eder). Bu, "consecutive empty" sayaci ile
    # ugrasip erken/yanlis durma riskini ortadan kaldirir.
    MAX_PAGES = 5
    work_items = [(q, p) for q in QUERIES for p in range(1, MAX_PAGES + 1)]

    try:
        exhausted_queries: set[str] = set()
        for query, page in work_items:
            if accepted_count >= args.target:
                break
            if query in exhausted_queries:
                continue

            try:
                results = search_openverse(query, page)
            except Exception as e:
                print(f"  [HATA] Openverse sorgusu basarisiz ({query}, page {page}): {e}")
                results = []

            if not results:
                exhausted_queries.add(query)
                continue
            query_hit_counts[query] += len(results)

            for r in results:
                if accepted_count >= args.target:
                    break
                url = r.get("url")
                if not url or url in used_urls:
                    continue
                used_urls.add(url)

                ok, reason = check_acceptability(r)
                if not ok:
                    rejected_count += 1
                    reject_reasons[reason] = reject_reasons.get(reason, 0) + 1
                    csv_writer.writerow({
                        "filename": "", "batch": args.batch, "status": "rejected",
                        "rejection_reason": reason, "source_url": url,
                        "foreign_landing_url": r.get("foreign_landing_url", ""),
                        "source_site": r.get("source", ""), "title": r.get("title", ""),
                        "creator": r.get("creator", ""), "license": r.get("license", ""),
                        "license_version": r.get("license_version", ""),
                        "openverse_id": r.get("id", ""),
                    })
                    continue

                lower_url = url.lower()
                if lower_url.endswith(".png"):
                    ext = ".png"
                elif lower_url.endswith(".jpg") or lower_url.endswith(".jpeg"):
                    ext = ".jpg"
                else:
                    continue

                file_counter += 1
                title_slug = slugify(r.get("title") or r.get("id", "image"))
                filename = f"b{args.batch}_{file_counter:04d}_{title_slug}{ext}"
                dest = DISTRACTORS_DIR / filename

                dl_ok, dl_reason = download_image(url, dest)
                if not dl_ok:
                    rejected_count += 1
                    reject_reasons[dl_reason] = reject_reasons.get(dl_reason, 0) + 1
                    csv_writer.writerow({
                        "filename": "", "batch": args.batch, "status": "rejected",
                        "rejection_reason": dl_reason, "source_url": url,
                        "foreign_landing_url": r.get("foreign_landing_url", ""),
                        "source_site": r.get("source", ""), "title": r.get("title", ""),
                        "creator": r.get("creator", ""), "license": r.get("license", ""),
                        "license_version": r.get("license_version", ""),
                        "openverse_id": r.get("id", ""),
                    })
                    continue

                file_hash = sha256_of(dest)
                if file_hash in ground_truth_hashes or file_hash in used_hashes:
                    dest.unlink()
                    rejected_count += 1
                    reason = "exact-duplicate"
                    reject_reasons[reason] = reject_reasons.get(reason, 0) + 1
                    csv_writer.writerow({
                        "filename": "", "batch": args.batch, "status": "rejected",
                        "rejection_reason": reason, "source_url": url,
                        "foreign_landing_url": r.get("foreign_landing_url", ""),
                        "source_site": r.get("source", ""), "title": r.get("title", ""),
                        "creator": r.get("creator", ""), "license": r.get("license", ""),
                        "license_version": r.get("license_version", ""),
                        "openverse_id": r.get("id", ""),
                    })
                    continue

                used_hashes.add(file_hash)
                accepted_count += 1
                print(f"  [{accepted_count}/{args.target}] KABUL: {filename}  ({r.get('title','')[:50]})")
                csv_writer.writerow({
                    "filename": filename, "batch": args.batch, "status": "accepted",
                    "rejection_reason": "", "source_url": url,
                    "foreign_landing_url": r.get("foreign_landing_url", ""),
                    "source_site": r.get("source", ""), "title": r.get("title", ""),
                    "creator": r.get("creator", ""), "license": r.get("license", ""),
                    "license_version": r.get("license_version", ""),
                    "openverse_id": r.get("id", ""),
                })
                csv_file.flush()
                time.sleep(0.2)

    finally:
        csv_file.close()

    print(f"\n[batch {args.batch}] tamamlandi: {accepted_count} kabul edilen, "
          f"{rejected_count} reddedilen (indirilmeden veya sonradan elendi).")
    print("Red nedenleri:")
    for reason, count in sorted(reject_reasons.items(), key=lambda x: -x[1]):
        print(f"  {reason}: {count}")
    print("\nEn cok sonuc getiren sorgular:")
    for q, n in sorted(query_hit_counts.items(), key=lambda x: -x[1])[:10]:
        print(f"  {n:4d}  {q}")


if __name__ == "__main__":
    main()
