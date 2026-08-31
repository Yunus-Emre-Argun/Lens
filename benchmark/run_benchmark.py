"""Faz 2: Tum benchmark pipeline'ini sirayla calistiran orkestratör.

Adimlar:
  1. make_variations.py        (data/raw -> data/variations)
  2. embed.py --model clip     (model agirliklarini indirir - buyuk indirme)
  3. embed.py --model siglip   (model agirliklarini indirir - buyuk indirme)
  4. evaluate.py --model clip
  5. evaluate.py --model siglip
  6. make_report.py

Kullanim:
    python run_benchmark.py
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).parent

STEPS = [
    ["make_variations.py"],
    ["embed.py", "--model", "clip"],
    ["embed.py", "--model", "siglip"],
    ["evaluate.py", "--model", "clip"],
    ["evaluate.py", "--model", "siglip"],
    ["make_report.py"],
]


def main() -> None:
    for step in STEPS:
        script = HERE / step[0]
        cmd = [sys.executable, str(script), *step[1:]]
        print(f"\n=== Calistiriliyor: {' '.join(step)} ===")
        subprocess.run(cmd, check=True)
    print("\nBenchmark tamamlandi. Sonuclar: results/report.md")


if __name__ == "__main__":
    main()
