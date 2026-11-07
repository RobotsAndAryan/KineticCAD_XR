# Meta VR Start Developer Competition 2026 — Complete Winning Submission Kit

---

## 1. Core Submission Fields (Copy & Paste into Devpost)

### **Submission Name**
```text
KineticCAD XR: Living Spatial Fusion 360 & Modular Robotics Studio
```

### **Submission Tagline** *(138 / 140 characters)*
```text
Hands-first MR CAD & robotics studio: capture 3D objects, snap modular joints, hold 1:1 prints in hand, Ghost-Carve CBFs, export STL/URDF.
```

### **Track & Eligible Prize Categories**
* **Primary Track:** **Productivity** — *Best New Productivity Experience ($70,000)* / *Runner-Up ($35,000)*
* **Targeted Special Awards ($25,000 each):**
  1. **Best Agentic Interaction ($25,000):** Autonomous **AEGIS Physical AI Co-Pilot** + Neuro-Symbolic Synthesis (`nemotron-3-super-120b` + $<1\text{ ms}$ On-Device PoE Parametric Engine).
  2. **Best First Five Minutes ($25,000):** Instant **MRUK Table Snap** + 90-Second Guided 3-Step Interactive Checklist (`3D Capture & CAD Feature` $\rightarrow$ `+Snap Module & Sweep W_i` $\rightarrow$ `Ghost Carve & Export`).
  3. **Best Accessibility Forward Experience ($25,000):** One-Handed Seated Lap Mode (**Gaze + 260 ms Dwell Pinch** + **Thumb Microgestures**) + Colorblind-Safe Symbolic & Scale-Coded Telemetry (`[▲ OPTIMAL]` vs `[▼ SINGULARITY]`).
  4. **Best Reason to Come Back ($25,000):** Dual 3D-Print Mesh (`.stl` / `.obj`) + ROS 2 `.urdf` / `_cbf_envelope.json` export utility + 5-Level Robotics Challenge with persistent career progression.
  5. **Boldest Original Concept ($25,000):** Real-Time Swept-Volume CSG **"Ghost Carving"** ($\mathcal{W}_i \to h(\mathbf{x}) \ge 0$) + **"Hold-In-Your-Hands" 1:1 Physical Print Preview**.

### **Division**
* **New Experience** *(Conceived and built from scratch after September 24, 2026)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store)

---

## 2. Project Description *(492 words — strictly under the 500-word limit, hitting all 4 Judging Pillars & Special Awards)*

```markdown
### Inspiration
Traditional desktop CAD like Fusion 360 feels "dead": you measure a real object with metal calipers, type numbers into a 2D monitor, extrude a static grey brick, and click through nested menus before anything moves. Worse, standing mid-air VR modeling tools fail within 120 seconds due to "Gorilla Arm" shoulder fatigue and "Heisenberg" pinch jitter ($2\text{–}6\text{ mm}$ index shift during pinch selection). We asked: *What if your physical desk and bare hands were a living "Spatial Fusion 360" where you capture real objects, snap motorized mechanisms together, hold 1:1 3D prints in your palm, and export both 3D-printable meshes and ROS 2 robots?*

### How We Built It (Engineered for Meta’s 4 Judging Pillars & Special Awards)
1. **Stage 1 — Real-World 3D Object Capture & Parametric Fusion CAD Features:**
   Anchored to your physical tabletop via Meta **MRUK** (`MRUKAnchor.SceneLabels.TABLE`), point both index fingers around any real object on your desk. Our $1\text{€}$-filtered bimanual caliper captures **Width ($X$)**, **Depth ($Z$)**, and **Height ($Y$)** to extrude a **1:1 Parametric 3D CAD Solid** right on your desk. One poke or thumb-swipe applies core Fusion CAD features—**Solid Extrude**, **2.5 mm Hollow Shell**, **Chamfer + M4 Mounting Bosses**, or **Gusseted L-Bracket**—while computing exact volume ($\text{cm}^3$), mass ($\text{g}$), and slicer print time across **PLA+**, **PETG-CF**, **6061-T6 Aluminum**, and **Titanium**.

2. **Stage 2 — Coaxial Magnetic Snap-Together Modular Assembly ($3\text{ to }6\text{ DoF}$):**
   Bring your hand to the glowing coaxial magnetic snap port (or poke **`[+SNAP MODULE]`** / **Thumb-Swipe Forward**) to snap modular **Prismatic Linear Actuator Sliders** ($\mathcal{S}_{\text{pris}}$), **Revolute Wrist Hinges** ($\mathcal{S}_{\text{rev}}$), and your **Captured 1:1 CAD Solid** directly onto the kinematic tree. Our double-precision **Product of Exponentials (PoE)** and **Damped Least-Squares (DLS) IK** engine ($\lambda=0.06$) immediately wakes up at 90 FPS, letting you puppet custom $3\text{–}6\text{ DoF}$ mechanisms with your right index fingertip.

3. **Stage 3 — "Hold-In-Your-Hands" 1:1 Print Preview & Swept-Volume "Ghost Carving" ($\mathcal{W}_i \to h(\mathbf{x}) \ge 0$):**
   - **Hold 1:1 Print in Hand:** Toggle **`[HOLD 1:1 IN HAND]`** to hold the 1:1 CAD solid in your left palm in Passthrough to inspect wall thicknesses and ergonomics before 3D printing.
   - **Ghost Carving & Agentic Co-Pilot:** As you puppet the arm, KineticCAD samples swept-volume capsules ($\mathcal{W}_i$) every $5^\circ$. The **AEGIS Co-Pilot Agent** flags clashing housing voxels in magenta ($h(\mathbf{x}) < 0$); a **Thumb-Tap** Boolean-carves a zero-clash clearance tunnel ($h(\mathbf{x}) \ge 0$).

4. **Stage 4 — Dual Mesh (`.STL` + `.OBJ`) & Robotics (`.URDF` + 60 Hz UDP) Export:**
   One poke exports watertight **`.stl`** slicer meshes, multi-group **`.obj`** CAD meshes, analytical **`.urdf`** robot descriptions, and **`_cbf_envelope.json`**, while streaming 60 Hz UDP joint states (`:9870`) to ROS 2 and NVIDIA Isaac Sim.

### Future Plans
- **Co-Located Multi-Engineer Rooms:** Shared spatial anchors for collaborative CAD assembly.
- **Direct Microcontroller Bridge:** Live Wi-Fi servo streaming to physical Dynamixel/ESP32 hardware.
```

---

## 3. How Hand Interactions & Gaze Are Implemented

```markdown
### How Hand Interactions & Gaze Are Implemented
KineticCAD XR is 100% controller-free end-to-end, combining 3D object capture, coaxial magnetic snapping, native Meta OpenXR Microgestures, and accessible seated "Eyes & Hands" gaze-pinch ergonomics:

1. **Zero-Jitter Meta OpenXR Microgestures (`OVRHand.GetMicrogestureType`):**
   Decouples 3D index-finger positioning from command execution: **Thumb-Swipe Left/Right** scrubs dimensions by $\pm 5.0\text{ mm}$, **Thumb-Swipe Forward** snaps a modular joint & link onto the assembly ($3\to 6\text{ DoF}$), **Thumb-Swipe Backward** cycles Fusion CAD features (`Extrude` / `Shell 2.5mm` / `M4 Bosses` / `L-Bracket`), and **Thumb-Tap** executes **`[GHOST CARVE]`** Boolean subtraction.
2. **Coaxial Magnetic Port Snapping & "Hold 1:1 Print in Hand":**
   Bringing your left index finger within $3.2\text{ cm}$ of the glowing distal snap port coaxially snaps new modular joints onto the live robot, while **`[HOLD 1:1 IN HAND]`** attaches the 1:1 CAD solid to your left palm so you can inspect ergonomics before 3D printing.
3. **Accessible Seated Lap Mode (7-Button Desk Bar + 260 ms Dwell Gaze & Pinch):**
   Users can physically poke the 7 desk buttons (`-5.5 mm` depression + procedural 35 ms mechanical switch click) or look within a $6.2^\circ$ FoV gaze cone and hold a 260 ms lap pinch for effortless one-handed seated control.

### What Excites Us About the Future of VR
Hands-first Mixed Reality + OpenXR Microgestures transform spatial computing into a **living CAD and physical engineering superpower**—letting creators measure real objects, snap mechanisms together, hold 1:1 prints in their hands, and export both 3D-printable meshes and ROS 2 robots.
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
4. **Build & Upload the Latest APK:**
   * In Unity, open **File → Build Profiles → Meta Quest** and click **Build** (save as `unity_vr_apps/KineticCAD_final_v3.apk`), then upload it to the **`Competition`** channel via **Meta Quest Developer Hub (MQDH)** or the web dashboard.
5. **Copy the Judge Invite URL:**
   * Inside the **`Competition`** channel, enable **Invite Users via URL** and paste that `https://www.meta.com/s/...` link into your Devpost submission!
