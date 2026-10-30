# Meta VR Start Developer Competition 2026 — Complete Submission Kit

---

## 1. Core Submission Fields (Copy & Paste into Devpost)

### **Submission Name**
```text
KineticCAD XR: Hands-First Spatial Robotics & CAD Studio
```

### **Submission Tagline** *(136 / 140 characters)*
```text
Hands-first MR spatial CAD studio: pinch-measure real desk objects, synthesize 3-DoF mechanisms, puppet at 90Hz, and export ROS 2 URDFs.
```

### **Track**
* **Productivity** *(Creative & Engineering Tools / Seated Desk Workspace)*

### **Division**
* **New Experience** *(Conceived and built from scratch after September 24, 2026)*

### **Target Launch Date**
* **Q1 2027** (Meta Horizon Store / App Lab)

---

## 2. Project Description *(465 words — strictly under the 500-word limit)*

```markdown
### Inspiration
Designing custom robotic mechanisms and articulated end-effectors for real-world objects traditionally requires days of fragmented work: measuring parts with physical calipers, modeling in flat 2D desktop CAD, deriving forward/inverse kinematics matrices by hand, and writing XML URDF files for simulation. We asked: *What if your physical desk and your bare hands were the entire robotics CAD studio?*

We built **KineticCAD XR** for Meta Quest to transform any seated desk into a hands-first spatial engineering workbench—collapsing days of mechanical design and kinematic math into a five-minute mixed reality workflow.

### How We Built It (Seated, Hands-First & On-Device Core)
KineticCAD XR is engineered specifically around Meta’s three core design pillars:

1. **The Airplane Seat Test (Seated & Stationary):**
   Every interaction lives within a compact 0.5-meter tabletop workspace directly in front of the seated user. Placing your left hand on your physical desk and poking **Reset Pose** snaps the virtual workbench flush to your real tabletop in Insight Passthrough.

2. **The One Bus Stop Test (Complete Workflow in Under 3 Minutes):**
   - **Bimanual Spatial Caliper:** Hold both index fingers on either side of a real physical object on your desk. A One-Euro filtered laser bridge measures the object in millimeters with tactile audio ticks and locks the dimension automatically.
   - **Neuro-Symbolic Synthesis:** Poking **Fast Synth** synthesizes a custom 3-DoF mechanism scaled to your physical object, with an instant on-device parametric synthesizer ensuring zero-latency offline operation.
   - **90 FPS Fingertip Puppeteering:** Pointing your right index finger directly drives the robot end-effector in 3D space.
   - **One-Poke URDF & 60 Hz Telemetry Export:** Poking **Export URDF** generates a ROS REP-103 compliant URDF with analytical 3D inertia tensors while broadcasting live 60 Hz joint states over UDP.

3. **The "Take It Away" Test (100% Entrant-Built Kinematics Core):**
   Even completely offline with zero cloud connectivity, KineticCAD XR runs a custom double-precision C# **Product of Exponentials (PoE)** screw-theory engine in $se(3)$, a **Damped Least-Squares (DLS)** analytical inverse kinematics solver ($\lambda = 0.06$), and real-time **Yoshikawa manipulability** ($\mu = \sqrt{\det(JJ^T)}$) shader feedback that shifts link colors from Cyan to Warning Red near kinematic singularities.

### Future Plans for Improvement
- **Multi-Contact Grasp Simulation:** On-device rigid-body contact testing to verify grip stability on scanned desk meshes.
- **Direct 3D Printing (STL/OBJ) Export:** One-poke export of watertight 3D-printable link meshes alongside the URDF.
- **Shared Spatial Engineering Rooms:** Multi-user co-located mixed reality sessions allowing two engineers to inspect and puppet the same mechanism simultaneously.
```

---

## 3. How Hand Interactions Are Implemented & What Excites Us About VR

```markdown
### How Hand Interactions Are Implemented
KineticCAD XR is 100% controller-free end-to-end, built on OpenXR `XRHandSubsystem` and `OVRHand`/`OVRSkeleton` joint tracking:

1. **Index-Only Pose Disambiguation (`HandFingertipReader`):**
   To prevent accidental triggers while resting hands on a desk, our pose classifier evaluates finger curl ratios in real time—activating interaction only when the index finger is extended straight and the middle, ring, and pinky fingers are curled.
2. **Bimanual Precision Caliper (`CaliperController`):**
   Extending both index fingers spawns a holographic caliper bridge between the user's real fingertips. We project fingertip coordinates orthogonally onto the desk plane to eliminate vertical tilt bias and apply a One-Euro filter ($f_{min}=1.0\text{ Hz}, \beta=0.003$) for sub-millimeter stability.
3. **Physical 3D Poke Buttons & Procedural Tactile Audio (`SpatialTactileAudio`):**
   Because hands lack controller vibration motors, we engineered a zero-latency procedural audio synthesizer in C# paired with physical button depression. Hovering over a desk button triggers a soft 1850 Hz micro-tick and highlight; pressing down physically depresses the 3D button into the desk and fires a crisp dual-tone mechanical switch click.

### What Excites Us About the Future of VR
Hands-first Mixed Reality turns spatial computing from a content-consumption medium into a true **physical superpower**. Being able to reach out with bare hands, measure a physical object on a real desk, and immediately manipulate mathematical kinematics in 3D space represents the future of human-centered engineering and spatial productivity.
```

---

## 4. Step-by-Step Guide: Uploading to the Meta "Competition" Release Channel

Meta judges **must** be able to install your build via a Meta Horizon Invite URL. Follow these exact steps (takes ~10 minutes):

1. **Open the Meta Horizon Developer Dashboard:**
   * Go to [https://developers.meta.com/horizon/manage/](https://developers.meta.com/horizon/manage/) and sign in with your Meta Developer account.
2. **Create or Select Your App:**
   * Click **Create App** → Name it **`KineticCAD XR`** → Select **Meta Quest** (Unity / Native).
3. **Navigate to Release Channels:**
   * In the left sidebar, click **Distribution → Release Channels**.
   * Click **Create Channel** and name it exactly:
     ```text
     Competition
     ```
4. **Upload the Build (`KineticCAD_final_v2.apk`):**
   * Inside the **`Competition`** channel, click **Upload Build** (or use **Meta Quest Developer Hub (MQDH)** → App Distribution → Upload APK to `Competition` channel).
   * Select `e:\Unity\KineticCAD_XR\unity_vr_apps\KineticCAD_final_v2.apk` (or build a fresh v3 APK in Unity via **File → Build Profiles → Meta Quest → Build**).
5. **Generate the Judge Invite URL:**
   * In the **`Competition`** channel, go to the **Users / Testers** tab.
   * Click **Invite Users via URL** (enable the shareable link).
   * Copy the generated `https://www.meta.com/s/...` Invite URL and paste it into the Devpost submission form!
