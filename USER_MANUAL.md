# KineticCAD XR — Complete User Manual & Spatial Controls Guide
**Living Spatial Fusion 360 & Modular Robotics Studio (Meta Quest 3 + Desktop Editor)**

---

## Quick-Start Overview (Your First 60 Seconds)
When you launch **KineticCAD XR** on Meta Quest 3, the studio automatically locks onto your physical tabletop using **Meta MRUK (`TABLE` anchor)** (or places an ergonomic desk 48 cm in front of you and 30 cm below eye level).

You have **three main interactive zones** on your desk:
1. **Left Wing / Left Hand — The Spatial Fusion 360 CAD Palette (`6 Nodes`):**
   * Raise your **Left Hand** in VR and this 6-button palette floats right beside your left wrist with a spinning 3D holographic shape preview above it (when your left hand is lowered, it docks cleanly on the left side of your desk).
2. **Center Workbench — The Living Mechanism, 1:1 Captured CAD Part & AEGIS Housing Bay:**
   * **Left Station:** Your **1:1 Captured & Sculpted Multi-Body CAD Part** (with live millimeter dimensions, material, mass in grams, and estimated 3D-print time).
   * **Center Mechanism:** Your articulated **$3\text{ to }6\text{ DoF}$ Robot Arm** with a glowing **Gold Coaxial Magnetic Snap Port Ring** at its tip and parallel gripper jaws.
   * **Center-Right Lattice:** The **72-Voxel AEGIS Drone Payload Bay Housing Block** for Swept-Volume CSG **"Ghost Carving"** ($h(\mathbf{x}) \ge 0$).
   * **Far-Right Pad:** The **Target Drop Zone** for the 5-Level Pick-and-Place Robotics Challenge.
3. **Front Edge of Desk — The 7-Button Main Command Bar:**
   * Large physical 3D poke-buttons that depress `-5.5 mm` into the desk with tactile mechanical switch clicks.

---

## Part 1: Hands-First VR Controls (Meta Quest 3 — No Controllers Required)

### A. Core Bimanual & Pointing Gestures
| What You Want To Do | How To Do It With Your Bare Hands | Visual & Audio Feedback |
| :--- | :--- | :--- |
| **1. Calibrate Desk to Your Physical Tabletop** | Rest your **Left Hand flat on your real desk** and poke **`[CALIBRATE DESK]`** (far-right red button on the front bar). | The virtual workbench snaps to the exact height ($Y$) and orientation of your physical desk. |
| **2. Measure & Capture a Real-World 3D Object In-Place ($W \times D \times H$)** | Point **both Index Fingers** (curling Middle, Ring, and Pinky fingers) on either side of a real object on your desk (e.g., motor, phone, battery). Hold steady for **1.0 second**. | Cyan caliper line bridges your fingertips, locks a **1:1 In-Place Passthrough Bounding Frame** right at the midpoint between your fingers over the real object, and updates the **Left Station CAD Solid**. |
| **3. Direct 3D Face-Pull Extrusion (`+X`, `+Y`, `+Z` Handles)** | Point your **Right Index Finger** (or click-drag with the mouse) on any of the **3 glowing 3D Face-Pull Handles** (`+X W` Coral Red, `+Y H` Emerald Green, `+Z D` Sky Blue) on the CAD Solid and pull outward/inward. | Continuously extrudes Width ($X$), Height ($Y$), or Depth ($Z$) in $1\text{ mm}$ increments with live millimeter ticks, volume ($\text{cm}^3$), mass ($\text{g}$), and slicer print time! |
| **4. Aim 3D Boolean CSG Cutters/Bosses Before Stamping** | Set Boolean mode to **`CUT`** (or hold a Left-Hand pinch / `Shift` key) and hover your **Right Index Finger** inside the Left Station CAD Solid. | A live **3D Boolean Cursor** tracks your fingertip on a $2\text{ mm}$ CAD grid so you can aim exact bore holes, slots, or bosses before poking **`[+STAMP BODY]`**! |
| **5. Drag-Carry-and-Snap Modules with Magnetic Tether ($3 \to 6\text{ DoF}$)** | Touch the **Spinning 3D Preview** above your Left-Hand Palette with your **Right Index Finger** and carry it toward the glowing **Gold Coaxial Snap Ring** at the robot's tip (or touch the Gold Ring with your Left Index Finger). | A glowing **6-segment Magnetic Snap Tether** bridges your finger to the snap port; touching the ring appends a new joint & link using your **sculpted CAD part's exact dimensions, shape, and mass ($\text{g}$)**! |
| **6. Direct Screw-Axis Joint Pin Inspector ($\mathcal{S}_i$ Flip)** | Poke any glowing **Cyan/Gold Joint Pin (`ScrewAxisPin`)** directly on the robot arm with your **Left Index Finger** (or click it with the mouse / press **`[K]`**). | Flips that specific joint's Screw Axis $\mathcal{S}_i$ (`Revolute Pitch Z [0,0,1]` $\rightarrow$ `Revolute Yaw Y [0,1,0]` $\rightarrow$ `Revolute Roll X [1,0,0]` $\rightarrow$ `Prismatic Slider X [1,0,0]`) and immediately recompiles the $SE(3)$ PoE chain! |
| **7. Puppet the Robot Arm (90 Hz DLS IK)** | Point **only your Right Index Finger** in the air above the desk (`z > -9.5 cm`) and move your finger smoothly. | A cyan reticle ring appears on your fingertip; the red IK target sphere follows your finger and the robot arm tracks you in real time. |
| **8. Hold Your 1:1 3D Print in Your Palm** | Poke **`[HOLD 1:1 IN HAND]`** (4th button on the front bar) and hold out your **Left Hand**. | Your multi-body CAD assembly attaches above your left palm at **true 1:1 millimeter scale** and slowly rotates so you can inspect wall thickness and bosses before 3D printing. |
| **9. Pick-and-Place Robotics Challenge** | Point your **Right Index Finger** over the **1:1 CAD Part** on the left of the desk to grasp it, then guide the arm along the cyan arc to the **Drop Zone Pad** on the right. | Jaws close automatically on pickup, track your manipulability $\mu(\boldsymbol{\theta})$ during carry, award a `★★★` rating on delivery, and advance through **Levels 1–5**. |

---

### B. Using the Left-Hand Spatial Fusion 360 CAD Palette (6 Nodes)
Raise your **Left Hand** above the desk (between `3 cm` and `45 cm` high) to summon the palette beside your left wrist, or use it while docked on the **Left Wing of the Desk**. Poke any of the **6 nodes** with your **Right Index Finger** (or look at a node and pinch):

1. **`[PRIM SHAPE (CUBE/CYL/SPH)]` (Top-Left, Sky Cyan):**
   * Cycles the active 3D CAD primitive through **6 engineering shapes**:
     1. `Box / Cube` (Parametric $W \times D \times H$ block)
     2. `Cylinder` (Radial shaft / boss)
     3. `Sphere` (Geodesic ball / dome)
     4. `Torus Ring` (Octagonal bearing / standoff ring)
     5. `Hollow Tube` (Thick-walled pipe sleeve with inner bore)
     6. `L-Bracket` (Gusseted structural mount)
   * Watch the **Spinning 3D Holographic Preview** above the palette morph shapes immediately!
2. **`[+STAMP BODY (MULTI-BODY)]` (Top-Center, Emerald Green):**
   * Stamps the current primitive (with its active Boolean mode and Pattern array) into your **Multi-Body Parametric Feature Tree** (up to **7 combined bodies** on the workbench and in your 1:1 Hand Preview; poking a 7th time clears/resets back to 1 primary solid).
3. **`[BOOLEAN CSG (JOIN/CUT/INT)]` (Top-Right, Crimson-Rose):**
   * Toggles how primitives combine:
     * **`JOIN (Union)`:** Adds solid material and mass.
     * **`CUT (Subtract)`:** Turns the feature into a glowing crimson-magenta negative cutter that carves bores/cavities and **subtracts mass**!
     * **`INTERSECT`:** Highlights shared intersection volume in gold.
4. **`[PATTERN (1x/LIN/CIRC)]` (Bottom-Left, Amber Gold):**
   * Cycles between **`1x Single`**, **`3x Linear Array`** (3 spaced features along $X$), and **`6x Circular Array`** (6 radial features in a $60^\circ$ bolt circle).
5. **`[PUSH / PULL FACE EXTRUDE]` (Bottom-Center, Violet):**
   * Steps through precision face extrusions (`+16 mm Height`, `+18 mm Depth`, `Isometric Equal-Width Lock`, and `Reset 55x42 mm`) with immediate mass and volume recalculation.
6. **`[MATERIAL (PLA/CF/AL/TI)]` (Bottom-Right, Titanium Silver):**
   * Cycles engineering materials: **`PLA+ (1.24 g/cm³)`** $\rightarrow$ **`PETG-CF (1.38 g/cm³)`** $\rightarrow$ **`6061-T6 Al (2.70 g/cm³)`** $\rightarrow$ **`Ti-6Al-4V (4.43 g/cm³)`**, updating part color, mass in grams, and 3D slicer print time in minutes.

---

### C. Using the Front Desk Bar (7 Main Command Buttons)
1. **`[SYNTHESIZE TOPOLOGY]` (Cyan):** Cycles across **4 distinct robot topologies** (*Articulated 3R Claw*, *True SCARA Selective Arm with Prismatic Z-Quill*, *Heavy Turret Palletizer*, *Precision Wrist Probe*) and runs live Nebius AI synthesis (`nvidia/nemotron-3-super-120b-a12b`).
2. **`[+SNAP MODULE (3..6 DoF)]` (Gold-Amber):** Coaxially snaps modular joints (`+Prismatic Slider` $\rightarrow$ `+Revolute Pitch Wrist` $\rightarrow$ `+Revolute Roll Flange + 1:1 CAD Solid`) onto the robot arm.
3. **`[CAD FEATURE (SHELL/BOSS)]` (Royal Blue):** Cycles parametric CAD body operations on box/bracket bodies: **`Solid Extrude`** $\rightarrow$ **`Shell 2.5mm Hollow Enclosure`** $\rightarrow$ **`Chamfer + 4x M4 Mounting Bosses`** $\rightarrow$ **`L-Bracket Mount`**.
4. **`[HOLD 1:1 IN HAND]` (Teal):** Attaches/detaches the 1:1 3D-printable multi-body CAD assembly to your Left Hand palm.
5. **`[GHOST CARVE (CBF h>=0)]` (Magenta):** Boolean-carves all clashing voxels (plus a safety margin $r_{\text{link}} + r_{\text{margin}}$) out of the **AEGIS Payload Bay Housing Block** along your recorded swept-volume path $\mathcal{W}_i$, guaranteeing Control Barrier Function safety ($h(\mathbf{x}) \ge 0$) and auto-exporting all files.
6. **`[EXPORT STL OBJ + URDF]` (Emerald Green):** Exports 4 industry-standard files in one click:
   * `<robot>.urdf` (ROS 2 REP-103 XML with exact inertia tensors $I_{xx}, I_{yy}, I_{zz}$)
   * `<robot>_cad.stl` (Watertight millimeter ASCII STL mesh with all stamped bodies, cylinders, spheres, patterns, links, and gripper jaws for Bambu Studio / PrusaSlicer / Cura)
   * `<robot>_cad.obj` (Multi-group Wavefront OBJ mesh for Fusion 360 / Blender / SolidWorks)
   * `<robot>_cbf_envelope.json` (AEGIS FlightOS Control Barrier Function & Parametric Feature Tree manifest)
7. **`[CALIBRATE DESK]` (Red):** Re-centers the desk to your current headset view (and snaps to your left hand's height if held on your physical tabletop) and restores the 72-voxel housing block.

---

### D. Zero-Jitter Meta OpenXR Thumb Microgestures (Asymmetric Left & Right Hand!)
Instead of pinching in mid-air (which pulls your index finger off target), keep your hands relaxed and flick your **Thumb along the side of your Index Finger**:

| Microgesture | **Right Hand** (Mechanism & Clearance) | **Left Hand** (Spatial Fusion CAD Palette) |
| :--- | :--- | :--- |
| **Thumb-Swipe Left / Right** | Scrubs active width dimension by **$-5\text{ mm}$ / $+5\text{ mm}$** | Cycles **Primitive Shape** (`Cube` $\rightarrow$ `Cylinder` $\rightarrow$ `Sphere` $\rightarrow$ `Torus` $\rightarrow$ `Tube` $\rightarrow$ `L-Bracket`) |
| **Thumb-Swipe Forward** | **`[+SNAP MODULE]`** (Snaps $3 \to 4 \to 5 \to 6\text{ DoF}$ joint/link) | **`[+STAMP BODY]`** (Stamps active primitive into Multi-Body CAD Tree) |
| **Thumb-Swipe Backward** | Cycles **CAD Feature Op** (`Solid` / `Shell 2.5mm` / `M4 Bosses` / `L-Bracket`) | Cycles **Pattern Array** (`1x Single` $\rightarrow$ `3x Linear` $\rightarrow$ `6x Circular`) |
| **Thumb-Tap** | Executes **`[GHOST CARVE]`** (Boolean-carves housing block & verifies $h(\mathbf{x}) \ge 0$) | Cycles **Boolean CSG Mode** (`JOIN` $\rightarrow$ `CUT` $\rightarrow$ `INTERSECT`) |

---

### E. Seated "Eyes & Hands" Accessibility Mode (Gaze + 260 ms Dwell Pinch)
Don't want to reach across the desk? Keep your hands resting in your lap:
1. **Look directly** at any of the **6 Left-Hand CAD Palette buttons** or **6 Front Desk buttons** (within a $6.2^\circ$ gaze cone; `[CALIBRATE DESK]` is excluded so you never accidentally reset your workspace).
2. The button highlights and depresses as you look at it.
3. **Hold an Index + Thumb Pinch** with either hand in your lap for **0.26 seconds** to click the button!

---

## Part 2: Desktop / Unity Editor Controls (Mouse & Keyboard)
When running in the Unity Editor without a headset connected, **KineticCAD XR** automatically activates its **Flat Editor Studio Camera** and top-left **Interactive Engineering GUI**:

### Mouse Controls
* **Left-Click any 3D Button on the Desk or Left-Hand Palette:** Fires that button with full 3D depression and mechanical click audio.
* **Left-Click & Drag any `+X W` / `+Y H` / `+Z D` 3D Face Handle on the CAD Part:** Continuously extrudes Width, Height, or Depth in $1\text{ mm}$ increments with live volume and mass updates.
* **Left-Click any Cyan/Gold Joint Pin (`ScrewAxisPin`) on the Robot Arm:** Directly flips that joint's Screw Axis $\mathcal{S}_i$ (`Revolute Pitch Z` $\rightarrow$ `Revolute Yaw Y` $\rightarrow$ `Revolute Roll X` $\rightarrow$ `Prismatic Slider X`) and recompiles the $SE(3)$ PoE chain.
* **Hold `Shift` + Drag over the CAD Part (or select `BOOLEAN CUT`):** Aims the live **3D Boolean CSG Cursor** on a $2\text{ mm}$ grid inside the CAD part before stamping (`B`).
* **Left-Click & Drag from the Left-Hand Palette 3D Preview to the Gold Snap Ring:** Carries your custom CAD module along the glowing **Magnetic Snap Tether** and snaps it onto the robot arm.
* **Left-Click & Drag in the 3D Scene:** Drags the red IK target sphere across the workbench (automatically dipping down over the Pickup Workpiece and Drop Zone for easy pick-and-place!).
* **Right-Click & Drag:** Orbits the 3D studio camera around the workbench.
* **Mouse Scroll Wheel:** Zooms the camera in and out.

### Complete Keyboard Shortcuts Table
| Key | Action | Description |
| :--- | :--- | :--- |
| **`T`** or **`1`** | **Cycle Primitive Shape** | `Box/Cube` $\rightarrow$ `Cylinder` $\rightarrow$ `Sphere` $\rightarrow$ `Torus Ring` $\rightarrow$ `Hollow Tube` $\rightarrow$ `L-Bracket` |
| **`B`** or **`2`** | **+Stamp Multi-Body** | Stamps current primitive (at your aimed 3D Boolean cursor offset!) into the Multi-Body CAD Tree |
| **`J`** or **`3`** | **Cycle Boolean Mode** | `JOIN (Union)` $\rightarrow$ `CUT (Subtract)` $\rightarrow$ `INTERSECT` |
| **`L`** or **`4`** | **Cycle Pattern Array** | `1x Single` $\rightarrow$ `3x Linear Array` $\rightarrow$ `6x Circular Radial Array` |
| **`X`** or **`5`** | **Push/Pull Face Extrude** | Steps through Height/Depth face extrusions (or drag the 3D `+X/+Y/+Z` handles directly!) |
| **`F`** | **Cycle CAD Feature Op** | `Solid Extrude` $\rightarrow$ `Shell 2.5mm Hollow` $\rightarrow$ `Chamfer + M4 Bosses` $\rightarrow$ `L-Bracket` |
| **`M`** | **Cycle Engineering Material** | `PLA+` $\rightarrow$ `PETG-CF` $\rightarrow$ `6061-T6 Al` $\rightarrow$ `Ti-6Al-4V` |
| **`N`** | **+Snap Modular Joint/Link** | Coaxially snaps $3 \to 4 \to 5 \to 6\text{ DoF}$ joints & links carrying your custom CAD part |
| **`K`** | **Flip Joint Screw Axis $\mathcal{S}_i$** | Cycles joint Screw Axis (`Revolute Z` $\rightarrow$ `Revolute Y` $\rightarrow$ `Revolute X` $\rightarrow$ `Prismatic X`) |
| **`I`** | **Hold 1:1 Print in Hand** | Toggles the floating 1:1 physical print preview |
| **`G`** | **Synthesize Topology** | Cycles robot topology (`3R` / `SCARA` / `Turret` / `Wrist Probe`) & triggers AI synthesis |
| **`C`** | **Execute Ghost Carve** | Boolean-carves clashing housing voxels along swept path $\mathcal{W}_i$ ($h(\mathbf{x}) \ge 0$) |
| **`V`** | **Toggle Envelope Recording** | Pauses/resumes Swept-Volume $\mathcal{W}_i$ ghost capsule sampling |
| **`[`** / **`]`** | **Scrub Width $\pm 5\text{ mm}$** | Decreases / increases active caliper & jaw width by $5\text{ mm}$ |
| **`H`** | **Deep Kinematic Audit** | Runs a 3D kinematic torque & singularity sweep (`Qwen3-235B` / analytical PoE) |
| **`U`** | **Export All CAD & Robot Files** | Saves `.urdf`, `_cad.stl`, `_cad.obj`, and `_cbf_envelope.json` to `Exports/` |
| **`W A S D` / `Q E`** | **Move IK Target ($X, Y, Z$)** | Moves the red IK target manually (hold `Shift` for $3\times$ speed) |
| **`P`** / **`O`** | **Toggle Puppet / Auto-Orbit** | Pauses/resumes IK tracking (`P`) or Figure-8 auto-orbit (`O`) |
| **`R`** or **`Space`** | **Reset & Calibrate** | Resets camera/desk pose, restores the 72-voxel housing block, and returns arm to home |

---

## Part 3: Where Are My Exported `.STL`, `.OBJ`, `.URDF`, and `.JSON` Files Saved?
Whenever you poke **`[EXPORT STL OBJ + URDF]`**, **`[GHOST CARVE]`**, or press **`[U]`**:
* **In Unity Editor (Windows/Mac/Linux):**
  Saved directly to `e:\Unity\KineticCAD_XR\Exports\`:
  * `Exports/<robot_name>.urdf` — Open in ROS 2 Jazzy (`rviz2`), Gazebo, or NVIDIA Isaac Sim.
  * `Exports/<robot_name>_cad.stl` — Drag straight into **Bambu Studio**, **PrusaSlicer**, or **UltiMaker Cura** to 3D print at 1:1 millimeter scale.
  * `Exports/<robot_name>_cad.obj` — Import into **Autodesk Fusion 360**, **Blender**, or **SolidWorks** with separated named object groups.
  * `Exports/<robot_name>_cbf_envelope.json` — Control Barrier Function safety margin ($h(\mathbf{x}) \ge 0$) and Multi-Body Feature Tree manifest.
* **On Meta Quest 3 Headset (Android):**
  Saved to `/sdcard/Android/data/com.DefaultCompany.KineticCAD_XR/files/` (pullable via `adb pull` or Meta Quest Developer Hub) while live joint states stream at **60 Hz over UDP port `9870`**.
