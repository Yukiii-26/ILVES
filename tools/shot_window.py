#!/usr/bin/env python3
"""只截游戏窗口（客户区），并且**只有在确认目标窗口处于最上层时才截**。

安全规则：任何采样点若被别的窗口盖住，就先把目标窗口提到最前重试；多次失败则
直接报错退出（exit 3），绝不退化成「抓到桌面/别的应用」——历史上整屏截图曾把
用户桌面发到公开仓库。
"""
import argparse, ctypes, sys, time
from ctypes import wintypes
from PIL import ImageGrab

user32 = ctypes.windll.user32

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try:
        user32.SetProcessDPIAware()
    except Exception:
        pass

WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_SHOWWINDOW = 0x0040
HWND_TOP = 0


def iter_windows():
    out = []
    def cb(h, l):
        if user32.IsWindowVisible(h):
            out.append(h)
        return True
    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return out


def window_pid(h):
    p = wintypes.DWORD()
    user32.GetWindowThreadProcessId(h, ctypes.byref(p))
    return p.value


def window_title(h):
    n = user32.GetWindowTextLengthW(h)
    if n <= 0:
        return ""
    b = ctypes.create_unicode_buffer(n + 1)
    user32.GetWindowTextW(h, b, n + 1)
    return b.value


def client_rect(h):
    rc = wintypes.RECT()
    user32.GetClientRect(h, ctypes.byref(rc))
    return rc.right - rc.left, rc.bottom - rc.top


def client_origin_on_screen(h):
    pt = wintypes.POINT(0, 0)
    user32.ClientToScreen(h, ctypes.byref(pt))
    return pt.x, pt.y


def root_of(h):
    return user32.GetAncestor(h, 2)  # GA_ROOT


def sample_points(x, y, w, h):
    pts = [(x + w // 2, y + h // 2),
           (x + w // 6, y + h // 6), (x + w * 5 // 6, y + h // 6),
           (x + w // 6, y + h * 5 // 6), (x + w * 5 // 6, y + h * 5 // 6),
           (x + w // 2, y + h // 6), (x + w // 2, y + h * 5 // 6)]
    return pts


def all_points_on(hwnd, x, y, w, h):
    target = root_of(hwnd) or hwnd
    for (px, py) in sample_points(x, y, w, h):
        top = user32.WindowFromPoint(wintypes.POINT(px, py))
        if not top:
            return False, (px, py, 0)
        if (root_of(top) or top) != target:
            return False, (px, py, top)
    return True, None


def raise_window(hwnd):
    user32.ShowWindow(hwnd, 9)                      # SW_RESTORE
    user32.SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW)
    user32.BringWindowToTop(hwnd)
    user32.SetForegroundWindow(hwnd)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pid", type=int, default=0)
    ap.add_argument("--title", default="")
    ap.add_argument("--out", required=True)
    ap.add_argument("--activate", action="store_true")
    ap.add_argument("--attempts", type=int, default=6)
    a = ap.parse_args()

    target = None
    for hwnd in iter_windows():
        if a.pid and window_pid(hwnd) != a.pid:
            continue
        if a.title and a.title.lower() not in window_title(hwnd).lower():
            continue
        w, h = client_rect(hwnd)
        if w < 200 or h < 150:
            continue
        if target is None or w * h > target[1] * target[2]:
            target = (hwnd, w, h)
    if target is None:
        print("ERROR: 找不到匹配的窗口 (pid=%s title=%s)" % (a.pid, a.title))
        return 2

    hwnd = target[0]
    for attempt in range(a.attempts):
        x, y = client_origin_on_screen(hwnd)
        w, h = client_rect(hwnd)
        ok, bad = all_points_on(hwnd, x, y, w, h)
        if ok:
            break
        if not a.activate:
            print("ERROR: 窗口被其它窗口遮挡（窗口未置顶）。未截图。遮挡点=%s" % (bad,))
            return 4
        raise_window(hwnd)
        time.sleep(0.5)
    else:
        print("ERROR: 尝试 %d 次仍无法把游戏窗口提到最前，为避免截到其它窗口，未截图。窗口可能被全屏应用或系统弹窗占住。" % a.attempts)
        return 3

    x, y = client_origin_on_screen(hwnd)
    w, h = client_rect(hwnd)
    ok, bad = all_points_on(hwnd, x, y, w, h)
    if not ok:
        print("ERROR: 截图前复查失败，未截图。遮挡点=%s" % (bad,))
        return 5
    img = ImageGrab.grab(bbox=(x, y, x + w, y + h), all_screens=True)
    img.save(a.out)
    print("OK title=%r pid=%s rect=(%d,%d,%d,%d) size=%dx%d out=%s"
          % (window_title(hwnd), window_pid(hwnd), x, y, w, h, img.width, img.height, a.out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
