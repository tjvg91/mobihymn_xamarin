"""Regenerate PWA icons: transparent 'any' (splash) + charcoal maskable (launcher)."""
from pathlib import Path

from PIL import Image

WEB = Path(__file__).resolve().parents[1] / "MobiHymn4.Web" / "wwwroot"
CHAR = (0x2D, 0x2D, 0x2D, 255)

SRC = Image.open(WEB / "icon-512.png").convert("RGBA")


def extract_mark(im: Image.Image) -> Image.Image:
    """Keep yellow M; drop charcoal/black plate → transparent."""
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sp, op = im.load(), out.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = sp[x, y]
            if a < 8:
                continue
            if r > 120 and g > 90 and b < 120:
                op[x, y] = (r, g, b, 255)
            elif r < 40 and g < 40 and b < 40:
                continue
            elif abs(r - 0x2D) <= 12 and abs(g - 0x2D) <= 12 and abs(b - 0x2D) <= 12:
                continue
            elif r + g > 120 and b < 150:
                op[x, y] = (r, g, b, a)
    bbox = out.getbbox()
    return out.crop(bbox) if bbox else out


def place(mark: Image.Image, size: int, scale: float, bg=None) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), bg if bg is not None else (0, 0, 0, 0))
    side = max(1, int(round(size * scale)))
    logo = mark.copy()
    logo.thumbnail((side, side), Image.Resampling.LANCZOS)
    ox = (size - logo.size[0]) // 2
    oy = (size - logo.size[1]) // 2
    canvas.paste(logo, (ox, oy), logo)
    return canvas


def main() -> None:
    mark = extract_mark(SRC)
    # purpose:any — transparent for OS splash on background_color
    place(mark, 512, 0.72).save(WEB / "icon-512.png", optimize=True)
    place(mark, 192, 0.72).save(WEB / "icon-192.png", optimize=True)
    # purpose:maskable — charcoal for home-screen / apple-touch
    place(mark, 512, 0.62, CHAR).convert("RGB").save(WEB / "icon-512-maskable.png", optimize=True)
    place(mark, 192, 0.62, CHAR).convert("RGB").save(WEB / "icon-192-maskable.png", optimize=True)
    place(mark, 512, 0.72).save(WEB / "splash-m.png", optimize=True)
    print("any=transparent maskable=#2D2D2D")


if __name__ == "__main__":
    main()
