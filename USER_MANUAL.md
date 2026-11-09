# KineticCAD XR — User Manual (Interactive 3D CAD Studio for Meta Quest 3)

**KineticCAD XR** is a hands-first **Interactive 3D CAD Studio ("Spatial Fusion 360")** built for **Meta Quest 3**. Instead of staring at a flat 2D monitor, you measure real-world objects on your desk through **Quest 3 Color Passthrough**, pull 3D faces (`+X WIDTH`, `+Y HEIGHT`, `+Z DEPTH`) directly with your fingertips, combine multi-body shapes (`Join` or `Cut Hole`) from your **Left-Hand Fusion Palette**, hold your 1:1 3D model in your hand, and export watertight **`.stl`** and **`.obj`** files ready for 3D printing.

---

## 1. What You See on Your Desk (Clean 3D CAD Workspace)

When you launch the app on Quest 3 (or press **Play** in Unity), the workspace automatically centers on your desk with **zero robot-arm clutter**:

1. **Center 3D CAD Model (`_sampleWorkpiece`):**
   - Sits right in the center of your desk workbench with 3 glowing **Direct Extrude / Pull Handles** on its faces:
     - **Coral Red Sphere (`+X WIDTH`):** Drag left/right with your fingertip to resize **Width ($X$)** (`20 mm – 250 mm`).
     - **Emerald Green Sphere (`+Y HEIGHT`):** Drag up/down with your fingertip to extrude **Height ($Y$)** (`12 mm – 160 mm`).
     - **Sky Blue Sphere (`+Z DEPTH`):** Drag forward/backward with your fingertip to resize **Depth ($Z$)** (`20 mm – 220 mm`).
2. **Left-Hand Fusion Palette (`LeftHand_SpatialFusion_Palette`):**
   - Follows your **Left Hand** (and automatically faces straight toward your eyes so every button and label is 100% readable!).
   - Displays a **live rotating 3D Shape Preview** at the top and **6 Fusion 360 buttons** arranged in two columns.
3. **Front Desk Toolbar (7 Tactile 3D Buttons):**
   - Arranged along the front edge of your desk for quick one-finger pokes.

---

## 2. Plain-English Fusion 360 Button Guide

### A. Left-Hand Fusion Palette (6 Buttons on Your Left Hand)

| Button Label | Fusion 360 Function | What It Does When You Poke It |
| :--- | :--- | :--- |
| **`CREATE: SHAPE` *(Box / Cyl / Sphere / Torus / Tube / L)*** | **Create Primitive** | Cycles the 3D shape: **1. Box / Cube** $\rightarrow$ **2. Cylinder** $\rightarrow$ **3. Sphere** $\rightarrow$ **4. Torus Ring** $\rightarrow$ **5. Hollow Tube** $\rightarrow$ **6. L-Bracket**. |
| **`+ADD / CUT BODY` *(Combine onto 3D Model)*** | **Combine (Execute)** | Adds the current shape onto your 3D CAD Model (or cuts a hole if `COMBINE MODE` is set to `Cut`). Stacks up to **7 bodies** (poking a 7th time resets to 1 base body). *Tip: You can also touch the 3D Shape Preview at the top of your Left-Hand Palette and drag it directly onto the Center 3D Model!* |
| **`COMBINE MODE` *(Join / Cut Hole / Intersect)*** | **Combine Operation** | Switches between **Join (Add Solid)**, **Cut (Hole / Subtract)** (shown in glowing crimson), and **Intersect** (gold). Touch anywhere on the Center 3D Model to aim the **live 2 mm grid-snapped 3D Combine Cursor** before adding/cutting! |
| **`PATTERN` *(1x / 3x Linear / 6x Circular)*** | **Rectangular & Circular Pattern** | Multiplies the active body into a **Single (1x)** body, a **Linear Pattern (3x)** along $X$, or a **Circular Pattern (6x)** bolt circle. |
| **`EXTRUDE / PULL` *(Resize Width / Depth / Height)*** | **Extrude / Press Pull** | Steps through preset Extrude/Pull dimensions (+16 mm Height, +18 mm Depth, 1:1 Isometric Cube, or Reset), or drag the **`+X WIDTH` / `+Y HEIGHT` / `+Z DEPTH`** spheres directly on the 3D model! |
| **`MATERIAL` *(PLA+ / PETG-CF / 6061 Al / Ti)*** | **Physical Material** | Switches the engineering material between **PLA+ ($1.24\text{ g/cm}^3$)**, **PETG-CF ($1.38\text{ g/cm}^3$)**, **6061-T6 Aluminum ($2.70\text{ g/cm}^3$)**, and **Ti-6Al-4V Titanium ($4.43\text{ g/cm}^3$)**, updating real-time **Volume ($\text{cm}^3$)**, **Mass ($\text{g}$)**, and **3D Print Time ($\text{min}$)**. |

---

### B. Front Desk Toolbar (7 Buttons Along the Front Edge of Your Desk)

| Button Label | Fusion 360 Function | What It Does When You Poke It |
| :--- | :--- | :--- |
| **`CREATE SHAPE` *(Box / Cyl / Sphere)*** | **Create Primitive** | Cycles the active 3D shape (`Box`, `Cylinder`, `Sphere`, `Torus`, `Hollow Tube`, `L-Bracket`). |
| **`+ADD / CUT BODY` *(Combine Feature)*** | **Combine (Join / Cut)** | Combines a new body or cuts a hole into the Center 3D CAD Model at your aimed 3D cursor position. |
| **`MODIFY: SHELL` *(Hollow / Chamfer)*** | **Shell & Chamfer** | Cycles internal structural modifiers: **Solid Extrude** $\rightarrow$ **Shell (2.5 mm Hollow Enclosure)** $\rightarrow$ **Chamfer + 4x M4 Bosses** $\rightarrow$ **L-Bracket Ribbed**. |
| **`PATTERN ARRAY` *(1x / 3x / 6x)*** | **Pattern** | Cycles between **Single (1x)**, **Linear Pattern (3x)**, and **Circular Pattern (6x)**. |
| **`HOLD 1:1 MODEL` *(Inspect in Hand)*** | **1:1 Physical Preview** | Attaches a true-scale **1:1 3D Print Preview** to your **Left Hand** so you can turn your wrist and inspect wall thickness, holes, and ergonomics before 3D printing. |
| **`EXPORT .STL / .OBJ` *(Save 3D Print)*** | **Export Mesh** | Exports watertight binary **`.stl`** and **`.obj`** files of your multi-body 3D CAD model to `Exports/` (PC) or `Application.persistentDataPath` (Quest 3). |
| **`CENTER DESK` *(Reset View)*** | **Reset Workspace** | Re-centers the 3D CAD Workbench comfortably in front of your headset (`0.48 m` forward, `0.30 m` below eye level, or locked to your left hand height if resting on your physical desk). |

---

## 3. Measuring a Real-World Object on Your Desk (1:1 Passthrough Capture)

1. Look at any real physical object on your desk through **Quest 3 Passthrough**.
2. Point **both index fingers** (left + right) at the opposite edges of the real object.
3. A glowing cyan **Caliper Gauge** spans between your fingertips showing exact millimeters (`mm`).
4. Hold your fingertips steady (`< 3.5 mm/s` movement) for **0.9 seconds**:
   - A **1:1 wireframe bounding box** locks right over the real object on your desk.
   - Your **Center 3D CAD Model** immediately resizes to match the captured **Width $\times$ Depth $\times$ Height** (`mm`).
   - Reach out and drag the **`+X WIDTH`**, **`+Y HEIGHT`**, or **`+Z DEPTH`** spheres on the model to fine-tune any face to 1 mm precision!

---

## 4. Desktop / Unity Editor Keyboard & Mouse Shortcuts

When testing in the Unity Editor without a headset attached:
- **Left-Click & Drag `+X WIDTH` / `+Y HEIGHT` / `+Z DEPTH` Spheres:** Directly extrudes Width, Height, or Depth in 3D.
- **Hold `Shift` + Move Mouse Over Model:** Aims the live 3D Combine Cursor (`Join` / `Cut Hole`) on the 2 mm CAD grid.
- **Left-Click Any 3D Button** (on the Desk Toolbar or Left-Hand Palette): Depresses and triggers that button.
- **Right-Click + Drag / Scroll Wheel:** Orbit and zoom camera.
- **Hotkeys:** `[T]` Create Shape | `[B]` +Add / Cut Body | `[J]` Combine Mode (Join / Cut) | `[L]` Pattern | `[X]` Extrude / Pull | `[F]` Modify: Shell / Chamfer | `[M]` Material | `[I]` Hold 1:1 Model | `[U]` Export `.STL` / `.OBJ` | `[R]` Center Desk.
