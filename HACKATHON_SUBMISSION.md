# KineticCAD XR — Official Hackathon Submission Guide & Pitch Kit

> **Track:** Nebius × NVIDIA Global AI Hackathon (Physical AI Track) & Meta Quest Spatial Computing  
> **Repository:** `https://github.com/RobotsAndAryan/KineticCAD_XR`

---

## 1. Ready-to-Paste Submission Form Copy (Devpost / Hackathon Portal)

### Project Title
**KineticCAD XR: Neuro-Symbolic Spatial Mechanism & Physical AI Synthesizer**

### Short Tagline / Elevator Pitch (Under 140 characters)
Measure real objects in Quest 3 Passthrough, synthesize 3-DoF robots via NVIDIA Nemotron on Nebius, puppet via DLS IK, and stream 60Hz URDF to ROS 2.

### Inspiration
Generative AI has transformed software engineering, yet **Physical AI and robotics** still suffer from a fundamental *spatial grounding gap*. Designing a custom robotic manipulator or inspection mechanism for a real-world object still requires manually measuring dimensions with physical calipers, typing parameters into desktop CAD software, hand-deriving forward/inverse kinematics, and manually authoring XML `.urdf` files.

We built **KineticCAD XR** to collapse that entire multi-day engineering pipeline into **seconds** directly on the engineer's physical desk using **Meta Quest 3 Mixed Reality** and **NVIDIA Nemotron Foundation Models on Nebius Token Factory**.

### What It Does
1. **Bimanual Spatial Caliper in Passthrough:** Using Meta Quest 3 Color Passthrough and hand skeleton tracking, the engineer holds out both index fingers across any real object on their desk. Our orthogonal tabletop-projected caliper filters hand tremor via a **One-Euro Filter** ($f_{c,\min}=1.0\text{ Hz}, \beta=0.003$) and locks the exact millimeter span.
2. **Dual-Tier NVIDIA Nemotron Synthesis via Nebius Token Factory:**
   - **Fast Synthesis (`nvidia/nemotron-3-super-120b-a12b`):** Generates a complete 3-DoF spatial robotic mechanism topology (`MechanismSpec` JSON: link lengths, masses, inertia tensors, screw axes, and joint limits) tailored to the measured object width in sub-second latency.
   - **Deep Engineering Audit (`nvidia/nemotron-3-ultra-550b-a55b`):** Runs a structural and control audit verifying Kutzbach mobility ($M=3\text{ DoF}$), peak static gravity torque ($\text{N}\cdot\text{m}$), and recommended ROS 2 PD controller gains ($K_p, K_d$).
3. **Symbolic Screw Theory & 60–90 Hz Damped Least-Squares (DLS) IK:**
   - Converts the synthesized spec into an exact **Product of Exponentials (PoE)** kinematic chain in $SE(3)$:
     $$T(\boldsymbol{\theta}) = e^{[\mathcal{S}_1]\theta_1} e^{[\mathcal{S}_2]\theta_2} e^{[\mathcal{S}_3]\theta_3} M$$
   - Tracks the engineer's right index fingertip in real time using **Damped Least-Squares Inverse Kinematics** ($\Delta\boldsymbol{\theta} = J^T(JJ^T + \lambda^2 I)^{-1}\mathbf{e}$) with continuous **Yoshikawa Manipulability** ($\mu(\boldsymbol{\theta}) = \sqrt{\det(JJ^T)}$) color-coding every link from Healthy Cyan to Warning Red near singularities.
4. **Zero-Friction Sim-to-Real Export (`.urdf` + 60 Hz UDP Stream):**
   - Poking the physical 3D **EXPORT URDF** button on the desk serializes a compliant ROS 2 / NVIDIA Isaac Sim `.urdf` file with analytical box inertia matrices ($I_{xx}, I_{yy}, I_{zz}$) and streams live 60 Hz joint states over UDP port `9870` (`Tools/udp_to_joint_states.py`).

### How We Built It
- **Spatial Computing & Rendering:** Unity 6 (`6000.0.6f1`), Meta XR Core & Interaction SDK (`OVRPassthroughLayer`, `OVRSkeleton`, `MRUK` tabletop anchoring), OpenXR `XRHandSubsystem`, and custom single-pass stereo mobile URP shaders (`KineticSolidLit.shader`, `KineticWorldText.shader`).
- **Neuro-Symbolic AI Layer:** Direct C# async REST integration with `https://api.tokenfactory.nebius.com/v1/chat/completions` querying `nvidia/nemotron-3-super-120b-a12b` and `nvidia/nemotron-3-ultra-550b-a55b`, paired with a $<1\text{ ms}$ deterministic parametric fallback compiler so the spatial loop never stalls even offline.
- **Zero-Allocation Robotics Math Engine:** Custom pure-C# double-precision `Vec3`, `Mat4`, `ScrewAxis` (Rodrigues' formula & screw exponential), `PoEForwardKinematics`, and `DlsInverseKinematics` (space-to-body Jacobian & $3\times 3$ analytical Cramer inversion).
- **ROS 2 & Isaac Sim Telemetry Bridge:** `UdpJointStateBroadcaster.cs` broadcasting 60 Hz JSON packets to `Tools/udp_to_joint_states.py` (supports both native `rclpy` `/joint_states` + `/cmd_vel` publishing and a standalone 60 Hz live console dashboard on Windows/macOS/Linux).

### Challenges We Ran Into & Solved
- **Sub-Centimeter 1:1 Hand Skeleton Alignment:** `OVRCameraRig` already applies the hand's `RootPose` to `LeftHandAnchor` and `RightHandAnchor`. Enabling `_updateRootPose` on `OVRSkeleton` caused a double-translation offset. Locking `OVRSkeleton` to local `(0,0,0)` inside the hand anchor achieved exact 1:1 physical fingertip alignment.
- **Intentional Gesture Disambiguation:** To prevent accidental caliper triggers while puppeteering the robot arm, we implemented a geometric curl classifier (`IsOvrIndexOnlyPointing` / `IsOpenXrIndexOnlyPointing`) that activates fingertip tools only when the index finger is extended straight ($>11.5\text{ cm}$ from wrist) and the middle, ring, and pinky fingers are curled ($<84\%$ reach).
- **Single-Pass Stereo Shader Stripping on Quest 3:** Built custom URP HLSL shaders with `UNITY_VERTEX_INPUT_INSTANCE_ID` and `UNITY_VERTEX_OUTPUT_STEREO` stored in `Assets/Resources/` so 3D world text and dynamic `MaterialPropertyBlock` link colors render crisply at 90 Hz on Quest 3's Adreno GPU.

### Built With
`nvidia-nemotron`, `nebius-token-factory`, `unity-6`, `c#`, `meta-quest-3`, `openxr`, `ros2`, `nvidia-isaac-sim`, `urdf`, `python`, `hlsl`

---

## 2. 2-Minute Winning Video Recording Script

Record two streams (or picture-in-picture): **Quest 3 Passthrough recording** (via Meta Quest Developer Hub / `adb exec-out screenrecord`) + **PC Terminal / Editor screen**.

| Timestamp | Visual Action | Voiceover Script |
|---|---|---|
| **0:00 – 0:20** | Put on Quest 3 looking at your real desk with a physical object (e.g., mug or box) next to the virtual robot arm and 5 glowing desk buttons. | *"Designing a custom robot manipulator for a physical task usually takes days of manual CAD, math derivation, and URDF coding. This is KineticCAD XR: a neuro-symbolic Physical AI synthesizer running live on Meta Quest 3 and powered by NVIDIA Nemotron on Nebius."* |
| **0:20 – 0:50** | Point both index fingers (other fingers curled) across the real object on your desk. Show the cyan 3D caliper bar and floating millimeter readout lock green (`CALIPER LOCKED`). | *"First, we ground the AI in physical reality. Using our bimanual index caliper with One-Euro tremor filtering and orthogonal plane projection, I measure this physical object directly on my desk. Holding still for 0.7 seconds locks the exact millimeter constraint."* |
| **0:50 – 1:20** | Poke the cyan **FAST SYNTH** button (and then the purple **DEEP AUDIT** button) on the desk. Show the arm dynamically re-synthesize and perform its gold kinematic inspection sweep. | *"Poking Fast Synth sends our physical measurement to NVIDIA Nemotron Super 120B via Nebius Token Factory, synthesizing a 3-DoF mechanism topology in real time. Poking Deep Audit invokes Nemotron Ultra 550B to audit Kutzbach mobility, peak gravity torque, and ROS 2 PD gains."* |
| **1:20 – 1:45** | Point your right index finger and move it through 3D space above the desk. The robot arm tracks your fingertip smoothly at 90 Hz, shifting color between Cyan and Red. | *"Every frame, our Product-of-Exponentials Screw Theory and Damped Least-Squares IK solver puppets the synthesized arm at 90 Hz—while Yoshikawa manipulability dynamically colors the links from cyan to warning red near singularities."* |
| **1:45 – 2:10** | Poke the green **EXPORT URDF** button (arm flashes emerald green). Show `python Tools/udp_to_joint_states.py` running in a terminal streaming 60 Hz joint angles, plus the exported `.urdf` in `Exports/`. | *"Finally, poking Export URDF compiles an analytical URDF with full inertia tensors while streaming 60 Hz joint states over UDP port 9870 straight to ROS 2 and NVIDIA Isaac Sim. From physical object to sim-ready robot in under two minutes."* |
