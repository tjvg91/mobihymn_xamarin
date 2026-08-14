"""Regenerate PWA icons: transparent 'any' + charcoal maskable (matches splash).

Android Chrome prefers maskable icons for the installed-app splash. If maskable
uses a pure-black plate while background_color is #2D2D2D, users see a black
squircle around the M until the HTML splash paints. Maskable must match
background_color so the plate disappears into the splash.
"""
from pathlib import Path

from PIL import Image

WEB = Path(__file__).resolve().parents[1] / "MobiHymn4.Web" / "wwwroot"
# Match manifest background_color / theme_color — not pure black.
ICON_BG = (0x2D, 0x2D, 0x2D, 255)

_SRC_CANDIDATES = ("logo-mark.png", "logo.png", "splash-m.png", "icon-512.png")
SRC = next(
    Image.open(WEB / name).convert("RGBA")
    for name in _SRC_CANDIDATES
    if (WEB / name).exists()
)


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
    # purpose:any — transparent for OS splash compositing on background_color
    place(mark, 512, 0.72).save(WEB / "icon-512.png", optimize=True)
    place(mark, 192, 0.72).save(WEB / "icon-192.png", optimize=True)
    # purpose:maskable — same charcoal as splash so Chrome's preferred splash icon
    # does not flash a black plate. Safe-zone scale ~0.48.
    place(mark, 512, 0.48, ICON_BG).convert("RGB").save(WEB / "icon-512-maskable.png", optimize=True)
    place(mark, 192, 0.48, ICON_BG).convert("RGB").save(WEB / "icon-192-maskable.png", optimize=True)
    place(mark, 512, 0.72).save(WEB / "splash-m.png", optimize=True)
    print("any=transparent maskable=#2D2D2D")


if __name__ == "__main__":
    main()
