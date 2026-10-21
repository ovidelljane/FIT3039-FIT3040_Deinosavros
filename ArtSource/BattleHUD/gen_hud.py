"""Generate the combat bottom-HUD SVGs.

Usage: python gen_hud.py <svg_out_dir>

Arc sprites (HUD_ArcBand, HUD_ArcRim) are ARC_W x ARC_H with the circle centre at the
bottom-middle, so Unity's Radial 180 fill (origin Bottom) pivots on the circle centre.
If you change A_LEFT / A_RIGHT / radii, update the matching constants in
Assets/Scripts/UI/CombatBottomHud.cs.
"""
import math, os, sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

ARC_W, ARC_H = 1800, 900
A_LEFT, A_RIGHT = 125, 55   # band spans 125 deg (left end) to 55 deg (right end)
R_IN, R_OUT = 844, 892      # band thickness 48px

DOME_R = 930                # dome circle shares the arc's centre
DOME_HALF = 600             # half-width of the dome's flat sides
DOME_W, DOME_H = 2 * DOME_HALF + 20, 420


def f(v):
    return f"{v:.2f}"


def pt(cx, cy, r, deg):
    a = math.radians(deg)
    return cx + r * math.cos(a), cy - r * math.sin(a)


def sector(cx, cy, r_in, r_out, a_left, a_right):
    x0, y0 = pt(cx, cy, r_out, a_left); x1, y1 = pt(cx, cy, r_out, a_right)
    x2, y2 = pt(cx, cy, r_in, a_right); x3, y3 = pt(cx, cy, r_in, a_left)
    return (f"M{f(x0)},{f(y0)} A{r_out},{r_out} 0 0 1 {f(x1)},{f(y1)} "
            f"L{f(x2)},{f(y2)} A{r_in},{r_in} 0 0 0 {f(x3)},{f(y3)} Z")


def svg(w, h, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}">\n{body}\n</svg>\n')


def write(name, content):
    with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as fh:
        fh.write(content)


ACX, ACY = ARC_W / 2, ARC_H
BAND = sector(ACX, ACY, R_IN, R_OUT, A_LEFT, A_RIGHT)

# --- Dome: cropped cap; its circle centre sits DOME_R below the sprite top -----------
dcx, dcy = DOME_W / 2, DOME_R


def cap(r, half, inset):
    dy = math.sqrt(r * r - half * half)
    xl, xr = dcx - half + inset, dcx + half - inset
    return (f"M{f(xl)},{DOME_H} L{f(xl)},{f(dcy - dy)} A{r},{r} 0 0 1 {f(xr)},{f(dcy - dy)} "
            f"L{f(xr)},{DOME_H} Z")


write("HUD_Dome", svg(DOME_W, DOME_H, f'''  <defs>
    <radialGradient id="body" cx="{dcx}" cy="{dcy}" r="{DOME_R}" gradientUnits="userSpaceOnUse">
      <stop offset="0.72" stop-color="#4f2a10"/>
      <stop offset="0.95" stop-color="#7a431b"/>
      <stop offset="1" stop-color="#8a4f22"/>
    </radialGradient>
  </defs>
  <path d="{cap(DOME_R, DOME_HALF, 0)}" fill="#2b1608"/>
  <path d="{cap(DOME_R - 18, DOME_HALF - 18, 0)}" fill="url(#body)"/>
  <path d="{sector(dcx, dcy, R_IN - 10, R_OUT + 10, A_LEFT + 1.5, A_RIGHT - 1.5)}" fill="#1c0e05" opacity="0.85"/>
  <path d="{cap(DOME_R - 18, DOME_HALF - 18, 0)}" fill="none" stroke="#b8874a" stroke-width="3" opacity="0.55"/>'''))

# --- Arc band: white, tintable; same sprite for track, health, shield and cost preview -
write("HUD_ArcBand", svg(ARC_W, ARC_H, f'''  <defs>
    <radialGradient id="shade" cx="{ACX}" cy="{ACY}" r="{R_OUT}" gradientUnits="userSpaceOnUse">
      <stop offset="{R_IN / R_OUT:.4f}" stop-color="#c8c8c8"/>
      <stop offset="0.98" stop-color="#ffffff"/>
      <stop offset="1" stop-color="#e6e6e6"/>
    </radialGradient>
  </defs>
  <path d="{BAND}" fill="url(#shade)"/>'''))

# --- Arc rim: gold outline drawn over the band ---------------------------------------
write("HUD_ArcRim", svg(ARC_W, ARC_H, f'''  <path d="{BAND}" fill="none" stroke="#1c0e05" stroke-width="7" stroke-linejoin="round"/>
  <path d="{BAND}" fill="none" stroke="#b8874a" stroke-width="3" stroke-linejoin="round"/>'''))

# --- Bar: white pill, 9-sliced (borders 20 on every side) -----------------------------
write("HUD_Bar", svg(96, 40, '''  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffffff"/>
      <stop offset="1" stop-color="#cfcfcf"/>
    </linearGradient>
  </defs>
  <rect x="0" y="0" width="96" height="40" rx="20" fill="url(#g)"/>
  <rect x="6" y="5" width="84" height="9" rx="4.5" fill="#ffffff" opacity="0.45"/>'''))

# --- Bar frame: dark outline with gold edge, 9-sliced, sits around the bar track ------
write("HUD_BarFrame", svg(104, 48, '''  <rect x="2" y="2" width="100" height="44" rx="22" fill="none" stroke="#1c0e05" stroke-width="4"/>
  <rect x="4.5" y="4.5" width="95" height="39" rx="19.5" fill="none" stroke="#b8874a" stroke-width="2"/>'''))

# Heart, Shield and ElixirDrop icons are hand-painted art in Assets/Resources/BattleHUD.

print("ok")
