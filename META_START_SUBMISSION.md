# Meta VR Start Developer Competition 2026 — Complete Winning Submission Kit

---

## 1. Core Submission Fields (Copy & Paste into Devpost)

### **Submission Name**
```text
KineticCAD XR: Hands-First Spatial Robotics & CAD Studio
```

### **Submission Tagline** *(139 / 140 characters)*
```text
Hands-first MR robotics CAD studio: OpenXR microgestures, swept-volume Ghost Carving (CBF h(x)>=0), 90Hz PoE IK, and ROS 2 URDF/STL export.
```

### **Track & Eligible Prize Categories**
* **Primary Track:** **Productivity** — *Best New Productivity Experience ($70,000)* / *Runner-Up ($35,000)*
* **Targeted Special Awards ($25,000 each):**
  1. **Best Agentic Interaction ($25,000):** Autonomous **AEGIS Physical AI Co-Pilot** + Neuro-Symbolic Synthesis (`nemotron-3-super-120b` + $<1\text{ ms}$ On-Device PoE Parametric Engine).
  2. **Best First Five Minutes ($25,000):** Instant **MRUK Table Snap** + 90-Second Guided 3-Step Interactive Checklist (`Measure/Scrub` $\rightarrow$ `Sweep W_i` $\rightarrow$ `Ghost Carve & Export`).
  3. **Best Accessibility Forward Experience ($25,000):** One-Handed Seated Lap Mode (**Gaze + 260 ms Dwell Pinch** + **Thumb Microgestures**) + Colorblind-Safe Symbolic & Scale-Coded Telemetry (`[▲ OPTIMAL]` vs `[▼ SINGULARITY]`).
  4. **Best Reason to Come Back ($25,000):** Daily ROS 2 `.urdf` / `.stl` / `_cbf_envelope.json` export utility + 5-Level Robotics Transfer Challenge with persistent career progression.
  5. **Boldest Original Concept ($25,000):** Real-Time Swept-Volume CSG **"Ghost Carving"** ($\mathcal{W}_i \to h(\mathbf{x}) \ge 0$) on a physical desk.

### **Division**
* **New Experience** *(Conceived and built from scratch after September 24, 2026)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store)

---

## 2. Project Description *(488 words — strictly under the 500-word limit, hitting all 4 Judging Pillars & Special Awards)*

```markdown
### Inspiration
Standing mid-air "Iron Man" CAD tools fail within 120 seconds due to "Gorilla Arm" shoulder fatigue and "Heisenberg" pinch jitter ($2\text{–}6\text{ mm}$ index shift during pinch selection). Furthermore, robotics engineers cannot visually verify if a moving arm or drone payload claw will collide with its mounting housing across its full joint range. We asked: *What if your physical desk and bare hands were an industrial spatial robotics & clearance-carving studio?*

We built **KineticCAD XR** for Meta Quest 3 to transform any seated tabletop into a controller-free, 90 Hz spatial engineering studio.

### How We Built It (Engineered for Meta’s 4 Judging Pillars & Special Awards)
1. **Best First Five Minutes & Seated Ergonomics (MRUK Table Anchoring):**
   Using Meta’s **MRUK** (`MRUKAnchor.SceneLabels.TABLE`), the workbench locks onto your physical desk so your elbows rest comfortably on the tabletop. On boot, it spawns the **AEGIS Aerial Payload Claw**, an open-frame **Payload Bay Mounting Housing Lattice**, and a 90-second 3-step interactive tutorial.

2. **Zero-Jitter Meta OpenXR Microgestures + Bimanual $1\text{€}$ Caliper:**
   - **Bimanual Caliper:** Point both index fingers on either side of a real object on your desk to measure its width (projected orthogonally onto the desk plane with a $1\text{€}$ filter, $f_{c,\min}=1.0\text{ Hz}, \beta=0.003$).
   - **Native `OVRHand` Microgestures:** Decouples positioning from confirmation—**Thumb-Swipe Left/Right** scrubs dimensions by $\pm 5.0\text{ mm}$, **Thumb-Swipe Forward** synthesizes topologies, **Thumb-Swipe Backward** toggles envelope recording, and **Thumb-Tap** executes **`[GHOST CARVE]`** with zero pinch jitter.

3. **Boldest Concept & Agentic Co-Pilot: Swept-Volume "Ghost Carving" ($\mathcal{W}_i \to h(\mathbf{x}) \ge 0$):**
   As you puppet the arm with your right index fingertip, KineticCAD samples moving link capsules every $5^\circ$ to build a 3D **Swept-Volume Ghost Envelope ($\mathcal{W}_i$)**. The **AEGIS Co-Pilot Agent** monitors capsule-to-voxel signed distances in real time, expanding clashing housing voxels in magenta ($h(\mathbf{x}) < 0$) and advising corrective action. Triggering **`[GHOST CARVE]`** performs a 3D Voxel Boolean subtraction—carving a zero-interference clearance slot and verifying the Control Barrier Function safety margin:
   $$h(\mathbf{x}) = \min_{k \in \mathcal{W}_i} \|\mathbf{p}_{\text{obs}} - \mathbf{p}_k(\boldsymbol{\theta})\|_2 - (r_{\text{link}} + r_{\text{margin}}) \ge 0$$

4. **Accessibility, Retention & Sim-to-Real Pipeline:**
   Supports one-handed seated lap operation (**6.2° Gaze + 260 ms Dwell Pinch** + **Thumb Microgestures**) and colorblind-safe symbolic telemetry (`[▲ OPTIMAL]` / `[▼ SINGULARITY]`). Runs a 100% entrant-built double-precision **Product of Exponentials (PoE)** and **DLS IK** ($\lambda = 0.06$) engine across 4 topologies (**AEGIS 3R Claw**, **True SCARA**, **Heavy Turret**, **Precision Probe**), a 5-level pick-and-place challenge with persistent career stats, and one-poke export of `.urdf`, `.stl`, and `_cbf_envelope.json` plus 60 Hz UDP streaming (`:9870`) to ROS 2 and NVIDIA Isaac Sim.

### Future Plans
- **Co-Located Multi-Engineer Rooms:** Shared spatial anchors for collaborative mechanism inspection.
- **Direct Microcontroller Bridge:** Live Wi-Fi servo streaming to physical Dynamixel/ESP32 hardware.
```

---

## 3. How Hand Interactions & Gaze Are Implemented

```markdown
### How Hand Interactions & Gaze Are Implemented
KineticCAD XR is 100% controller-free end-to-end, combining direct physical manipulation, native Meta OpenXR Microgestures, and accessible seated "Eyes & Hands" gaze-pinch ergonomics:

1. **Zero-Jitter Meta OpenXR Microgestures (`OVRHand.GetMicrogestureType`):**
   Eliminates "Heisenberg" pinch jitter by decoupling 3D index-finger positioning from command execution: **Thumb-Swipe Left/Right** scrubs mechanism width by $\pm 5.0\text{ mm}$, **Thumb-Swipe Forward** triggers topology synthesis, **Thumb-Swipe Backward** toggles Swept-Volume $\mathcal{W}_i$ recording, and **Thumb-Tap** executes **`[GHOST CARVE]`** Boolean subtraction on the payload housing block.
2. **Accessible Seated Lap Mode (Workspace-Gated Poke + 260 ms Dwell Gaze & Pinch):**
   Users can physically poke the 6 desk buttons (`-5.5 mm` depression + procedural 35 ms mechanical switch click) or look within a $6.2^\circ$ FoV gaze cone and hold a 260 ms lap pinch for effortless one-handed seated control.
3. **Bimanual $1\text{€}$ Caliper & Index-Only Pose Disambiguation:**
   Evaluates finger curl ratios on `XRHandSubsystem` and `OVRSkeleton` for intentional index-finger puppeteering and orthogonal tabletop caliper measurement.

### What Excites Us About the Future of VR
Hands-first Mixed Reality + OpenXR Microgestures transform spatial computing into an **accessible physical engineering superpower**—letting creators measure real hardware, carve zero-clash swept-volume housings, and export verified robotics packages with their bare hands.
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
