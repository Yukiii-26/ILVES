#!/usr/bin/env python3
"""在游戏窗口客户区里点击（用于翻页/弹窗验收截图）。坐标是 1280x720 设计坐标，自动按窗口实际尺寸缩放。"""
import argparse, ctypes, sys, time
from ctypes import wintypes

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try: user32.SetProcessDPIAware()
    except Exception: pass

WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004

def windows():
    out = []
    def cb(h, l):
        if user32.IsWindowVisible(h): out.append(h)
        return True
    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return out

def pid_of(h):
    p = wintypes.DWORD(); user32.GetWindowThreadProcessId(h, ctypes.byref(p)); return p.value

def client(h):
    rc = wintypes.RECT(); user32.GetClientRect(h, ctypes.byref(rc))
    pt = wintypes.POINT(0, 0); user32.ClientToScreen(h, ctypes.byref(pt))
    return pt.x, pt.y, rc.right, rc.bottom

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--x", type=float, required=True)
    ap.add_argument("--y", type=float, required=True)
    ap.add_argument("--count", type=int, default=1)
    ap.add_argument("--gap", type=float, default=0.6)
    a = ap.parse_args()
    best = None
    for h in windows():
        if pid_of(h) != a.pid: continue
        x, y, w, hh = client(h)
        if w < 200 or hh < 150: continue
        if best is None or w * hh > best[1] * best[2]: best = (h, w, hh, x, y)
    if best is None:
        print("ERROR window not found"); return 2
    h, w, hh, cx, cy = best
    sx = w / 1280.0; sy = hh / 720.0
    px = int(cx + a.x * sx); py = int(cy + a.y * sy)
    user32.SetForegroundWindow(h)
    time.sleep(0.2)
    for i in range(a.count):
        user32.SetCursorPos(px, py)
        time.sleep(0.12)
        user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        time.sleep(0.06)
        user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        time.sleep(a.gap)
    print("OK clicks=%d at client(%d,%d) window=%dx%d" % (a.count, px, py, w, hh))
    return 0

if __name__ == "__main__":
    sys.exit(main())
