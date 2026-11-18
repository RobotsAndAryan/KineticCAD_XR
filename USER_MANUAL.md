# KineticCAD XR — Interactive 3D CAD Studio Manual (Fusion 360 for Meta Quest 3)

**KineticCAD XR** is an interactive, controller-free **3D Spatial CAD & 3D-Printing Studio** for **Meta Quest 3 Mixed Reality** (and Unity Desktop Editor). It uses familiar **Autodesk Fusion 360** terminology (`Create`, `Extrude / Pull`, `Rotate / Tilt`, `Move 3D`, `Combine: Join / Cut Hole / Intersect`, `Modify: Shell / Chamfer`, `Pattern`, `Physical Material`, `Hold 1:1 Model`, `Reset Bodies`, and `Export .STL / .OBJ`) with full **6-DoF CAD manipulation** inside an expanded **720 mm × 540 mm** desk workbench and shape dimensions up to **900 mm × 900 mm × 900 mm**.

---

## 1. Quick-Start Layout: What You See in Front of You

1. **Center 3D CAD Workbench & Active 3D Solid (`720 mm × 540 mm` Desk Platform):**
   - Right in the center of your desk sits your live **1:1 3D CAD Model**, controlled by **7 direct spatial handles**:
   - **3 Direct Extrude / Pull Handles (`10 mm` to `900 mm`):**
     - **Coral Red (`+X WIDTH`):** Drag left/right to resize Width (`10 mm` – `900 mm`).
     - **Emerald Green (`+Y HEIGHT`):** Drag up/down to pull Height (`10 mm` – `900 mm`).
     - **Sky Blue (`+Z DEPTH`):** Drag forward/back to pull Depth (`10 mm` – `900 mm`).
   - **3 Direct 3D Angular Tilt / Rotation Handles (`-180°` to `+180°` in `5°` ticks):**
     - **Amber Orange (`TILT X°` Pitch):** Drag to tilt forward/backward around the X-axis.
     - **Neon Violet (`TILT Y°` Yaw):** Drag to rotate left/right around the vertical Y-axis.
     - **Electric Gold (`TILT Z°` Roll):** Drag to bank/roll side-to-side around the Z-axis.
   - **1 Direct 3D Translation Handle (`MOVE 3D (X/Y/Z)` — Teal Cube):**
     - Drag anywhere in 3D space (`X: [-300, +300] mm`, `Y: [-75, +330] mm`, `Z: [-270, +270] mm`) in `2 mm` snaps to position a new shape before joining or cutting it onto your model.
   - An **8 mm Cyan Fingertip Reticle Dot** shows your active right index pointing position (`2.4 cm` poke radius with `0.85 s` cooldown and automatic pull-back release latch so buttons never double-trigger).

2. **Left-Hand Fusion 360 Tool Palette (`LeftHand_SpatialFusion_Palette` — Palm-Facing Lock):**
   - **Palm Facing Camera (`dot > 0.35`):** Hold your Left Palm toward your headset to smoothly bring the floating **6-Button Fusion 360 Palette** (with its spinning 3D holographic primitive preview above the top edge) wherever you want it.
   - **Turn Palm Away / Drop Hand:** The palette **freezes completely still in 3D space**, giving you a rock-steady surface to poke with your right index finger without hand jitter!
   - **6 Essential Palette Buttons:**
     1. **`CREATE: SHAPE (12 CAD Shapes)`** — Cycle all 12 parametric engineering solids.
     2. **`+ADD / CUT BODY (Place on Model)`** — Stamp/combine the current shape (with its dimensions, 3D offset, and 3D tilt angles) onto your multi-body model.
     3. **`COMBINE MODE (Join / Cut Hole)`** — Switch between `Join (Add Solid)`, `Cut (Hole / Subtract)`, and `Intersect`.
     4. **`MODIFY: SHELL (Hollow / Chamfer)`** — Cycle `Solid Extrude`, `Shell (2.5mm Hollow)`, `Chamfer + M4 Bosses`, and `L-Bracket Ribbed`.
     5. **`PATTERN (1x / Linear / Circ)`** — Repeat bodies in `Single (1x)`, `Linear Pattern (3x)`, or `Circular Pattern (6x)`.
     6. **`MATERIAL (PLA / CF / Al / Ti)`** — Update live density, color, mass (`g`), and estimated 3D print time (`PLA+`, `PETG-CF`, `6061-T6 Al`, `Ti-6Al-4V`).

3. **Front Desk Fusion 360 Toolbar (`VR_Desk_PokeButtonBar` — 4 Essential Buttons):**
   - 4 physical `-5.5 mm` spring-loaded buttons along the front edge of the desk:
     1. **`HOLD 1:1 MODEL (On Right Hand)`** — Detach a 1:1 print preview onto your Right Hand for ergonomic Passthrough inspection.
     2. **`RESET BODIES (Clear Added)`** — Clear all added/cut sub-bodies and reset position/tilt back to a clean base solid.
     3. **`EXPORT .STL / .OBJ (Save & Preview)`** — Export watertight 3D print & CAD files and spawn the in-VR turntable preview.
     4. **`CENTER DESK (Reset View)`** — Re-center the workbench in front of your headset.

4. **Right-Side In-VR Exported 3D Model Gallery Turntable (`ExportedModel_3D_GalleryPedestal`):**
   - Whenever you poke **`EXPORT .STL / .OBJ`**, an emerald-lit **3D Turntable Gallery** spawns on the right side of the desk displaying a live rotating 3D copy of your exported mesh alongside the exact `<robot>_cad.stl` and `<robot>_cad.obj` filenames and Quest 3 / PC folder path.

---

## 2. All 12 Procedural Engineering & Fusion 360 Shapes (Up to `900 mm`)

Every shape renders with custom watertight procedural geometry (`BuildFrustumConeMesh`, `BuildTorusMesh`, `BuildHollowPrismTubeMesh`, `BuildWedgeRampMesh`) including true open through-holes and multi-part fillets/gussets:

| # | Shape Name | Engineering / CAD Geometry |
|---|---|---|
| 1 | **`Box / Cube`** | Precision rectangular prism block |
| 2 | **`Cylinder`** | Radial shaft, spacer, or standoff cylinder |
| 3 | **`Sphere`** | Ball joint or dome primitive |
| 4 | **`Cone / Frustum`** | Tapered conical frustum / nozzle adapter |
| 5 | **`Torus Ring`** | Watertight O-ring / radial collar ring |
| 6 | **`Hollow Tube`** | Outer cylindrical sleeve with true coaxial inner bore (`0.62` inner ratio) |
| 7 | **`Hex Prism (M-Nut)`** | 6-sided metric hex nut / bolt head with true center bore |
| 8 | **`Wedge / 45° Ramp`** | Angled structural gusset / 45° chamfered ramp |
| 9 | **`L-Bracket`** | 90° structural mounting bracket with dual chamfered gusset ribs |
| 10 | **`U-Channel`** | 3-walled structural C/U rail extrusion |
| 11 | **`T-Beam Extrusion`** | Structural T-section flange + center web beam |
| 12 | **`Flanged Bearing`** | Base mounting flange + raised cylindrical boss hub + center bore |

---

## 3. Hold Your 1:1 Model on Your Right Hand (`HOLD 1:1 MODEL`) — Palm Lock

- Poke **`HOLD 1:1 MODEL (On Right Hand)`** on the Front Desk Toolbar (or press **`I`** on Desktop).
- Show your **Right Palm toward the headset camera** to attach the true-scale **1:1 3D Print Preview** (`+5.5 cm` above your right hand) so you can turn it around and test physical fit against real desk objects in Passthrough.
- **Turn your Right Palm away or drop your hand:** the 1:1 model **freezes right where you placed it in mid-air**, so you can walk or lean around to inspect overhangs and through-holes!
- Keep your **Right Hand** separate from your **Left-Hand Fusion 360 Tool Palette** so you can still tweak materials, shapes, and modifications while holding the part.

---

## 4. Export & Inspect `.STL`, `.OBJ`, and `.URDF` Files

When you poke **`EXPORT .STL / .OBJ`** (or press **`U`** on Desktop), KineticCAD XR exports a standalone CAD mesh containing your base model plus every joined/cut/patterned sub-body at its exact 3D offset and tilt:

1. **Inside VR (Right-Side Desk Turntable Gallery):** Look right to inspect your spinning 3D model immediately.
2. **On PC / Unity Editor:** Saved directly in `E:\Unity\KineticCAD_XR\Exports\`:
   - `<robot>_cad.stl` (ASCII STL millimeter units ready for Bambu Studio, PrusaSlicer, Cura)
   - `<robot>_cad.obj` (Multi-group Wavefront OBJ CAD mesh)
   - `<robot>.urdf` & `<robot>_cbf_envelope.json`
3. **On Meta Quest 3 Headset:** Saved to `/sdcard/Android/data/com.RobotsAndAryan.KineticCADXR/files/`. Pull to PC via USB:
   ```powershell
   adb pull /sdcard/Android/data/com.RobotsAndAryan.KineticCADXR/files/ ./Exports/
   ```

---

## 5. Desktop / Unity Editor Keyboard & Mouse Shortcuts

| Input | Action |
|---|---|
| **Left-Click Any 3D Button** | Activates any Desk Toolbar or Left-Hand Palette button |
| **Left-Click & Drag `+X`, `+Y`, `+Z` Spheres** | Directly extrudes Width, Height, or Depth (`10 mm` – `900 mm`) |
| **Left-Click & Drag `TILT X°`, `TILT Y°`, `TILT Z°`** | Directly tilts/rotates the 3D model in Pitch ($X$), Yaw ($Y$), or Roll ($Z$) (`-180°` to `+180°`) |
| **Left-Click & Drag `MOVE 3D (X/Y/Z)`** | Directly translates the active solid in 3D space (`X/Y/Z`) |
| **`W / A / S / D / E / Q`** (or Arrow Keys) | Translates the 3D CAD Model in $X, Y, Z$ (`+Shift` for faster step) |
| **Right-Click + Drag / Scroll** | Orbit / zoom 3D studio camera |
| **`T` or `1`** | **Create Shape:** Cycle 12 CAD shapes |
| **`B` or `2`** | **+Add / Cut Body:** Combine current shape (with its 3D tilt & offset) onto the model |
| **`J` or `3`** | **Combine Mode:** Cycle `Join (Add Solid)` / `Cut (Hole / Subtract)` / `Intersect` |
| **`L` or `4`** | **Pattern:** Cycle `Single (1x)` / `Linear (3x)` / `Circular (6x)` |
| **`X` or `5`** | **Extrude / Pull:** Step through preset 3D dimensions |
| **`V` or `6`** | **Rotate / Tilt 3D:** Step through 3D tilt angles (`Pitch X° / Yaw Y° / Roll Z°`) |
| **`F`** | **Modify:** Cycle `Solid Extrude` / `Shell (2.5mm Hollow)` / `Chamfer + M4 Bosses` / `L-Bracket Ribbed` |
| **`M`** | **Material:** Cycle `PLA+` / `PETG-CF` / `6061-T6 Al` / `Ti-6Al-4V` |
| **`I`** | **Hold 1:1 Model:** Toggle 1:1 preview on your **Right Hand** |
| **`Backspace` or `Delete`** | **Reset Bodies:** Clear all added/cut sub-bodies & reset pose |
| **`U`** | **Export `.STL` / `.OBJ`:** Save 3D print files & display on Right Desk Gallery |
| **`R` or `Space`** | **Center Desk:** Reset view |
