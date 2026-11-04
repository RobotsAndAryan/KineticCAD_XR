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

### **Track**
* **Productivity** *(Multi-Panel Creative & Engineering Studio / Seated Desk Workspace)*

### **Division**
* **New Experience** *(Conceived and built from scratch after September 24, 2026)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store)

---

## 2. Project Description *(495 words — strictly under the 500-word limit, hitting all 4 Judging Pillars & Special Awards)*

```markdown
### Inspiration
Standing mid-air "Iron Man" CAD tools fail within 120 seconds due to "Gorilla Arm" shoulder fatigue and "Heisenberg" pinch jitter ($2\text{–}6\text{ mm}$ index shift during pinch selection). Furthermore, engineers cannot visually verify if an articulated robot arm or drone payload claw will collide with its mounting housing across its full motion range. We asked: *What if your physical desk and bare hands were an industrial spatial robotics & clearance-carving studio?*

We built **KineticCAD XR** for Meta Quest 3 to transform any seated tabletop into a controller-free, 90 Hz spatial engineering studio.

### How We Built It (Engineered for Meta’s 4 Judging Pillars)
1. **Seated Ergonomics (MRUK Table Anchoring & 90-Second AEGIS Starter Template):**
   Using Meta’s **MRUK** (`MRUKAnchor.SceneLabels.TABLE`), the workbench locks onto your physical desk so your elbows rest comfortably on the tabletop. On boot, it immediately loads the **AEGIS Aerial Payload Claw Starter Template**, an uncarved **Payload Bay Mounting Housing Block**, and a 3-step interactive checklist.

2. **Zero-Jitter Meta OpenXR Microgestures + Bimanual $1\text{€}$ Caliper:**
   - **Bimanual Caliper:** Point both index fingers on either side of a real object on your desk to measure its width (projected orthogonally onto the desk plane with a $1\text{€}$ filter, $f_{c,\min}=1.0\text{ Hz}, \beta=0.003$).
   - **Native `OVRHand` Microgestures:** Separates positioning from confirmation—**Thumb-Swipe Left/Right** scrubs mechanism dimensions by $\pm 5.0\text{ mm}$, **Thumb-Swipe Forward** synthesizes topologies, **Thumb-Swipe Backward** toggles envelope recording, and **Thumb-Tap** executes **`[GHOST CARVE]`** with zero pinch jitter.

3. **Swept-Volume CSG "Ghost Carving" ($\mathcal{W}_i$) & Control Barrier Function ($h(\mathbf{x}) \ge 0$):**
   As you puppet the arm with your right index fingertip through its trajectory $\boldsymbol{\theta}(t)$, KineticCAD samples moving link capsules every $5^\circ$ to render a 3D **Swept-Volume Ghost Envelope ($\mathcal{W}_i$)** and flags clashing housing voxels in magenta ($h(\mathbf{x}) < 0$). Triggering **`[GHOST CARVE]`** performs a real-time 3D Voxel Boolean subtraction—carving a zero-interference clearance slot through the mounting block on your desk and verifying the Control Barrier Function safety margin:
   $$h(\mathbf{x}) = \min_{k \in \mathcal{W}_i} \|\mathbf{p}_{\text{obs}} - \mathbf{p}_k(\boldsymbol{\theta})\|_2 - (r_{\text{link}} + r_{\text{margin}}) \ge 0$$

4. **100% Entrant-Built $SE(3)$ Kinematics Core & Sim-to-Real Pipeline:**
   Runs a custom double-precision C# **Product of Exponentials (PoE)** screw-theory engine, **Damped Least-Squares (DLS)** IK ($\lambda = 0.06$) at 90 FPS, and **Yoshikawa manipulability** ($\mu = \sqrt{\det(JJ^T)}$) shader feedback across 4 topologies (**AEGIS 3R Claw**, **True SCARA**, **Heavy Turret**, **Precision Probe**). One poke exports compliant `.urdf` files, `.stl` meshes, and `_cbf_envelope.json` while streaming 60 Hz UDP telemetry (`:9870`) to ROS 2 and NVIDIA Isaac Sim.

### Future Plans for Improvement
- **Co-Located Multi-Engineer Rooms:** Shared spatial anchors for collaborative mechanism inspection.
- **Direct Microcontroller Bridge:** Live Wi-Fi servo streaming to physical Dynamixel/ESP32 hardware on the desk.
```

---

## 3. How Hand Interactions & Gaze Are Implemented (Special Award Contender)

```markdown
### How Hand Interactions & Gaze Are Implemented
KineticCAD XR is 100% controller-free end-to-end, combining direct physical manipulation, native Meta OpenXR Microgestures, and seated "Eyes & Hands" gaze-pinch ergonomics:

1. **Zero-Jitter Meta OpenXR Microgestures (`OVRHand.GetMicrogestureType`):**
   Eliminates "Heisenberg" pinch jitter by decoupling 3D index-finger positioning from command execution: **Thumb-Swipe Left/Right** scrubs mechanism width by $\pm 5.0\text{ mm}$, **Thumb-Swipe Forward** triggers topology synthesis, **Thumb-Swipe Backward** toggles Swept-Volume $\mathcal{W}_i$ recording, and **Thumb-Tap** executes **`[GHOST CARVE]`** Boolean subtraction on the payload housing block.
2. **Dual-Modal 6-Button Desk Bar (Workspace-Gated Poke + 260ms Dwell Gaze & Pinch):**
   Users can physically poke the 6 desk buttons (`-5.5 mm` depression + procedural 35 ms mechanical switch click) or look within a $6.2^\circ$ FoV gaze cone and hold a 260 ms lap pinch for effortless seated control.
3. **Bimanual $1\text{€}$ Caliper & Index-Only Pose Disambiguation:**
   Evaluates finger curl ratios on `XRHandSubsystem` and `OVRSkeleton` for intentional index-finger puppeteering and orthogonal tabletop caliper measurement.

### What Excites Us About the Future of VR
Hands-first Mixed Reality + OpenXR Microgestures transform spatial computing into a **precision physical engineering superpower**—letting creators measure real hardware, carve zero-clash swept-volume housings, and export verified robotics packages with their bare hands.
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
