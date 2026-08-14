"""Build transparent yellow-M splash icons for TWA Android 12 system splash."""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "MobiHymn4.Web" / "wwwroot"
RES = ROOT / "tools" / "android-twa" / "app" / "src" / "main" / "res"
BRAND = (245, 210, 0, 255)
# dpi → px for ~288dp splash icon (safe for Android 12 splash)
SIZES = {
    "drawable-mdpi": 192,
    "drawable-hdpi": 288,
    "drawable-xhdpi": 384,
    "drawable-xxhdpi": 576,
    "drawable-xxxhdpi": 768,
}


def extract_yellow(im: Image.Image) -> Image.Image:
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sp, op = im.load(), out.load()
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            r, g, b, a = sp[x, y]
            if a < 16:
                continue
            if r > 180 and g > 140 and b < 120 and (r + g) > (b * 3):
                op[x, y] = BRAND
    bbox = out.getbbox()
    return out.crop(bbox) if bbox else out


def place(mark: Image.Image, size: int, scale: float = 0.72) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    logo = mark.copy()
    logo.thumbnail((max(1, int(size * scale)), max(1, int(size * scale))), Image.Resampling.LANCZOS)
    logo = extract_yellow(logo)
    ox = (size - logo.size[0]) // 2
    oy = (size - logo.size[1]) // 2
    canvas.paste(logo, (ox, oy), logo)
    return canvas


def main() -> None:
    src = Image.open(WEB / "splash-m.png").convert("RGBA")
    mark = extract_yellow(src)
    for folder, size in SIZES.items():
        dest_dir = RES / folder
        dest_dir.mkdir(parents=True, exist_ok=True)
        icon = place(mark, size)
        icon.save(dest_dir / "splash_icon.png", optimize=True)
        print("wrote", dest_dir / "splash_icon.png", size)
    # Adaptive icon background: charcoal so Android 12 system splash matches.
    colors = RES / "values" / "colors.xml"
    text = colors.read_text(encoding="utf-8") if colors.exists() else '<?xml version="1.0" encoding="utf-8"?>\n<resources>\n</resources>\n'
    if "splash_icon_background" not in text:
        text = text.replace(
            "</resources>",
            '    <color name="splash_icon_background">#2D2D2D</color>\n</resources>',
        )
        colors.write_text(text, encoding="utf-8")
        print("added splash_icon_background color")


if __name__ == "__main__":
    main()
