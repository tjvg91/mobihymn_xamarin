"""Generate 1200x630 Open Graph share preview image."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

WEB = Path(__file__).resolve().parents[1] / "MobiHymn4.Web" / "wwwroot"
SRC = Image.open(WEB / "icon-512.png").convert("RGBA")


def extract_mark(im: Image.Image) -> Image.Image:
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


def main() -> None:
    w, h = 1200, 630
    canvas = Image.new("RGBA", (w, h), (45, 45, 45, 255))

    mark = extract_mark(SRC)
    mark.thumbnail((280, 280), Image.Resampling.LANCZOS)
    mx = (w - mark.size[0]) // 2
    my = 110
    canvas.paste(mark, (mx, my), mark)

    try:
        font = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 64)
        sub = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 28)
    except OSError:
        font = ImageFont.load_default()
        sub = font

    draw = ImageDraw.Draw(canvas)
    title = "MobiHymn"
    tw = draw.textbbox((0, 0), title, font=font)
    tx = (w - (tw[2] - tw[0])) // 2
    ty = my + mark.size[1] + 28
    draw.text((tx, ty), title, fill=(255, 255, 255, 255), font=font)

    subtitle = "Your pocket hymn companion"
    sw = draw.textbbox((0, 0), subtitle, font=sub)
    sx = (w - (sw[2] - sw[0])) // 2
    draw.text((sx, ty + 72), subtitle, fill=(245, 210, 0, 230), font=sub)

    out_path = WEB / "og-image.png"
    canvas.convert("RGB").save(out_path, optimize=True)
    print(f"wrote {out_path} {Image.open(out_path).size}")


if __name__ == "__main__":
    main()
