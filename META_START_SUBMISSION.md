# Meta VR Start Developer Competition 2026 — Complete Winning Submission Kit

---

## 1. Core Submission Fields (Copy & Paste into Devpost)

### **Submission Name**
```text
KineticCAD XR: Hands-First Spatial Robotics & CAD Studio
```

### **Submission Tagline** *(138 / 140 characters)*
```text
Hands-first MR spatial CAD studio: pinch-measure desk objects, synthesize 4 robot topologies, pick-and-place at 90Hz, and export URDFs.
```

### **Track**
* **Productivity** *(Multi-Panel Creative & Engineering Studio / Seated Desk Workspace)*

### **Division**
* **New Experience** *(Conceived and built from scratch after September 24, 2026)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store)

---

## 2. Project Description *(492 words — strictly under the 500-word limit, hitting all 4 Judging Pillars & Special Awards)*

```markdown
### Inspiration
Designing custom robotic mechanisms and articulated end-effectors for real-world objects traditionally requires days of fragmented desktop work: measuring parts with calipers, modeling in flat 2D CAD, deriving forward/inverse kinematics matrices by hand, and writing XML URDF files for simulation. We asked: *What if your physical desk and your bare hands were the entire robotics CAD studio?*

We built **KineticCAD XR** for Meta Quest to transform any seated desk into a multi-panel, hands-first spatial engineering studio—collapsing days of mechanical design and kinematic math into a three-minute mixed reality workflow.

### How We Built It (Engineered for Meta’s 4 Judging Pillars)
1. **Seated & Airplane-Seat Optimized (MRUK Table Locking & Left-Hand Calibration):**
   Every interaction lives within a compact 0.5-meter tabletop workspace directly in front of the seated user at 90 Hz. Using Meta’s **MRUK** scene understanding (`DeskWorkspaceInitializer`), the workbench automatically locks onto the user’s physical table surface, or calibrates flush to their real tabletop in Insight Passthrough by resting the left hand on the desk and poking **Reset Pose**.

2. **3-Panel Curved Spatial Productivity Workspace & Reachability Envelope:**
   Three high-contrast dark-slate floating panels surround the workspace above a 20-segment **Kinematic Reachability Envelope Ring** projected onto the desk:
   - **Left Panel (PoE Screw & Torque Spec Sheet):** Displays active robot topology, reach radius, link mass ($\text{kg}$), and static peak torque ($\text{N}\cdot\text{m}$).
   - **Center Panel (Main Telemetry, 4-Step Checklist & 5-Level Challenge):** Tracks live onboarding (`[✓]Synth -> [✓]Puppet -> [✓]Pick&Place -> [✓]Audit/URDF`) and a **5-Level Spatial Robotics Challenge** with star ratings (`★★★`) scored by minimum Yoshikawa manipulability maintained during transport.
   - **Right Panel (ROS 2 / Isaac Sim URDF Inspector):** Displays the generated REP-103 `<robot>` XML hierarchy and live 60 Hz `/joint_states` UDP telemetry stream directly inside Mixed Reality.

3. **Complete Workflow in Under 3 Minutes ("The One Bus Stop Test"):**
   - **Bimanual Spatial Caliper:** Hold both index fingers on either side of a real object on your desk to measure and lock its millimeter width with tactile audio ticks.
   - **Multi-Topology Synthesis:** Synthesizes 4 distinct 3-DoF industrial topologies (**Articulated 3R Claw**, **SCARA Selective Arm**, **Heavy Turret Palletizer**, and **Precision Wrist Probe**) with articulated parallel gripper jaws scaled to your object.
   - **Reach-Scaled Pick-and-Place Loop:** Guide the parallel jaws with your right index fingertip over the desk workpiece to automatically clamp, lift, and deliver it to the Drop Zone pad across 5 engineering challenge levels.

4. **100% Entrant-Built Kinematics Core ("The Take It Away Test"):**
   Even completely offline with zero cloud connectivity, KineticCAD XR runs a custom double-precision C# **Product of Exponentials (PoE)** screw-theory engine in $se(3)$, a **Damped Least-Squares (DLS)** analytical inverse kinematics solver ($\lambda = 0.06$) at 90 FPS, and real-time **Yoshikawa manipulability** ($\mu = \sqrt{\det(JJ^T)}$) shader feedback that shifts link colors from Cyan (`OPTIMAL DEXTERITY`) to Warning Red (`SINGULARITY RISK`).

### Future Plans for Improvement
- **Direct 3D Printing (STL/OBJ) Export:** One-poke export of watertight 3D-printable link meshes alongside the ROS 2 URDF.
- **Co-Located Multi-Engineer Rooms:** Shared spatial anchors allowing two engineers to inspect and puppet the same mechanism simultaneously.
```

---

## 3. How Hand Interactions & Gaze Are Implemented (Special Award Contender)

```markdown
### How Hand Interactions & Gaze Are Implemented
KineticCAD XR is 100% controller-free end-to-end, combining direct physical manipulation with seated "Eyes & Hands" gaze-pinch ergonomics:

1. **Dual-Modal Button Activation (Workspace-Gated Poke + 260ms Dwell Gaze & Pinch):**
   - **Direct Tactile Poke:** Users can physically poke the 5 desk buttons with either index finger; buttons highlight on hover, physically depress `-5.5 mm` into the desk, and fire a procedural 35 ms dual-tone mechanical switch click (`SpatialTactileAudio`). Spatial workspace gating prevents button pokes along the front bar from dragging the robot IK target.
   - **Eyes & Hands (Gaze + 260ms Dwell Lap Pinch):** For effortless seated use ("Airplane Seat Test"), looking at any action button within a $6.2^\circ$ FoV gaze cone and holding a thumb-to-index pinch for `260 ms` smoothly charges and triggers the button while resting hands in the lap (with `Reset Pose` excluded from gaze-pinch to prevent accidental resets).
2. **Index-Only Pose Disambiguation (`HandFingertipReader`):**
   Evaluates finger curl ratios on OpenXR `XRHandSubsystem` and `OVRSkeleton` so fingertip puppeteering only engages when the index finger is deliberately extended and middle/ring/pinky fingers are curled.
3. **Bimanual Precision Caliper (`CaliperController`):**
   Extending both index fingers spawns a One-Euro filtered ($f_{min}=1.0\text{ Hz}, \beta=0.003$) caliper bridge projected orthogonally onto the desk plane to eliminate vertical tilt bias, complete with 5 mm ratchet ticks and a harmonic C6->G6 lock chime.

### What Excites Us About the Future of VR
Hands-first Mixed Reality transforms spatial computing from passive media consumption into a **physical engineering superpower**—letting creators measure the real world with bare hands and immediately synthesize, test, and export real robotics hardware.
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
