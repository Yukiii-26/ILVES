#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""ILVES 主界面美术资产生成管线（可复现、幂等）。

源素材（只读，绝不修改）:
    D:\DeepSeek_工作区\GitHub-3\猞猁小队基础设定集

产出（工程 D:\UnityProjects\ILVES）:
    Assets/Resources/art/title-logo.png   标题艺术字 -> 去黑底透明 PNG，1:1 裁切不缩放
    Assets/Resources/art/title-bg.jpg     主页背景图 -> quality=88 / 4:4:4 重编码，尺寸不变
    Assets/Art/icon.png                   图标.png  -> 1024x1024 RGBA（LANCZOS）
    docs/设定集/assets/title-art.png      原始素材归档（逐字节拷贝，禁止重编码）
    docs/设定集/assets/title-bg.jpg
    docs/设定集/assets/icon.png

幂等性：编码参数全部写死，输出不含时间戳；同一批源素材重复执行 -> 输出字节完全一致。

用法:
    python tools/gen_art_assets.py
    python tools/gen_art_assets.py --src <源目录> --repo <工程根> --report <报告路径>
    python tools/gen_art_assets.py --dry-run      # 只做生成前检查，不写任何文件
"""

from __future__ import annotations

import argparse
import hashlib
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

# ----------------------------------------------------------------------------
# 常量：源目录 / 产物路径 / 编码参数
# ----------------------------------------------------------------------------

SRC_DIR = Path(r"D:\DeepSeek_工作区\GitHub-3\猞猁小队基础设定集")

SRC_TITLE_ART = "标题艺术字.png"
SRC_TITLE_BG = "主页背景图.jpg"
SRC_ICON = "图标.png"

REPO_ROOT = Path(__file__).resolve().parent.parent

REL_TITLE_LOGO = Path("Assets/Resources/art/title-logo.png")
REL_TITLE_BG = Path("Assets/Resources/art/title-bg.jpg")
REL_ICON = Path("Assets/Art/icon.png")
ARCHIVE_DIR = Path("docs/设定集/assets")
ARCHIVE_FILES = {
    "title-art.png": SRC_TITLE_ART,
    "title-bg.jpg": SRC_TITLE_BG,
    "icon.png": SRC_ICON,
}

# 标题艺术字：luma>8 求内容包围盒，四边各留 8px；alpha=clamp((luma-3)/19,0,1)
LOGO_BBOX_LUMA = 8.0
LOGO_PAD = 8
ALPHA_LO = 3.0          # luma<=3  -> alpha 0
ALPHA_HI = 22.0         # luma>=22 -> alpha 255
LOGO_EXPECTED_SIZE = (1363, 349)   # 1672x940 源图 + 上述规则得出的固定尺寸

# 主页背景图：q88 + 4:4:4（无子采样），尺寸不变
BG_QUALITY = 88
BG_SUBSAMPLING = 0      # 0 = 4:4:4, 1 = 4:2:2, 2 = 4:2:0
BG_MAX_BYTES = 600 * 1024
BG_EXPECTED_SIZE = (1671, 941)

# 图标：1254x1254 -> 1024x1024
ICON_SIZE = (1024, 1024)

# 验收阈值（标题艺术字）
MIN_TRANSPARENT_RATIO = 0.70    # alpha==0 占比下限
MIN_OPAQUE_RATIO = 0.15         # alpha==255 占比下限
MAX_INTERMEDIATE_RATIO = 0.05   # 0<alpha<255 占比上限
MIN_INTERMEDIATE_NEAR_BG = 0.90 # 半透明像素中位于背景 3px 内的比例下限（无灰雾）

# 背景编码候选（用于报告对比，最终采用 BG_QUALITY/BG_SUBSAMPLING）
BG_CANDIDATES = [
    ("q88-4:2:0", 88, 2),
    ("q88-4:4:4", 88, 0),
    ("q82-4:2:0", 82, 2),
]


# ----------------------------------------------------------------------------
# 基础工具
# ----------------------------------------------------------------------------

def md5_of(path: Path) -> str:
    h = hashlib.md5()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def luma_of(rgb: np.ndarray) -> np.ndarray:
    """Rec.709 亮度：0.2126R + 0.7152G + 0.0722B。"""
    a = rgb.astype(np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def content_bbox(luma: np.ndarray, threshold: float, pad: int):
    """返回 (x0, y0, x1, y1)，含端点、已按图像边界裁剪。"""
    ys, xs = np.where(luma > threshold)
    if xs.size == 0:
        raise RuntimeError("源图里没有 luma>%.1f 的内容" % threshold)
    h, w = luma.shape
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(w - 1, int(xs.max()) + pad)
    y1 = min(h - 1, int(ys.max()) + pad)
    return x0, y0, x1, y1


def dilate(mask: np.ndarray, iterations: int = 1) -> np.ndarray:
    """4 邻域膨胀（只用 numpy，避免额外依赖）。"""
    out = mask.copy()
    for _ in range(iterations):
        grown = out.copy()
        grown[1:, :] |= out[:-1, :]
        grown[:-1, :] |= out[1:, :]
        grown[:, 1:] |= out[:, :-1]
        grown[:, :-1] |= out[:, 1:]
        out = grown
    return out


def psnr_db(a: np.ndarray, b: np.ndarray) -> float:
    mse = float(np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2))
    return 99.0 if mse == 0 else 10.0 * np.log10(255.0 * 255.0 / mse)


def fmt_size(n: int) -> str:
    return "%d bytes (%.1f KB / %.3f MB)" % (n, n / 1024.0, n / (1024.0 * 1024.0))


def ascii_safe(text: str) -> str:
    """控制台在 zh-CN 代码页下可能乱码，报告写 UTF-8 文件，stdout 只保证不抛异常。"""
    return text.encode("ascii", "backslashreplace").decode("ascii")


# ----------------------------------------------------------------------------
# 1) 标题艺术字 -> 透明 PNG
# ----------------------------------------------------------------------------

def build_title_logo(src: Path, dst: Path) -> Path:
    im = Image.open(src)
    im.load()
    if im.mode != "RGB":
        im = im.convert("RGB")
    rgb = np.asarray(im)
    luma = luma_of(rgb)
    x0, y0, x1, y1 = content_bbox(luma, LOGO_BBOX_LUMA, LOGO_PAD)

    crop_rgb = rgb[y0:y1 + 1, x0:x1 + 1]
    crop_luma = luma[y0:y1 + 1, x0:x1 + 1]
    alpha = np.rint(
        np.clip((crop_luma - ALPHA_LO) / (ALPHA_HI - ALPHA_LO), 0.0, 1.0) * 255.0
    ).astype(np.uint8)

    if crop_rgb.shape[:2] != alpha.shape:
        raise AssertionError("RGB/alpha 尺寸不一致")
    if crop_rgb.shape[1] != LOGO_EXPECTED_SIZE[0] or crop_rgb.shape[0] != LOGO_EXPECTED_SIZE[1]:
        raise AssertionError(
            "标题艺术字裁切尺寸 %dx%d 与预期 %dx%d 不符（源素材或裁切规则变了）"
            % (crop_rgb.shape[1], crop_rgb.shape[0], *LOGO_EXPECTED_SIZE)
        )

    rgba = np.dstack([crop_rgb, alpha]).astype(np.uint8)
    out = Image.fromarray(rgba, "RGBA")
    dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(dst, format="PNG", optimize=True)
    return dst


# ----------------------------------------------------------------------------
# 2) 主页背景图 -> 重编码 JPEG（尺寸不变）
# ----------------------------------------------------------------------------

def measure_bg_candidates(src: Path, tmpdir: Path):
    """实测各编码候选的体积与保真度，写进报告，证明 subsampling 选择有依据。"""
    im = Image.open(src).convert("RGB")
    ref = np.asarray(im)
    rows = []
    for label, quality, subsampling in BG_CANDIDATES:
        probe = tmpdir / (label.replace(":", "") + ".jpg")
        im.save(probe, format="JPEG", quality=quality, subsampling=subsampling,
                optimize=True, progressive=False)
        rec = np.asarray(Image.open(probe).convert("RGB"))
        rows.append((label, probe.stat().st_size, psnr_db(ref, rec),
                     int(np.abs(ref.astype(np.int16) - rec.astype(np.int16)).max())))
    return rows


def build_title_bg(src: Path, dst: Path, tmpdir: Path):
    im = Image.open(src).convert("RGB")
    if im.size != BG_EXPECTED_SIZE:
        raise AssertionError("背景图尺寸 %s 与预期 %s 不符" % (im.size, BG_EXPECTED_SIZE))
    rows = measure_bg_candidates(src, tmpdir)

    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst, format="JPEG", quality=BG_QUALITY, subsampling=BG_SUBSAMPLING,
            optimize=True, progressive=False)
    size = dst.stat().st_size
    if size >= BG_MAX_BYTES:
        raise AssertionError("title-bg.jpg 体积 %d 超过上限 %d" % (size, BG_MAX_BYTES))
    return dst, rows


# ----------------------------------------------------------------------------
# 3) 图标 -> 1024x1024 RGBA
# ----------------------------------------------------------------------------

def build_icon(src: Path, dst: Path) -> Path:
    im = Image.open(src)
    if im.size != (1254, 1254):
        raise AssertionError("图标源尺寸 %s 与预期 (1254, 1254) 不符" % (im.size,))
    rgb = im.convert("RGB").resize(ICON_SIZE, Image.LANCZOS)
    rgba = rgb.convert("RGBA")          # alpha 全 255
    dst.parent.mkdir(parents=True, exist_ok=True)
    rgba.save(dst, format="PNG", optimize=True)
    return dst


# ----------------------------------------------------------------------------
# 4) 原始素材归档（逐字节拷贝）
# ----------------------------------------------------------------------------

def archive_originals(src_dir: Path, archive_dir: Path):
    archive_dir.mkdir(parents=True, exist_ok=True)
    copied = []
    for dst_name, src_name in ARCHIVE_FILES.items():
        src = src_dir / src_name
        dst = archive_dir / dst_name
        if src.resolve() == dst.resolve():
            raise AssertionError("归档目标与源是同一个文件: %s" % dst)
        dst.write_bytes(src.read_bytes())
        if md5_of(src) != md5_of(dst):
            raise AssertionError("归档拷贝 md5 不一致: %s" % dst_name)
        copied.append((src, dst))
    return copied


# ----------------------------------------------------------------------------
# 5) 自检（重新用 Pillow 打开产物，断言尺寸/模式/质量指标）
# ----------------------------------------------------------------------------

def edge_band_stats(alpha: np.ndarray):
    """沿行扫描：只统计「一侧 alpha==0、另一侧 alpha==255」的过渡段（纯边缘 AA 带）。

    斜边/掠射扫描线会因几何原因出现更长的段，故同时给出中位数与分位数。
    """
    def runs_along(arr):
        widths = []
        for row in arr:
            n = row.size
            i = 0
            while i < n:
                if 0 < row[i] < 255:
                    j = i
                    while j < n and 0 < row[j] < 255:
                        j += 1
                    left = row[i - 1] if i > 0 else 0
                    right = row[j] if j < n else 0
                    if (left == 0 and right == 255) or (left == 255 and right == 0):
                        widths.append(j - i)
                    i = j
                else:
                    i += 1
        return widths

    h = runs_along(alpha)
    v = runs_along(alpha.T)
    return np.asarray(h + v, dtype=np.int32)


def verify_logo(path: Path, lines):
    im = Image.open(path)
    im.load()
    if im.size != LOGO_EXPECTED_SIZE:
        raise AssertionError("title-logo.png 尺寸 %s != %s" % (im.size, LOGO_EXPECTED_SIZE))
    if im.mode != "RGBA":
        raise AssertionError("title-logo.png 模式 %s != RGBA" % im.mode)
    arr = np.asarray(im)
    alpha = arr[..., 3]
    total = alpha.size
    n_zero = int((alpha == 0).sum())
    n_full = int((alpha == 255).sum())
    inter = (alpha > 0) & (alpha < 255)
    n_inter = int(inter.sum())

    zero = alpha == 0
    near2 = inter & dilate(zero, 2)
    near3 = inter & dilate(zero, 3)
    interior = inter & ~near3

    ratios = (n_zero / total, n_full / total, n_inter / total)
    if ratios[0] < MIN_TRANSPARENT_RATIO:
        raise AssertionError("alpha==0 占比 %.2f%% 过低（黑底没全透明）" % (100 * ratios[0]))
    if ratios[1] < MIN_OPAQUE_RATIO:
        raise AssertionError("alpha==255 占比 %.2f%% 过低（字干不实）" % (100 * ratios[1]))
    if ratios[2] > MAX_INTERMEDIATE_RATIO:
        raise AssertionError("半透明像素占比 %.2f%% 过高（疑似灰雾）" % (100 * ratios[2]))
    if near3.sum() / max(1, n_inter) < MIN_INTERMEDIATE_NEAR_BG:
        raise AssertionError("半透明像素过度分散，疑似光晕")
    # 四周 8px padding 必须是全透明
    for name, edge in (("top", alpha[0, :]), ("bottom", alpha[-1, :]),
                       ("left", alpha[:, 0]), ("right", alpha[:, -1])):
        if int(edge.max()) != 0:
            raise AssertionError("title-logo.png %s 边界不透明" % name)
    if int(alpha.max()) != 255:
        raise AssertionError("没有全不透明像素")

    band = edge_band_stats(alpha)
    med = float(np.median(band))
    mean = float(band.mean())
    p90 = float(np.percentile(band, 90))
    p99 = float(np.percentile(band, 99))
    if med > 2.0:
        raise AssertionError("边缘过渡带中位宽度 %.2fpx 过宽（>2px）" % med)

    lines.append("  size=%dx%d mode=%s bytes=%s" % (im.size[0], im.size[1], im.mode,
                                                    fmt_size(path.stat().st_size)))
    lines.append("  alpha==0   : %8d px  %.4f%%" % (n_zero, 100 * ratios[0]))
    lines.append("  alpha==255 : %8d px  %.4f%%" % (n_full, 100 * ratios[1]))
    lines.append("  0<a<255    : %8d px  %.4f%%  (near bg<=2px %.1f%%, <=3px %.1f%%)"
                 % (n_inter, 100 * ratios[2],
                    100 * near2.sum() / max(1, n_inter), 100 * near3.sum() / max(1, n_inter)))
    lines.append("  interior semi-transparent (source dark veins/trees, NOT halo): %d px (%.3f%%)"
                 % (int(interior.sum()), 100 * interior.sum() / total))
    lines.append("  edge transition band (bounded by 0 and 255): n=%d median=%.0fpx mean=%.2fpx "
                 "p90=%.0fpx p99=%.0fpx max=%.0fpx"
                 % (band.size, med, mean, p90, p99, float(band.max())))
    return im


def verify_bg(path: Path, lines):
    im = Image.open(path)
    im.load()
    if im.size != BG_EXPECTED_SIZE:
        raise AssertionError("title-bg.jpg 尺寸 %s != %s" % (im.size, BG_EXPECTED_SIZE))
    if im.mode != "RGB":
        raise AssertionError("title-bg.jpg 模式 %s != RGB" % im.mode)
    if path.stat().st_size >= BG_MAX_BYTES:
        raise AssertionError("title-bg.jpg 体积超限")
    layer = getattr(im, "layer", None)
    # 断言确实写成了 4:4:4（JPEG 各分量采样因子 1x1；4:2:0 会是 Y=2x2）
    if layer and len(layer[0]) >= 3 and (layer[0][1] != 1 or layer[0][2] != 1):
        raise AssertionError("title-bg.jpg 不是 4:4:4: layer=%s" % (layer,))
    lines.append("  size=%dx%d mode=%s bytes=%s" % (im.size[0], im.size[1], im.mode,
                                                    fmt_size(path.stat().st_size)))
    lines.append("  encoder: quality=%d subsampling=%d (4:4:4) optimize=True progressive=False; "
                 "jpeg layer=%s" % (BG_QUALITY, BG_SUBSAMPLING, layer))
    return im


def verify_icon(path: Path, lines):
    im = Image.open(path)
    im.load()
    if im.size != ICON_SIZE:
        raise AssertionError("icon.png 尺寸 %s != %s" % (im.size, ICON_SIZE))
    if im.mode != "RGBA":
        raise AssertionError("icon.png 模式 %s != RGBA" % im.mode)
    alpha = np.asarray(im)[..., 3]
    if int(alpha.min()) != 255 or int(alpha.max()) != 255:
        raise AssertionError("icon.png 存在非 255 alpha")
    lines.append("  size=%dx%d mode=%s bytes=%s alpha_range=[%d,%d]"
                 % (im.size[0], im.size[1], im.mode, fmt_size(path.stat().st_size),
                    int(alpha.min()), int(alpha.max())))
    return im


def verify_archive(repo: Path, src_dir: Path, lines):
    for dst_name, src_name in ARCHIVE_FILES.items():
        src = src_dir / src_name
        dst = repo / ARCHIVE_DIR / dst_name
        if not dst.is_file():
            raise AssertionError("归档缺失: %s" % dst)
        if md5_of(src) != md5_of(dst):
            raise AssertionError("归档与源不同字节: %s" % dst_name)
        lines.append("  %-18s -> %s  %s  md5=%s"
                     % (src_name, dst, fmt_size(dst.stat().st_size), md5_of(dst)))


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------

def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="ILVES 主界面美术资产生成管线")
    parser.add_argument("--src", default=str(SRC_DIR), help="源素材目录（只读）")
    parser.add_argument("--repo", default=str(REPO_ROOT), help="ILVES 工程根目录")
    parser.add_argument("--report", default=str(Path(tempfile.gettempdir()) / "ilves_art_report.txt"),
                        help="UTF-8 报告输出路径")
    parser.add_argument("--dry-run", action="store_true", help="只检查源素材，不写任何文件")
    args = parser.parse_args(argv)

    src_dir = Path(args.src)
    repo = Path(args.repo)
    report_path = Path(args.report)

    for name in ARCHIVE_FILES.values():
        p = src_dir / name
        if not p.is_file():
            raise FileNotFoundError("源素材缺失: %s" % p)
        if p.stat().st_size == 0:
            raise AssertionError("源素材为空: %s" % p)

    if args.dry_run:
        print("[dry-run] 源素材检查通过: %s" % ascii_safe(str(src_dir)))
        return 0

    title_logo = repo / REL_TITLE_LOGO
    title_bg = repo / REL_TITLE_BG
    icon = repo / REL_ICON
    archive_dir = repo / ARCHIVE_DIR

    lines = []
    lines.append("=" * 78)
    lines.append("ILVES 主界面美术资产生成报告（由 tools/gen_art_assets.py 生成）")
    lines.append("源素材: %s（只读）" % src_dir)
    lines.append("工程根: %s" % repo)
    lines.append("=" * 78)

    # --- 生成 ---
    build_title_logo(src_dir / SRC_TITLE_ART, title_logo)
    print("[build] title-logo.png")
    with tempfile.TemporaryDirectory(prefix="ilves_gen_") as td:
        title_bg_path, cand_rows = build_title_bg(src_dir / SRC_TITLE_BG, title_bg, Path(td))
    print("[build] title-bg.jpg")
    build_icon(src_dir / SRC_ICON, icon)
    print("[build] icon.png")
    copied = archive_originals(src_dir, archive_dir)
    print("[build] archive x%d" % len(copied))

    # --- 1) 产物自检 ---
    lines.append("")
    lines.append("--- 1) 产物自检（重新用 Pillow 打开）---")
    lines.append("[title-logo] %s" % title_logo)
    verify_logo(title_logo, lines)
    lines.append("  md5=%s" % md5_of(title_logo))
    lines.append("[title-bg] %s" % title_bg_path)
    verify_bg(title_bg_path, lines)
    lines.append("  md5=%s" % md5_of(title_bg_path))
    lines.append("[icon] %s" % icon)
    verify_icon(icon, lines)
    lines.append("  md5=%s" % md5_of(icon))

    # --- 2) 背景编码候选对比 ---
    lines.append("")
    lines.append("--- 2) title-bg.jpg 编码候选实测（PSNR 对源图解码结果）---")
    lines.append("  %-10s %10s %8s %8s" % ("candidate", "bytes", "psnr_dB", "maxerr"))
    for label, size, psnr, maxerr in cand_rows:
        lines.append("  %-10s %10d %8.2f %8d" % (label, size, psnr, maxerr))
    lines.append("  采用: %s（体积 %s，占 600KB 预算 %.1f%%）"
                 % ("q%d-4:4:4" % BG_QUALITY,
                    fmt_size(title_bg_path.stat().st_size),
                    100.0 * title_bg_path.stat().st_size / BG_MAX_BYTES))

    # --- 3) 归档校验 ---
    lines.append("")
    lines.append("--- 3) 原始素材归档（逐字节，md5 已核对）---")
    verify_archive(repo, src_dir, lines)

    # --- 4) 汇总 ---
    lines.append("")
    lines.append("--- 4) 产物清单 ---")
    for rel in (REL_TITLE_LOGO, REL_TITLE_BG, REL_ICON):
        p = repo / rel
        with Image.open(p) as im:
            lines.append("  %-40s %4dx%-4d %-5s %s"
                         % (rel.as_posix(), im.width, im.height, im.mode, fmt_size(p.stat().st_size)))
    for dst_name in ARCHIVE_FILES:
        p = archive_dir / dst_name
        with Image.open(p) as im:
            lines.append("  %-40s %4dx%-4d %-5s %s"
                         % ((ARCHIVE_DIR / dst_name).as_posix(), im.width, im.height,
                            im.mode, fmt_size(p.stat().st_size)))
    lines.append("")
    lines.append("OK：全部断言通过。")

    report = "\n".join(lines) + "\n"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(report, encoding="utf-8")
    print(ascii_safe("\n".join(lines)))
    print("[report] %s" % ascii_safe(str(report_path)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
