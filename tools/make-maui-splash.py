"""Regenerate MAUI splash.png: yellow M only on transparent (no black plate)."""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "MobiHymn4.Web" / "wwwroot"
OUT = ROOT / "MobiHymn4.Maui" / "Resources" / "Splash" / "splash.png"
SIZE = 432
BRAND = (245, 210, 0, 255)


def extract_yellow(im: Image.Image) -> Image.Image:
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sp, op = im.load(), out.load()
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            r, g, b, a = sp[x, y]
            if a < 16:
                continue
            # Yellow / gold only — drop black, charcoal, and dark fringe.
            if r > 180 and g > 140 and b < 120 and (r + g) > (b * 3):
                op[x, y] = BRAND
    bbox = out.getbbox()
    return out.crop(bbox) if bbox else out


def main() -> None:
    src_path = next(
        WEB / name
        for name in ("splash-m.png", "logo-mark.png", "icon-512.png", "logo.png")
        if (WEB / name).exists()
    )
    mark = extract_yellow(Image.open(src_path).convert("RGBA"))
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    side = int(SIZE * 0.72)
    logo = mark.copy()
    logo.thumbnail((side, side), Image.Resampling.LANCZOS)
    # Re-threshold after resize to kill dark antialias fringe.
    logo = extract_yellow(logo)
    ox = (SIZE - logo.size[0]) // 2
    oy = (SIZE - logo.size[1]) // 2
    canvas.paste(logo, (ox, oy), logo)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUT, optimize=True)

    dark = 0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = canvas.getpixel((x, y))
            if a > 0 and r < 40 and g < 40 and b < 40:
                dark += 1
    print(f"wrote {OUT} bbox={canvas.getbbox()} dark_pixels={dark}")


if __name__ == "__main__":
    main()
