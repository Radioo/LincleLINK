"""Generate all app icon artifacts from the master SVG (packaging/LL_logo.svg).

Outputs:
  - src/LincleLINK.App/Assets/LL_logo.ico  (16..256 px)
  - packaging/linux/linclelink.png          (256 px)
  - packaging/macos/LL_logo.icns            (16..1024 px)

Requires: pip install pillow resvg-py
"""

import io
from pathlib import Path

import resvg_py
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SVG = Path(__file__).resolve().parent / "LL_logo.svg"

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
ICNS_SIZES = [16, 32, 64, 128, 256, 512, 1024]


def render(size: int) -> Image.Image:
    png_bytes = resvg_py.svg_to_bytes(svg_string=SVG.read_text(), width=size, height=size)
    return Image.open(io.BytesIO(bytes(png_bytes))).convert("RGBA")


def main() -> None:
    ico_path = ROOT / "src" / "LincleLINK.App" / "Assets" / "LL_logo.ico"
    png_path = ROOT / "packaging" / "linux" / "linclelink.png"
    icns_path = ROOT / "packaging" / "macos" / "LL_logo.icns"

    ico_images = [render(s) for s in ICO_SIZES]
    ico_images[-1].save(
        ico_path, format="ICO", append_images=ico_images[:-1],
        sizes=[(s, s) for s in ICO_SIZES],
    )
    print(f"wrote {ico_path}")

    render(256).save(png_path, format="PNG")
    print(f"wrote {png_path}")

    icns_images = [render(s) for s in ICNS_SIZES]
    icns_images[-1].save(icns_path, format="ICNS", append_images=icns_images[:-1])
    print(f"wrote {icns_path}")


if __name__ == "__main__":
    main()
