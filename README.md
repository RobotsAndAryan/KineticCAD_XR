# KineticCAD XR — Interactive 3D Spatial CAD & 3D-Printing Studio (Fusion 360 for Meta Quest 3)

> **Hands-First Spatial CAD, Bimanual Passthrough Calipers & Physical AI Studio for Meta Quest 3**  
> *Official Submission for the **Meta VR Start Developer Competition 2026** (Productivity Track + Special Awards)*

[![Competition](https://img.shields.io/badge/Meta%20VR%20Start%202026-Productivity%20Track-0081FB.svg)](https://www.meta.com/)
[![Platform](https://img.shields.io/badge/Platform-Meta%20Quest%203%20%7C%20Unity%206%20(6000.6.6f1)-blue.svg)](https://unity.com/)
[![Input](https://img.shields.io/badge/Interaction-Controller--Free%20Hand%20Tracking%20%2B%20Microgestures-00CEC9.svg)]()
[![CAD](https://img.shields.io/badge/CAD%20Engine-12%20Procedural%20Solids%20%7C%20900mm%20Buildspace-FF7675.svg)]()
[![Export](https://img.shields.io/badge/Export-.STL%20%7C%20.OBJ%20%7C%20.URDF%20%7C%2060Hz%20UDP-2ECC71.svg)]()

---

## 🌟 Overview

**KineticCAD XR** transforms your physical desk in **Meta Quest 3 Color Passthrough** into a hands-first **Spatial Fusion 360 CAD & 3D-Printing Studio**. Instead of battling 2D mouse orbits or menu-heavy desktop software, makers and mechanical engineers sculpt up to **900 mm** multi-body CAD parts directly with bare hands, measure real objects on their desk with **bimanual Passthrough calipers**, hold **1:1 physical print previews** on their right hand, and export slicer-ready **`.stl`**, **`.obj`**, and robotics **`.urdf`** files in one poke.

Built on a shared mathematical kinematics & robotics kernel (**Product of Exponentials**, **Damped Least-Squares IK**, **1€ Precision Filtering**, **Swept-Volume Control Barrier Functions**, and **60 Hz UDP ROS 2 / Isaac Sim telemetry**), KineticCAD XR bridges spatial product design directly to 3D printing and physical AI.

---

## ✨ Key Capabilities

### 1. Expanded 3D Spatial CAD Workbench (`720 mm × 540 mm` Desk, up to `900 mm` Extrusions)
- **3 Direct Extrude / Pull Handles (`10 mm – 900 mm`):** Coral Red (**`+X WIDTH`**), Emerald Green (**`+Y HEIGHT`**), and Sky Blue (**`+Z DEPTH`**) handles resize your solid continuously with acoustic millimeter ticks.
- **3 Angular Tilt / Rotation Handles (`-180°` to `+180°` in `5°` steps):** Amber Orange (**`TILT X°`** Pitch), Neon Violet (**`TILT Y°`** Yaw), and Electric Gold (**`TILT Z°`** Roll).
- **Direct 3D Translation Handle (`MOVE 3D (X/Y/Z)`):** Omnidirectional teal handle lets you position shapes anywhere across the expanded build volume (**`X: [-300, +300] mm`**, **`Y: [-75, +330] mm`**, **`Z: [-270, +270] mm`**) before joining or cutting.

### 2. 12 Watertight Procedural Engineering & Fusion 360 Shapes
Cycle all **12 parametric CAD solids** rendered with crisp normals and true coaxial through-holes (`BuildFrustumConeMesh`, `BuildTorusMesh`, `BuildHollowPrismTubeMesh`, `BuildWedgeRampMesh`):
1. `Box / Cube`
2. `Cylinder`
3. `Sphere`
4. `Cone / Frustum`
5. `Torus Ring`
6. `Hollow Tube` *(true coaxial inner bore)*
7. `Hex Prism (M-Nut)` *(true 6-sided nut with center bore)*
8. `Wedge / 45° Ramp`
9. `L-Bracket` *(with dual chamfered gusset ribs)*
10. `U-Channel` *(3-walled C/U structural rail)*
11. `T-Beam Extrusion`
12. `Flanged Bearing` *(mounting flange + boss hub + through-bore)*

### 3. Palm-Locked Left-Hand Fusion 360 Palette & Right-Hand 1:1 Print Preview
Computed via 3D cross-product palm normals (`HandFingertipReader.TryGetOvrPalmFacingCamera` / `TryGetOpenXrPalmFacingCamera`):
- **Palm Facing Headset Camera (`dot > 0.35`):** Your **Left-Hand Fusion 360 Tool Palette** (or **Right-Hand 1:1 Model Preview**) smoothly tracks your hand.
- **Palm Turned Away or Lowered:** The panel or 1:1 model **freezes rock-solid in 3D space**, eliminating mid-air wobble so you can poke buttons comfortably or inspect a model from every angle without arm fatigue.
- **Essential 10-Button Layout (Debounced `2.4 cm` poke radius + pull-back release latch + `-5.5 mm` spring press + 35 ms mechanical click):**
  - **6 Left-Hand Palette Buttons:** `CREATE: SHAPE (12 CAD Shapes)`, `+ADD / CUT BODY (Place on Model)`, `COMBINE MODE (Join / Cut Hole)`, `MODIFY: SHELL (Hollow / Chamfer)`, `PATTERN (1x / Linear / Circ)`, `MATERIAL (PLA / CF / Al / Ti)`.
  - **4 Front Desk Toolbar Buttons:** `HOLD 1:1 MODEL (On Right Hand)`, `RESET BODIES (Clear Added)`, `EXPORT .STL / .OBJ (Save & Preview)`, `CENTER DESK (Reset View)`.

### 4. Bimanual Passthrough Calipers & Live Agentic CAD / Slicer Co-Pilot
- **Bimanual Index Caliper (`1€` Filtered):** Point both index fingers (`IsOvrIndexOnlyPointing` / `IsOpenXrIndexOnlyPointing`) around any physical desk object to capture exact `Width × Depth × Height` tolerances (`MinCutoff = 1.0 Hz`, `Beta = 0.003`).
- **Live Engineering & Printability Telemetry:** Instant mass (`g`) across **PLA+ (`1.24 g/cm³`)**, **PETG-CF (`1.38 g/cm³`)**, **6061-T6 Al (`2.70 g/cm³`)**, and **Ti-6Al-4V (`4.43 g/cm³`)**, estimated 3D print time (`min`), overhang warning (`> 45°` tree support advisory), and **NVIDIA Nemotron** neuro-symbolic synthesis fallback.

### 5. One-Poke `.STL` / `.OBJ` / `.URDF` Export & In-VR 3D Turntable Gallery
- Poking **`EXPORT .STL / .OBJ`** writes:
  - Watertight ASCII **`.stl`** (`<robot>_cad.stl`, millimeter units ready for Bambu Studio / PrusaSlicer / Cura),
  - Multi-group Wavefront **`.obj`** (`<robot>_cad.obj`) preserving `Join` / `Cut` / `Intersect` bodies, tilts, patterns, and 3D offsets,
  - Analytical **`.urdf`** + **`_cbf_envelope.json`**,
- Spawns a live rotating preview on the right-side **Exported 3D Model Gallery Turntable (`ExportedModel_3D_GalleryPedestal`)** right inside Mixed Reality, while broadcasting 60 Hz UDP telemetry on port `9870` (`Tools/udp_to_joint_states.py`).

---

## 🏗️ Architecture & Kinematics Foundation

- **MRUK Spatial Anchoring:** Snaps the `720 mm × 540 mm` workbench directly onto your physical desk (`MRUKAnchor.SceneLabels.TABLE`).
- **One-Euro Precision Filter:** Eliminates high-frequency hand-tracking tremor while keeping rapid extrusions responsive.
- **Product of Exponentials (PoE) & DLS IK:** Analytical $se(3)$ forward kinematics, Yoshikawa manipulability $\mu(\boldsymbol{\theta}) = \sqrt{\det(J J^T)}$, and singularity-safe Damped Least-Squares IK ($\lambda = 0.06$).
- **Accessible Input Options:** Full bare-hand direct poking, Meta OpenXR **Microgestures**, seated **Gaze + 260 ms Dwell Pinch**, Meta Touch controllers, and Desktop Unity Editor mouse/keyboard workflows.

---

## 🚀 Quickstart Guide

### On Meta Quest 3 (Standalone APK)
1. Enable Developer Mode on your Quest 3 and connect via USB-C or wireless ADB.
2. Install the latest competition build APK:
   ```bash
   adb install -r unity_vr_apps/KineticCAD_final_v3_meta.apk
   ```
3. Launch **KineticCAD XR** from **Unknown Sources** (or install via your Meta Horizon **`Competition`** release channel Invite URL).
4. See [`USER_MANUAL.md`](USER_MANUAL.md) for the full gesture, button, and desktop keyboard guide.

### Pulling Exported `.STL` & `.OBJ` Prints from Quest 3
```powershell
adb pull /sdcard/Android/data/com.DefaultCompany.KineticCADXR/files/ ./Exports/
```
*(In the Unity Editor, exports write directly to `Exports/`.)*

### Optional 60 Hz ROS 2 / Isaac Sim Telemetry Bridge
```bash
python Tools/udp_to_joint_states.py --port 9870
```

---

## 📦 Repository Structure

```
KineticCAD_XR/
├── Assets/
│   ├── KineticCore/
│   │   ├── AI/                 # NebiusKinematicSynthesizer (Nemotron + deterministic fallback)
│   │   ├── CaliperController.cs# Bimanual index caliper & 1€ noise filter
│   │   ├── Editor/             # Build validation & scene tools
│   │   ├── Export/             # UrdfSerializer (.stl/.obj/.urdf/_cbf_envelope.json) & UDP broadcaster
│   │   ├── Input/              # HandFingertipReader (palm-normal camera lock, OpenXR/OVR/mouse shims)
│   │   ├── Kinematics/         # PoE Forward Kinematics, ScrewAxis, DLS IK, OneEuroFilter
│   │   ├── Runtime/            # KineticMechanismController & QuestHandAndCommandBridge
│   │   └── Schema/             # MechanismGraph & StampedCadBodySpec JSON schemas
│   └── Scenes/
│       └── SampleScene.unity   # Production Quest 3 Mixed Reality scene
├── Exports/                    # Reference & Editor-exported .stl / .obj / .urdf files
├── Tools/
│   └── udp_to_joint_states.py  # 60 Hz UDP to ROS 2 / standalone terminal telemetry bridge
├── META_START_SUBMISSION.md    # Ready-to-paste Devpost answers & Meta Dashboard release steps
├── USER_MANUAL.md              # End-user XR & desktop CAD studio manual
└── README.md
```
