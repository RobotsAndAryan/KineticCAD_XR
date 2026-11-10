# KineticCAD XR — Interactive 3D CAD Studio Manual (Fusion 360 for Meta Quest 3)

**KineticCAD XR** is an interactive, hands-first **3D Spatial CAD & 3D-Printing Studio** for **Meta Quest 3 Mixed Reality** (and Desktop Editor). It uses familiar **Autodesk Fusion 360** terminology (`Create`, `Extrude / Pull`, `Rotate / Tilt`, `Combine: Join / Cut Hole / Intersect`, `Modify: Shell / Chamfer`, `Pattern`, `Physical Material`, `Hold 1:1 Model on Right Hand`, and `Export .STL / .OBJ`) with full **6-DoF CAD manipulation** (3 translations + 3 angular rotations/tilts) and dimensions up to **600 mm × 600 mm × 600 mm**.

---

## 1. Quick-Start Layout: What You See in Front of You

1. **Center 3D CAD Workbench & Active 3D Solid (`_sampleWorkpiece`):**
   - Right in the center of your desk sits your live **1:1 3D CAD Model**.
   - **3 Direct Extrude Handles (`10 mm` to `600 mm`):**
     - **Coral Red (`+X WIDTH`):** Drag left/right to resize Width (`10 mm` – `600 mm`).
     - **Emerald Green (`+Y HEIGHT`):** Drag up/down to pull Height (`10 mm` – `600 mm`).
     - **Sky Blue (`+Z DEPTH`):** Drag forward/back to pull Depth (`10 mm` – `600 mm`).
   - **3 Direct 3D Angular Tilt / Rotation Handles (`-180°` to `+180°`):**
     - **Amber Orange (`TILT X° (Pitch)`):** Drag to tilt forward/backward around the X-axis (`5°` precision increments).
     - **Neon Violet (`TILT Y° (Yaw)`):** Drag to rotate left/right around the vertical Y-axis (`5°` precision increments).
     - **Electric Gold (`TILT Z° (Roll)`):** Drag to bank/roll side-to-side around the Z-axis (`5°` precision increments).
2. **Left-Hand Fusion 360 Tool Palette (`LeftHand_SpatialFusion_Palette`):**
   - Follows your **Left Hand** when held up in view and **always faces directly toward your eyes** (`Quaternion.LookRotation` on a front-facing `-Z` panel).
   - Contains 6 core Fusion 360 buttons (`CREATE: SHAPE`, `+ADD / CUT BODY`, `COMBINE MODE`, `PATTERN`, `EXTRUDE / PULL`, `MATERIAL`) plus a spinning **3D Hologram Shape Preview** above the top edge.
3. **Front Desk Fusion 360 Toolbar (`VR_Desk_PokeButtonBar`):**
   - 7 physical `-5.5 mm` spring-loaded buttons along the front edge of the desk (`CREATE SHAPE`, `+ADD / CUT BODY`, `MODIFY: SHELL`, `ROTATE / TILT`, `HOLD 1:1 MODEL`, `EXPORT .STL / .OBJ`, `CENTER DESK`).
4. **Right-Side In-VR Exported 3D Model Gallery Turntable (`ExportedModel_3D_GalleryPedestal`):**
   - Whenever you poke **`EXPORT .STL / .OBJ`**, an emerald-lit **3D Turntable Gallery** appears on the right side of the desk displaying a live rotating 3D copy of your exported mesh alongside the exact `.stl` and `.obj` filenames and folder path.

---

## 2. All 12 Engineering & Fusion 360 Shapes (Up to `600 mm`)

Poke **`CREATE SHAPE`** on the Desk Toolbar or **`CREATE: SHAPE`** on your Left-Hand Palette to cycle through all **12 parametric CAD shapes**:

| # | Shape Name | Engineering / CAD Geometry |
|---|---|---|
| 1 | **`Box / Cube`** | Precision rectangular prism block |
| 2 | **`Cylinder`** | Radial shaft, spacer, or standoff cylinder |
| 3 | **`Sphere`** | Ball joint or dome primitive |
| 4 | **`Cone / Frustum`** | Tapered conical frustum / nozzle adapter |
| 5 | **`Torus Ring`** | O-ring / radial collar ring |
| 6 | **`Hollow Tube`** | Outer cylindrical sleeve with coaxial inner bore |
| 7 | **`Hex Prism (M-Nut)`** | 6-sided metric hex nut / bolt head with center bore |
| 8 | **`Wedge / 45° Ramp`** | Angled structural gusset / 45° chamfered ramp |
| 9 | **`L-Bracket`** | 90° structural mounting bracket with reinforced gusset ribs |
| 10 | **`U-Channel`** | 3-walled structural C/U rail extrusion |
| 11 | **`T-Beam Extrusion`** | Structural T-section flange + center web beam |
| 12 | **`Flanged Bearing`** | Base mounting flange + raised cylindrical boss hub + center bore |

---

## 3. How to Hold Your 1:1 Model on Your Right Hand (`HOLD 1:1 MODEL`)

- Poke **`HOLD 1:1 MODEL (On Right Hand)`** on the Front Desk Toolbar (or press **`I`** on Desktop).
- A true-scale **1:1 3D Print Preview** attaches directly above your **Right Hand** (`RightWorld + 5.5 cm`) so it never collides with the Fusion 360 Tool Palette on your Left Hand.
- Move and rotate your Right Hand in passthrough to inspect how the physical 3D print will fit against real-world objects on your desk.

---

## 4. How to See & Access Your Exported `.STL` and `.OBJ` 3D Models

When you poke **`EXPORT .STL / .OBJ`** (or press **`U`** on Desktop), KineticCAD XR exports a 100% standalone, centered CAD mesh (with all combined sub-bodies and their 3D translations + 3D tilt angles):

1. **See It Immediately Inside VR (Right-Side Desk Turntable Gallery):**
   - Look at the right side of the virtual desk: the **Exported 3D Model Gallery Turntable** automatically spawns and rotates your exported 3D model, showing the exact filename, dimensions, tilt angles, and save path.
2. **On PC / Unity Editor:**
   - Open the folder:
     `E:\Unity\KineticCAD_XR\Exports\`
   - Inside you will find:
     - `kinetic_cad_3dof_cad.stl` (ASCII STL ready for Bambu Studio, PrusaSlicer, Cura, or Fusion 360)
     - `kinetic_cad_3dof_cad.obj` (Wavefront OBJ 3D mesh)
3. **On Meta Quest 3 Headset (Standalone Android Build):**
   - Files are saved to:
     `/sdcard/Android/data/com.DefaultCompany.KineticCADXR/files/`
   - Connect your Quest 3 to your PC via USB cable and pull the `.stl` and `.obj` files to your desktop with one command:
     ```powershell
     adb pull /sdcard/Android/data/com.DefaultCompany.KineticCADXR/files/ ./Exports/
     ```

---

## 5. Desktop / Unity Editor Keyboard & Mouse Shortcuts

| Input | Action |
|---|---|
| **Left-Click Any 3D Button** | Activates any Desk Toolbar or Left-Hand Palette button |
| **Left-Click & Drag `+X`, `+Y`, `+Z` Spheres** | Directly extrudes Width, Height, or Depth (`10 mm` – `600 mm`) |
| **Left-Click & Drag `TILT X°`, `TILT Y°`, `TILT Z°`** | Directly tilts/rotates the 3D model in Pitch ($X$), Yaw ($Y$), or Roll ($Z$) (`-180°` to `+180°`) |
| **`W / A / S / D / E / Q`** (or Arrow Keys) | Translates the 3D CAD Model in $X, Y, Z$ (`+Shift` for faster step) |
| **Right-Click + Drag / Scroll** | Orbit / zoom 3D studio camera |
| **`T` or `1`** | **Create Shape:** Cycle 12 CAD shapes |
| **`B` or `2`** | **+Add / Cut Body:** Combine current shape (with its 3D tilt & offset) onto the model |
| **`J` or `3`** | **Combine Mode:** Cycle `Join` / `Cut Hole` / `Intersect` |
| **`L` or `4`** | **Pattern:** Cycle `Single (1x)` / `Linear (3x)` / `Circular (6x)` |
| **`X` or `5`** | **Extrude / Pull:** Step through preset 3D dimensions up to `600 mm` |
| **`V` or `6`** | **Rotate / Tilt 3D:** Step through 3D tilt angles (`Pitch X° / Yaw Y° / Roll Z°`) |
| **`F`** | **Modify:** Cycle `Solid Extrude` / `Shell (2.5mm Hollow)` / `Chamfer + M4 Bosses` / `L-Bracket Ribbed` |
| **`M`** | **Material:** Cycle `PLA+` / `PETG-CF` / `6061-T6 Al` / `Ti-6Al-4V` |
| **`I`** | **Hold 1:1 Model:** Toggle 1:1 preview on your **Right Hand** |
| **`U`** | **Export `.STL` / `.OBJ`:** Save 3D print files & display on Right Desk Gallery |
| **`R` or `Space`** | **Center Desk:** Reset view |
