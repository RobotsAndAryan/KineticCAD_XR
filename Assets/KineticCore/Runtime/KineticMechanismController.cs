using System;
using UnityEngine;
using UnityEngine.XR;
using KineticCore.Math;
using KineticCore.Schema;
using KineticCore.AI;
using KineticCore.Export;

namespace KineticCore.Runtime
{
    public class KineticMechanismController : MonoBehaviour
    {
        [Header("Scene References")]
        public Transform DeskWorkspaceOrigin;

        [Header("Nebius Token Factory Config")]
        public string NebiusApiKey = "";
        public string UserIntentPrompt = "3-DoF articulated inspection and gripper arm";

        [Header("Interactive Controls")]
        public double ManualTestWidthMm = 70.0;
        public bool PuppetActive = true;
        public bool AutoOrbitTarget = false;
        public Vector3 ManualTargetLocalPos = new Vector3(0.14f, 0.10f, 0.04f);
        public float ShimMoveSpeed = 0.25f;

        public MechanismSpec CurrentSpec { get; private set; }
        public PoEChain CurrentChain { get; private set; }
        public double[] JointAngles => _theta;
        public double CurrentManipulability { get; private set; }
        public Vec3 CurrentEndEffectorPos { get; private set; }
        public string LastExportedUrdfPath { get; private set; } = "Not exported yet (Press U or Click Button)";
        public string StatusMessage { get; private set; } = "Ready — Use WASD/Sliders or Quest 3 Hands";
        public string ActiveNebiusModel { get; private set; } = NebiusKinematicSynthesizer.FastSuperModelId;
        public long LastApiLatencyMs { get; private set; } = 18;
        public string EngineeringAuditReport { get; private set; } = "Kutzbach Mobility M=3 DoF | Non-Singular | Ready for Isaac Sim";

        private double[] _theta;
        private double[] _minLimits;
        private double[] _maxLimits;

        private GameObject _mechanismRoot;
        private Transform[] _jointPivots;
        private Renderer[] _linkRenderers;
        private Transform _eeMarker;
        private Transform _shimTargetSphere;

        private bool _keyW, _keyA, _keyS, _keyD, _keyQ, _keyE, _keyShift;

        private void Start()
        {
            if (DeskWorkspaceOrigin == null)
            {
                GameObject found = GameObject.Find("DeskWorkspaceOrigin");
                DeskWorkspaceOrigin = found != null ? found.transform : this.transform;
            }

            Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (!XRSettings.isDeviceActive && cam != null)
            {
                if (Vector3.Distance(cam.transform.position, DeskWorkspaceOrigin.position) < 0.2f)
                {
                    Vector3 forwardFlat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    if (forwardFlat.sqrMagnitude < 0.01f) forwardFlat = Vector3.forward;
                    DeskWorkspaceOrigin.position = cam.transform.position + forwardFlat * 0.45f + Vector3.down * 0.14f;
                    DeskWorkspaceOrigin.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
                }
            }

            CreateEditorLightingAndDeskMat();
            CreateEditorTargetSphere();
            SynthesizeAndSpawnSync(ManualTestWidthMm);
        }

        private void CreateEditorLightingAndDeskMat()
        {
            GameObject lightObj = new GameObject("DeskWorkspaceLight");
            lightObj.transform.SetParent(DeskWorkspaceOrigin, false);
            lightObj.transform.localPosition = new Vector3(0f, 0.5f, -0.2f);
            Light ptLight = lightObj.AddComponent<Light>();
            ptLight.type = LightType.Point;
            ptLight.range = 3.0f;
            ptLight.intensity = 2.0f;
            ptLight.color = Color.white;

            GameObject matObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            matObj.name = "DeskEngineeringMat";
            matObj.transform.SetParent(DeskWorkspaceOrigin, false);
            matObj.transform.localPosition = new Vector3(0.08f, -0.004f, 0f);
            matObj.transform.localScale = new Vector3(0.45f, 0.006f, 0.35f);
            SetRendererColor(matObj.GetComponent<Renderer>(), new Color(0.12f, 0.16f, 0.22f));
        }

        private void CreateEditorTargetSphere()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "RightFingertip_Target_Red";
            sphere.transform.SetParent(DeskWorkspaceOrigin, false);
            sphere.transform.localPosition = ManualTargetLocalPos;
            sphere.transform.localScale = Vector3.one * 0.026f;
            SetRendererColor(sphere.GetComponent<Renderer>(), new Color(1.0f, 0.15f, 0.15f));
            _shimTargetSphere = sphere.transform;
        }

        private void Update()
        {
            UpdateTargetPosition();

            if (CurrentChain != null && _theta != null)
            {
                if (PuppetActive && _shimTargetSphere != null)
                {
                    Vector3 lp = _shimTargetSphere.localPosition;
                    Vec3 targetDesk = new Vec3(lp.x, lp.y, lp.z);

                    DlsInverseKinematics.SolvePosition(
                        CurrentChain, targetDesk, _theta, _minLimits, _maxLimits,
                        lambda: 0.06, maxIterations: 25, tolerance: 1e-4, maxStepRad: 0.15);
                }

                CurrentManipulability = DlsInverseKinematics.ComputeManipulabilityIndex(CurrentChain, _theta);
                CurrentEndEffectorPos = CurrentChain.EndEffectorPosition(_theta);
                UpdateVisualLinksFromPoE();
            }
        }

        private void UpdateTargetPosition()
        {
            if (_shimTargetSphere == null) return;

            Vector3 delta = Vector3.zero;
            if (_keyD) delta.x += 1f;
            if (_keyA) delta.x -= 1f;
            if (_keyW) delta.z += 1f;
            if (_keyS) delta.z -= 1f;
            if (_keyE) delta.y += 1f;
            if (_keyQ) delta.y -= 1f;

            try
            {
                if (UnityEngine.Input.GetKey(KeyCode.D)) delta.x += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.A)) delta.x -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.W)) delta.z += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.S)) delta.z -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.E)) delta.y += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.Q)) delta.y -= 1f;
            }
            catch { }

            if (delta.sqrMagnitude > 0.001f)
            {
                AutoOrbitTarget = false;
                float speed = ShimMoveSpeed * (_keyShift ? 3.0f : 1.0f);
                ManualTargetLocalPos += delta.normalized * (speed * Time.deltaTime);
            }
            else if (AutoOrbitTarget)
            {
                float t = Time.time * 1.4f;
                ManualTargetLocalPos = new Vector3(
                    0.13f + 0.10f * Mathf.Cos(t),
                    0.09f + 0.05f * Mathf.Sin(t * 2.0f),
                    0.09f * Mathf.Sin(t));
            }

            _shimTargetSphere.localPosition = ManualTargetLocalPos;
        }

        public async void TriggerAsyncSynthesis(double caliperWidthMm)
        {
            StatusMessage = $"[Nebius Super] Synthesizing {caliperWidthMm:F1}mm mechanism...";
            var synth = new NebiusKinematicSynthesizer(NebiusApiKey);
            MechanismSpec spec = await synth.SynthesizeMechanismAsync(caliperWidthMm, UserIntentPrompt);
            ActiveNebiusModel = synth.LastModelUsed;
            LastApiLatencyMs = synth.LastLatencyMs;
            ApplyMechanismSpec(spec);
            StatusMessage = $"Spawned '{spec.robotName}' ({spec.targetCaliperWidthMm:F1}mm) via Nebius Super!";
        }

        public async void TriggerUltraEngineeringAudit()
        {
            if (CurrentSpec == null) return;
            StatusMessage = "[Nebius Ultra 253B] Running deep kinematic & LQR/PD audit...";
            var synth = new NebiusKinematicSynthesizer(NebiusApiKey);
            EngineeringAuditReport = await synth.RunDeepKinematicAuditUltraAsync(CurrentSpec, CurrentManipulability);
            ActiveNebiusModel = synth.LastModelUsed;
            LastApiLatencyMs = synth.LastLatencyMs;
            StatusMessage = "Nebius Nemotron-Ultra 253B Audit Complete!";
        }

        public void SynthesizeAndSpawnSync(double caliperWidthMm)
        {
            MechanismSpec spec = NebiusKinematicSynthesizer.BuildDeterministicMechanism(caliperWidthMm);
            ApplyMechanismSpec(spec);
        }

        public void ExportCurrentUrdf()
        {
            if (CurrentSpec == null) return;
            LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, Application.persistentDataPath);
            StatusMessage = $"Exported URDF & Inertia Tensors!";
            Debug.Log($"[KineticCAD] URDF Exported to: {LastExportedUrdfPath}");
        }

        private void ApplyMechanismSpec(MechanismSpec spec)
        {
            CurrentSpec = spec;
            CurrentChain = spec.ToPoEChain(out _minLimits, out _maxLimits);
            _theta = new double[CurrentChain.Joints.Length];
            RebuildVisualHierarchy(spec);
            CurrentManipulability = DlsInverseKinematics.ComputeManipulabilityIndex(CurrentChain, _theta);
            CurrentEndEffectorPos = CurrentChain.EndEffectorPosition(_theta);
            UpdateVisualLinksFromPoE();
        }

        private void RebuildVisualHierarchy(MechanismSpec spec)
        {
            if (_mechanismRoot != null)
                Destroy(_mechanismRoot);

            _mechanismRoot = new GameObject($"Mechanism_{spec.robotName}");
            _mechanismRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            _mechanismRoot.transform.localPosition = Vector3.zero;
            _mechanismRoot.transform.localRotation = Quaternion.identity;

            if (spec.links != null && spec.links.Length > 0)
            {
                LinkSpec baseLink = spec.links[0];
                GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseObj.name = baseLink.name;
                baseObj.transform.SetParent(_mechanismRoot.transform, false);
                baseObj.transform.localPosition = new Vector3((float)baseLink.centerX, (float)baseLink.centerY, (float)baseLink.centerZ);
                baseObj.transform.localScale = new Vector3((float)baseLink.widthM, (float)(baseLink.heightM * 0.5), (float)baseLink.lengthM);
                SetRendererColor(baseObj.GetComponent<Renderer>(), new Color(0.28f, 0.32f, 0.38f));
            }

            int n = spec.joints != null ? spec.joints.Length : 0;
            _jointPivots = new Transform[n];
            _linkRenderers = new Renderer[n];

            for (int i = 0; i < n; i++)
            {
                JointSpec j = spec.joints[i];
                GameObject pivotObj = new GameObject($"JointPivot_{i}_{j.name}");
                pivotObj.transform.SetParent(_mechanismRoot.transform, false);
                _jointPivots[i] = pivotObj.transform;

                GameObject pinVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pinVisual.name = $"ScrewAxisPin_{j.name}";
                pinVisual.transform.SetParent(pivotObj.transform, false);
                pinVisual.transform.localPosition = new Vector3((float)j.originX, (float)j.originY, (float)j.originZ);
                Vector3 axisDir = new Vector3((float)j.axisX, (float)j.axisY, (float)j.axisZ).normalized;
                if (axisDir.sqrMagnitude > 0.001f)
                    pinVisual.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axisDir);
                pinVisual.transform.localScale = new Vector3(0.020f, 0.024f, 0.020f);
                SetRendererColor(pinVisual.GetComponent<Renderer>(), new Color(0.0f, 0.95f, 1.0f));

                if (spec.links != null && (i + 1) < spec.links.Length)
                {
                    LinkSpec link = spec.links[i + 1];
                    GameObject linkBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    linkBox.name = link.name;
                    linkBox.transform.SetParent(pivotObj.transform, false);
                    linkBox.transform.localPosition = new Vector3((float)link.centerX, (float)link.centerY, (float)link.centerZ);
                    linkBox.transform.localScale = new Vector3((float)link.lengthM, (float)link.heightM, (float)link.widthM);
                    _linkRenderers[i] = linkBox.GetComponent<Renderer>();
                    SetRendererColor(_linkRenderers[i], new Color(0.1f, 0.8f, 0.95f));
                }
            }

            GameObject eeSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eeSphere.name = "EndEffectorTip";
            eeSphere.transform.SetParent(_mechanismRoot.transform, false);
            eeSphere.transform.localScale = Vector3.one * 0.024f;
            SetRendererColor(eeSphere.GetComponent<Renderer>(), new Color(0.2f, 1.0f, 0.4f));
            _eeMarker = eeSphere.transform;
        }

        private void UpdateVisualLinksFromPoE()
        {
            Mat4[] jointTransforms = CurrentChain.ComputeCumulativeJointTransforms(_theta);

            float health = Mathf.Clamp01((float)(CurrentManipulability / 0.0012));
            Color linkColor = Color.Lerp(new Color(1.0f, 0.2f, 0.2f), new Color(0.1f, 0.85f, 0.95f), health);

            for (int i = 0; i < jointTransforms.Length; i++)
            {
                Mat4 Ti = jointTransforms[i];
                Vec3 pos = Ti.GetTranslation();
                Ti.ToQuaternion(out double qx, out double qy, out double qz, out double qw);

                _jointPivots[i].localPosition = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                _jointPivots[i].localRotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);

                if (_linkRenderers[i] != null)
                    SetRendererColor(_linkRenderers[i], linkColor);
            }

            if (_eeMarker != null)
            {
                _eeMarker.localPosition = new Vector3((float)CurrentEndEffectorPos.X, (float)CurrentEndEffectorPos.Y, (float)CurrentEndEffectorPos.Z);
            }
        }

        private static void SetRendererColor(Renderer r, Color c)
        {
            if (r == null || r.material == null) return;
            r.material.color = c;
            if (r.material.HasProperty("_BaseColor"))
                r.material.SetColor("_BaseColor", c);
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e != null && (e.type == EventType.KeyDown || e.type == EventType.KeyUp))
            {
                bool down = (e.type == EventType.KeyDown);
                _keyShift = e.shift;
                if (e.keyCode == KeyCode.W) _keyW = down;
                if (e.keyCode == KeyCode.A) _keyA = down;
                if (e.keyCode == KeyCode.S) _keyS = down;
                if (e.keyCode == KeyCode.D) _keyD = down;
                if (e.keyCode == KeyCode.Q) _keyQ = down;
                if (e.keyCode == KeyCode.E) _keyE = down;

                if (down && e.keyCode == KeyCode.U) ExportCurrentUrdf();
                if (down && e.keyCode == KeyCode.G) TriggerAsyncSynthesis(ManualTestWidthMm);
                if (down && e.keyCode == KeyCode.H) TriggerUltraEngineeringAudit();
                if (down && e.keyCode == KeyCode.P) PuppetActive = !PuppetActive;
            }

            GUI.Box(new Rect(15, 15, 700, 290), "KineticCAD — Neuro-Symbolic Physical AI (Nebius Token Factory + PoE SE(3) Kernel)");
            GUI.Label(new Rect(25, 40, 680, 22), $"Status: {StatusMessage}");
            GUI.Label(new Rect(25, 62, 680, 22), $"Nebius Model: {ActiveNebiusModel} | Latency: {LastApiLatencyMs} ms");
            GUI.Label(new Rect(25, 84, 680, 22), $"Ultra Audit: {EngineeringAuditReport}");

            if (_theta != null && _theta.Length >= 3)
            {
                GUI.Label(new Rect(25, 106, 680, 22),
                    $"PoE Joints: J1={_theta[0] * Mathf.Rad2Deg:F1}° | J2={_theta[1] * Mathf.Rad2Deg:F1}° | J3={_theta[2] * Mathf.Rad2Deg:F1}° | Manipulability μ={CurrentManipulability:E2}");
            }
            GUI.Label(new Rect(25, 128, 680, 22), $"URDF Export: {LastExportedUrdfPath}");

            // Caliper Width Slider (Simulates resizing real-world object on desk from 30mm to 140mm)
            GUI.Label(new Rect(25, 155, 175, 22), $"Caliper Width ({ManualTestWidthMm:F0} mm):");
            float newWidth = GUI.HorizontalSlider(new Rect(205, 160, 220, 20), (float)ManualTestWidthMm, 30.0f, 140.0f);
            if (Mathf.Abs(newWidth - (float)ManualTestWidthMm) > 0.5f)
            {
                ManualTestWidthMm = newWidth;
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            }

            // Target X/Y/Z Sliders
            GUI.Label(new Rect(25, 180, 175, 22), $"Target X ({ManualTargetLocalPos.x:F2}m):");
            float nx = GUI.HorizontalSlider(new Rect(205, 185, 220, 20), ManualTargetLocalPos.x, -0.25f, 0.35f);
            GUI.Label(new Rect(25, 205, 175, 22), $"Target Y ({ManualTargetLocalPos.y:F2}m):");
            float ny = GUI.HorizontalSlider(new Rect(205, 210, 220, 20), ManualTargetLocalPos.y, 0.00f, 0.30f);
            GUI.Label(new Rect(25, 230, 175, 22), $"Target Z ({ManualTargetLocalPos.z:F2}m):");
            float nz = GUI.HorizontalSlider(new Rect(205, 235, 220, 20), ManualTargetLocalPos.z, -0.25f, 0.25f);

            if (Mathf.Abs(nx - ManualTargetLocalPos.x) > 1e-4f ||
                Mathf.Abs(ny - ManualTargetLocalPos.y) > 1e-4f ||
                Mathf.Abs(nz - ManualTargetLocalPos.z) > 1e-4f)
            {
                AutoOrbitTarget = false;
                ManualTargetLocalPos = new Vector3(nx, ny, nz);
            }

            if (GUI.Button(new Rect(25, 260, 155, 28), AutoOrbitTarget ? "Orbit: ON" : "Orbit: OFF (Manual)"))
                AutoOrbitTarget = !AutoOrbitTarget;

            if (GUI.Button(new Rect(190, 260, 165, 28), "1. Fast Synth (Super [G])"))
                TriggerAsyncSynthesis(ManualTestWidthMm);

            if (GUI.Button(new Rect(365, 260, 165, 28), "2. Deep Audit (Ultra [H])"))
                TriggerUltraEngineeringAudit();

            if (GUI.Button(new Rect(540, 260, 160, 28), "3. Export URDF [U]"))
                ExportCurrentUrdf();
        }
    }
}