# Meta VR Start Developer Competition 2026 — Complete Winning Submission Kit

---

## 1. Core Submission Fields (Copy & Paste into Devpost)

### **Submission Name**
```text
KineticCAD XR: Spatial Fusion 360 & 3D-Printing Studio
```

### **Submission Tagline** *(138 / 140 characters)*
```text
Controller-free MR Fusion 360 studio: caliper real desk parts, sculpt 12 CAD solids up to 900mm, hold 1:1 in hand, export .STL/.OBJ/.URDF.
```

### **Track & Eligible Prize Categories**
* **Primary Track:** **Productivity** — *Best New Productivity Experience ($70,000)* / *Runner-Up ($35,000)*
* **Targeted Special Awards ($25,000 each):**
  1. **Best Agentic Interaction ($25,000):** Live **Agentic CAD & Slicer Co-Pilot** + Neuro-Symbolic Synthesis (`nemotron-3-super-120b` + $<1\text{ ms}$ On-Device Analytical Engine) offering real-time printability, overhang $>45^\circ$ tree-support warnings, and material-aware mass telemetry.
  2. **Best First Five Minutes ($25,000):** Instant **MRUK Table Snap** (`720 mm × 540 mm` expanded desk) + clean 10-button Fusion 360 layout with an ergonomic **Palm-Locked Left-Hand Palette** and live spinning **In-VR Export Turntable**.
  3. **Best Accessibility Forward Experience ($25,000):** Seated **Gaze + 850 ms Dwell Pinch**, Palm-Freeze resting stability (drop your hand and panels stay put), pull-back button release latch, and colorblind-safe symbolic feedback.
  4. **Best Reason to Come Back ($25,000):** True multi-body `.STL` / `.OBJ` slicer export (`Exports/`) + ROS 2 `.urdf` / 60 Hz UDP robotics telemetry so makers return daily for real 3D prints.
  5. **Boldest Original Concept ($25,000):** Bare-hand **Spatial Fusion 360** in Passthrough combining bimanual physical calipers, **12 watertight procedural CAD shapes**, direct **Extrude/Tilt/Move 3D handles**, and **Hold 1:1 Model on Right Hand**.

### **Division**
* **New Experience** *(Conceived and built from scratch for Meta Horizon Quest 3)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store)

---

## 2. Project Description *(455 words — strictly under the 500-word limit — ready to paste into Devpost)*

```markdown
### Inspiration
Desktop CAD tools like Fusion 360 feel disconnected from the physical world: you measure a desk object with metal calipers, type numbers into a 2D screen, orbit a rigid grey viewport with a mouse, and wait hours for a test print only to discover a 2 mm fit error. Meanwhile, older VR modeling tools suffer from mid-air shoulder fatigue ("Gorilla Arm") and pinch jitter that make precision engineering impossible. We built **KineticCAD XR** so your physical desk and bare hands become a living **Spatial Fusion 360 & 3D-Printing Studio** in Meta Quest 3 Mixed Reality.

### What It Does & How We Built It
1. **Bimanual Passthrough Calipers & 50% Expanded `720 mm × 540 mm` Workbench:**
   Anchored to your desk via Meta **MRUK**, pointing both index fingers around any real-world object captures **Width × Depth × Height** in millimeters using a **$1\text{€}$ jitter filter** (`fc_min = 1.0 Hz`, `beta = 0.003`), spawning a 1:1 wireframe bounding twin right in place.

2. **12 Watertight Procedural Engineering Solids (Up to `900 mm`) & Direct 3D Handles:**
   Instead of nested menus, users sculpt directly around their model using **7 color-coded spatial handles**:
   - **3 Extrude / Pull Handles (`10 mm – 900 mm`):** Drag **`+X` (Coral)**, **`+Y` (Emerald)**, and **`+Z` (Sky Blue)** faces with acoustic millimeter ticks.
   - **3 Angular Tilt Handles (`-180°` to `+180°` in `5°` steps):** Drag **`TILT X°`**, **`TILT Y°`**, and **`TILT Z°`** handles.
   - **Direct 3D Translation (`MOVE 3D (X/Y/Z)`):** Drag anywhere across the expanded build volume (`X: ±300 mm`, `Y: -75..+330 mm`, `Z: ±270 mm`) before combining bodies.
   Cycle **12 custom procedural CAD meshes**—**Box/Cube, Cylinder, Sphere, Cone/Frustum, Torus Ring, Hollow Tube, Hex Prism (M-Nut), Wedge/45° Ramp, L-Bracket, U-Channel, T-Beam Extrusion, and Flanged Bearing**—rendered with watertight normals and true through-holes.

3. **Palm-Locked Left-Hand Palette & Hold 1:1 Model on Right Hand:**
   Using a 3D cross-product palm-normal solver (`Vector3.Cross` across wrist, index, and pinky metacarpals), the **Left-Hand 6-Button Fusion 360 Palette** (`CREATE`, `+ADD / CUT BODY`, `COMBINE MODE`, `MODIFY: SHELL`, `PATTERN 1x/3x/6x`, `MATERIAL`) and the **Right-Hand 1:1 Print Preview** follow your hand *only* when your palm faces the headset camera—turn your palm away and they freeze rock-solid in 3D space!

4. **One-Poke Multi-Body `.STL` / `.OBJ` / `.URDF` Export & In-VR Turntable Gallery:**
   The clean 4-button Front Desk bar (`HOLD 1:1 MODEL`, `RESET BODIES`, `EXPORT .STL / .OBJ`, `CENTER DESK`) pairs with a live **Agentic CAD & Slicer Co-Pilot** calculating mass (PLA+, PETG-CF, 6061-T6 Al, Ti-6Al-4V), print time, and overhang warnings, then spawns a spinning 3D turntable gallery right on your desk alongside slicer-ready `.stl`, `.obj`, and `.urdf` files.

### Future Plans
Shared MRUK spatial rooms for multi-engineer CAD reviews and direct Wi-Fi slicer queueing to Bambu Lab / Prusa 3D printers.
```

---

## 3. How Hand Interactions & Gaze Are Implemented

```markdown
### How Hand Interactions & Gaze Are Implemented
KineticCAD XR is 100% controller-free end-to-end, engineered to eliminate mid-air UI drift and accidental double-taps:

1. **Palm-Normal Camera-Facing Lock (`HandFingertipReader.TryGetOvrPalmFacingCamera` & `TryGetOpenXrPalmFacingCamera`):**
   We compute each hand's anatomical palm plane normal from wrist (`Hand_WristRoot`), index metacarpal (`Hand_Index1`), and pinky metacarpal (`Hand_Pinky1`) via chirality-corrected cross products (`Cross(vIndex, vPinky)` left, `Cross(vPinky, vIndex)` right). Both the floating **6-Button Left-Hand Fusion 360 Palette** and **Right-Hand 1:1 Print Preview** smoothly track your hand *only* when `Vector3.Dot(palmNormal, toCamera) > 0.35`. Rotating your palm away or resting your arm freezes the panel/model stationary in world space for effortless two-handed inspection and jitter-free button poking.

2. **Debounced Tactile Poke + Pull-Back Release Latch (`QuestHandAndCommandBridge`):**
   An `8 mm` cyan fingertip reticle (`HandsFirst_IndexReticleDot`) gives sub-centimeter aiming across **10 essential spatial buttons** (**4 Front Desk Toolbar buttons** including **`RESET BODIES`** + **6 Left-Hand Palette buttons**). Buttons use a tight **`2.4 cm` poke radius**, **`0.85 s` cooldown**, `-5.5 mm` physical spring depression, `35 ms` procedural mechanical audio click, and a **pull-back hysteresis latch** (must withdraw $> 4.2\text{ cm}$ before another trigger).

3. **Direct 6-DoF Handle Dragging & Bimanual Pointing Detection:**
   Pointing strictly with the index finger (`middle/ring/pinky` curled) engages the **Bimanual Calipers** (`1€`-filtered planar projection) and drags the **7 direct CAD handles** (`+X/+Y/+Z` Extrude, `TILT X°/Y°/Z°`, and `MOVE 3D X/Y/Z`).

4. **Meta OpenXR Microgestures & Seated Gaze + Dwell Pinch Accessibility:**
   Supports native **Thumb-Swipe** / **Thumb-Tap** (`OVRHand.GetMicrogestureType`) and a seated **`4.2°` FoV Gaze + `850 ms` Dwell Pinch** mode (`ResetPose` excluded to prevent accidental resets) for effortless one-handed desktop CAD.

### What Excites Us About the Future of VR
Hands-first Mixed Reality bridges digital design and physical reality—letting anyone measure a broken household clip or aerospace bracket on their desk, sculpt a parametric fix in 1:1 scale right in their palm, and send a watertight `.STL` straight to a 3D printer.
```

---

## 4. Step-by-Step Guide: Uploading to the Meta "Competition" Release Channel

Meta judges **must** be able to install your build via a Meta Horizon Invite URL:

1. **Open the Meta Horizon Developer Dashboard:**
   * Go to [https://developers.meta.com/horizon/manage/](https://developers.meta.com/horizon/manage/) and sign in.
2. **Create or Select Your App:**
   * Click **Create App** → Name it **`KineticCAD XR`** → Select **Meta Quest**.
3. **Create the `Competition` Release Channel:**
   * Go to **Distribution → Release Channels** → Click **Create Channel** → Name it **`Competition`**.
4. **Upload Your Latest Tested APK:**
   * Upload `unity_vr_apps/KineticCAD_final_v3_meta.apk` (or a fresh Unity Build from `File → Build Profiles → Meta Quest`) to the **`Competition`** channel via **Meta Quest Developer Hub (MQDH)** or the web dashboard.
5. **Copy the Judge Invite URL:**
   * Inside the **`Competition`** channel, enable **Invite Users via URL**, copy the `https://www.meta.com/s/...` link, and paste it into your Devpost submission!
