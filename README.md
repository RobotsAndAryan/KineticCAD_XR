# KineticCAD XR

> **Neuro-Symbolic Spatial Mechanism & Physical AI Synthesizer on Meta Quest 3**  
> *Built for the Nebius × NVIDIA Global AI Hackathon (Physical AI Track) & Meta Quest Spatial Computing*

[![Platform](https://img.shields.io/badge/Platform-Meta%20Quest%203%20%7C%20Unity%206-blue.svg)](https://unity.com/)
[![NVIDIA](https://img.shields.io/badge/NVIDIA-Nemotron--3%20(120B%20%2F%20550B)-76B900.svg)](https://build.nvidia.com/)
[![Nebius](https://img.shields.io/badge/Nebius-Token%20Factory%20API-6C5CE7.svg)](https://tokenfactory.nebius.com/)
[![Kinematics](https://img.shields.io/badge/Kinematics-PoE%20Screw%20Theory%20%2B%20DLS%20IK-00CEC9.svg)]()
[![Hardware Bridge](https://img.shields.io/badge/Hardware%20Bridge-60Hz%20UDP%20%7C%20URDF%20%7C%20ROS%20%7C%20Isaac%20Sim-E17055.svg)]()

---

## 🌟 Overview

**KineticCAD XR** bridges the gap between human spatial intuition, foundation models, and deterministic robotics. Instead of manually typing mechanical parameters into static CAD software, engineers use **Meta Quest 3 Color Passthrough** to inspect physical objects in their workspace, measure target tolerances with a **bimanual index caliper**, and synthesize spatial robotic mechanisms via **NVIDIA Nemotron foundation models** served through the **Nebius Token Factory API**.

The resulting mechanism mounts directly onto the physical desk, can be **puppeted in real time at 60–90 Hz via Damped Least-Squares (DLS) Inverse Kinematics**, exports production-grade analytical `.urdf` files, and broadcasts live joint state telemetry over UDP to ROS and NVIDIA Isaac Sim.

---

## 🏗️ System Architecture

```
+---------------------------------------------------------------------------------------------------+
|                                        KINETICCAD XR ARCHITECTURE                                 |
+---------------------------------------------------------------------------------------------------+
|  [ Quest 3 Color Passthrough ]                                                                    |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ Tabletop Spatial Anchor ]  ──> Places DeskWorkspaceOrigin directly onto the physical desk      |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ Bimanual Index Caliper ]   ──> One-Euro Filtered (fc_min=1.0Hz, beta=0.003)                   |
|         │                         Strict Index-pointing pose check (middle/ring/pinky curled)     |
|         │                         Outputs precise physical target mm (15 mm - 320 mm)             |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ 3D Desk Poke Bar / Touch ] ──> 5 Desk Buttons (5.2 cm touch radius, white pop feedback)        |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ Nebius AI Synthesis ]      ──> Token Factory API (nvidia/nemotron-3-super-120b-a12b / 550b)    |
|         │                         < 1 ms deterministic analytical fallback if offline             |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ Symbolic Kinematics Core ] ──> Analytical Screw Axis + Product of Exponentials (PoE) FK        |
|         │                         Yoshikawa Manipulability Analysis: mu(theta) = sqrt(det(J*J^T)) |
|         │                         Damped Least-Squares (DLS) IK at 60-90 Hz (lambda^2 = 0.001)    |
|         │                         Real-time Right Index Fingertip Tracking & Puppeting            |
|         │                                                                                         |
|         ▼                                                                                         |
|  [ Physical AI Pipeline Out ] ──> Analytical .URDF Generation & Local Storage                     |
|                               ──> 60 Hz UDP Joint State Telemetry (Port 9870)                     |
|                               ──> Tools/udp_to_joint_states.py (ROS / Isaac Sim bridge)           |
+---------------------------------------------------------------------------------------------------+
```

---

## 🔬 Core Capabilities

### 1. Spatial Measurement (Bimanual Caliper)
- **One-Euro Filtered Planar Projection:** Measures Euclidean distance between left and right index fingertips, projecting onto the tabletop plane ($XZ$) to eliminate hand-tilt hypotenuse distortion.
- **Strict Pointing Pose Detection:** Activates only when the user extends the index finger straight while curling the middle, ring, and pinky fingers.
- **Visual Stability Locking:** Real-time 3D caliper bar with floating millimeter label that turns green (`LOCKED: xx.x mm`) after holding steady for $0.70\text{ s}$.

### 2. Neuro-Symbolic Generative AI
- **Dual-Tier NVIDIA Nemotron via Nebius API:**
  - `nvidia/nemotron-3-super-120b-a12b`: Sub-second rapid topology and link length exploration.
  - `nvidia/nemotron-3-ultra-550b-a55b`: Deep mechanical verification, singularity avoidance, and payload feasibility audit.
- **Deterministic Offline Fallback:** Fully analytical $< 1\text{ ms}$ fallback mechanism synthesis ensuring zero disruption if Wi-Fi or API tokens drop.
- **3D HUD Diagnostics:** Live display of API latency ($ms$), completion tokens, Wi-Fi status, and active model.

### 3. Real-Time Robotics & Kinematic Puppeting
- **Product of Exponentials (PoE) Formulation:** Exact screw-theory forward kinematics in $se(3)$ using matrix exponentials.
- **Damped Least-Squares (DLS) IK:** Real-time Jacobian inverse solving $\Delta \boldsymbol{\theta} = J^T (J J^T + \lambda^2 I)^{-1} \Delta \mathbf{x}$ ($\lambda^2 = 0.001$), eliminating singularity instabilities.
- **Yoshikawa Manipulability Ellipsoid Analysis:** Live computation of $\mu(\boldsymbol{\theta}) = \sqrt{\det(J J^T)}$, dynamically shifting arm link colors from Cyan (optimal manipulability) to Orange/Red (near kinematic singularity).
- **Sub-Centimeter 1:1 Hand Puppeting:** Solved the double-translation root pose offset in Quest 3 bone hierarchies so the red/orange end-effector target sphere tracks directly on your physical right index fingertip.

### 4. Physical AI Export & Simulation Bridge
- **Standardized URDF Export:** Generates clean XML `.urdf` robot descriptions with links, visual meshes, revolute joints, and axis vectors on button press.
- **60 Hz UDP Joint State Telemetry:** Streams real-time joint states on UDP port `9870`.
- **Python Bridge (`Tools/udp_to_joint_states.py`):** Converts incoming UDP stream directly into ROS / ROS 2 `sensor_msgs/JointState` or feeds directly into NVIDIA Isaac Sim and Omniverse.

---

## 🚀 Quickstart Guide

### Running on Meta Quest 3
1. Connect your Meta Quest 3 via USB-C or Wi-Fi (enable Developer Mode).
2. Install the pre-built APK via ADB or SideQuest:
   ```bash
   adb install -r unity_vr_apps/KineticCAD_antiGrav.apk
   ```
3. Put on the Quest 3 and launch **KineticCAD XR** from the Unknown Sources menu.
4. Point both index fingers to measure any object on your desk, or poke the 3D buttons along the front desk edge to synthesize and puppet the robotic mechanism.

### Running the Python ROS 2 / Isaac Sim / Standalone Telemetry Bridge
In a terminal on your host machine (works out-of-the-box on Windows, macOS, and Linux **with or without** `rclpy` installed):
```bash
python Tools/udp_to_joint_states.py --port 9870
```
- **If ROS 2 (`rclpy`) is installed:** Publishes live `/joint_states` (`sensor_msgs/msg/JointState`) and `/cmd_vel` (`geometry_msgs/msg/Twist`) at 60 Hz.
- **If running on Windows / standalone Python:** Automatically launches a live 60 Hz terminal telemetry dashboard displaying real-time joint angles (`J1`, `J2`, `J3` in degrees), Yoshikawa manipulability $\mu(\boldsymbol{\theta})$, and singularity health.
- **Pre-exported Reference URDFs:** Ready-to-inspect `.urdf` files with analytical inertia tensors ($I_{xx}, I_{yy}, I_{zz}$) are included in [`Exports/`](Exports/) (`kinetic_claw_70mm.urdf`, `kinetic_claw_145mm.urdf`), and pressing **EXPORT URDF** in the Editor also writes directly to `Exports/`.
- **Hackathon Pitch & Devpost Guide:** See [`HACKATHON_SUBMISSION.md`](HACKATHON_SUBMISSION.md) for full submission copy and the 2-minute demo script.

---

## 📦 Project Structure

```
KineticCAD_XR/
├── Assets/
│   ├── KineticCore/
│   │   ├── AI/                 # NebiusKinematicSynthesizer (Token Factory API)
│   │   ├── CaliperController.cs# Bimanual index measurement & One-Euro filter
│   │   ├── Editor/             # Build validation & pre-build shader cleaner
│   │   ├── Export/             # UdpJointStateBroadcaster & UrdfSerializer
│   │   ├── Input/              # HandFingertipReader & strict pointing pose logic
│   │   ├── Kinematics/         # PoE Forward Kinematics, ScrewAxis, DLS IK
│   │   ├── Runtime/            # KineticMechanismController & QuestHandAndCommandBridge
│   │   └── Schema/             # MechanismGraph JSON schema
│   ├── Resources/
│   │   ├── KineticSolidLit.shader  # Mobile single-pass stereo URP lit shader
│   │   └── KineticWorldText.shader # Mobile stereo 3D world-space text shader
│   └── Scenes/
│       └── SampleScene.unity   # Core production scene
├── Tools/
│   └── udp_to_joint_states.py  # 60 Hz UDP to ROS / Isaac Sim telemetry bridge
├── unity_vr_apps/              # Built Quest 3 APK binaries (Git LFS)
└── README.md
```

---

## 🏆 Hackathon Tracks & Recognition

- **Nebius × NVIDIA Global AI Hackathon:** Physical AI Track (NVIDIA Nemotron via Nebius Token Factory).
- **Meta Quest Spatial Computing:** Native Passthrough, Tabletop Scene Anchoring, and Hand Tracking Interaction.
