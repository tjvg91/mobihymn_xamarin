"""Trace monochrome PNG silhouette to SVG (potrace)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image
from potrace import Bitmap, BezierSegment, CornerSegment


def pt(p) -> tuple[float, float]:
    if hasattr(p, "x"):
        return float(p.x), float(p.y)
    return float(p[0]), float(p[1])


def curve_to_path_d(curve) -> str:
    parts: list[str] = []
    sx, sy = pt(curve.start_point)
    parts.append(f"M {sx:.2f} {sy:.2f}")
    for seg in curve:
        if isinstance(seg, BezierSegment):
            c1x, c1y = pt(seg.c1)
            c2x, c2y = pt(seg.c2)
            ex, ey = pt(seg.end_point)
            parts.append(f"C {c1x:.2f} {c1y:.2f} {c2x:.2f} {c2y:.2f} {ex:.2f} {ey:.2f}")
        elif isinstance(seg, CornerSegment):
            cx, cy = pt(seg.c)
            ex, ey = pt(seg.end_point)
            parts.append(f"L {cx:.2f} {cy:.2f} L {ex:.2f} {ey:.2f}")
        else:
            end = getattr(seg, "end_point", None)
            if end is not None:
                ex, ey = pt(end)
                parts.append(f"L {ex:.2f} {ey:.2f}")
    parts.append("Z")
    return " ".join(parts)


def main() -> None:
    import sys

    root = Path(__file__).resolve().parents[1]
    src = Path(r"C:\Users\TimmyG\Downloads\add-task_12584652.png")
    if len(sys.argv) > 1:
        src = Path(sys.argv[1])
    if not src.is_file():
        raise SystemExit(f"Source PNG not found: {src}")

    # Keep a raster copy for reference / fallbacks.
    png_out = root / "MobiHymn4.Web" / "wwwroot" / "board-plus.png"
    Image.open(src).convert("RGBA").save(png_out, optimize=True)

    im = Image.open(src).convert("RGBA")
    arr = np.array(im)
    ink = (arr[:, :, 3] > 128) & (arr[:, :, 0] < 128)
    bmp = Bitmap(ink)
    curves = list(bmp.trace())

    path_ds = []
    for curve in curves:
        path_ds.append(curve_to_path_d(curve))

    if not path_ds:
        raise SystemExit("No usable paths traced from PNG")

    # Prefer content bounds (ignore a pure full-canvas frame if present).
    ys, xs = np.where(ink)
    min_x, max_x = int(xs.min()), int(xs.max())
    min_y, max_y = int(ys.min()), int(ys.max())
    pad = 4
    min_x = max(0, min_x - pad)
    min_y = max(0, min_y - pad)
    max_x = min(im.width - 1, max_x + pad)
    max_y = min(im.height - 1, max_y + pad)
    bw = max_x - min_x
    bh = max_y - min_y
    size = max(bw, bh)
    ox = min_x - (size - bw) / 2
    oy = min_y - (size - bh) / 2

    # Translate into crop coords via SVG transform on group.
    out = root / "MobiHymn4.Web" / "wwwroot" / "board-plus.svg"
    compound = " ".join(path_ds)
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" '
        f'fill="currentColor" fill-rule="evenodd" aria-hidden="true">\n'
        f'  <g transform="translate({-ox:.2f},{-oy:.2f})">\n'
        f'    <path d="{compound}"/>\n'
        f"  </g>\n"
        f"</svg>\n"
    )
    out.write_text(svg, encoding="utf-8")
    print(f"wrote {out} curves={len(curves)} kept={len(path_ds)} viewBox=0 0 {size} {size}")


if __name__ == "__main__":
    main()
