using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting;
using UnityEngine.XR;
using KineticCore.Kinematics;
using KineticCore.Schema;
using KineticCore.AI;
using KineticCore.Export;
using KineticCore.Input;

namespace KineticCore.Runtime
{
    /// <summary>
    /// Central controller: owns the MechanismSpec -> PoEChain, the DLS IK puppet loop, the procedural
    /// arm visuals, the 3D world-space HUD, and the camera setup for both Quest 3 (XR) and the flat Editor.
    /// </summary>
    [Preserve]
    [DefaultExecutionOrder(-100)]
    public class KineticMechanismController : MonoBehaviour
    {
        [Header("Scene References")]
        public Transform DeskWorkspaceOrigin;
        public Transform TargetTransform;

        [Header("Nebius Token Factory Config")]
        public string UserIntentPrompt = "3-DoF articulated inspection and gripper arm";

        [Header("Interactive Controls")]
        public double ManualTestWidthMm = 70.0;
        public bool PuppetActive = true;
        public bool AutoOrbitTarget = true;
        public Vector3 ManualTargetLocalPos = new Vector3(0.14f, 0.12f, 0.04f);

        [Header("Flat Editor / Desktop Fallback")]
        public bool AutoOrbitInFlatMode = true;
        public float KeyboardMoveSpeed = 0.22f;

        public MechanismSpec CurrentSpec { get; private set; }
        public PoEChain CurrentChain { get; private set; }
        public double[] JointAngles => _theta;
        public double CurrentManipulability { get; private set; }
        public double ReferenceManipulability { get; private set; } = 0.0012;
        public Vec3 CurrentEndEffectorPos { get; private set; }
        public string LastExportedUrdfPath { get; private set; } = "Press [U] / EXPORT URDF";
        public string StatusMessage { get; private set; } = "Ready";
        public string ActiveNebiusModel { get; private set; } = "offline-parametric";
        public string LastSpecSource { get; private set; } = "parametric (init)";
        public bool LastSpecFromAi { get; private set; } = false;
        public long LastApiLatencyMs { get; private set; } = 0;
        public string EngineeringAuditReport { get; private set; } = "Mobility M=3 DoF | Non-Singular | Ready";
        public bool IsFlatEditorMode => Application.isEditor && _flatCamera != null;
        public Camera HeadCamera => _headCamera;
        public Transform MechanismRoot => _mechanismRoot != null ? _mechanismRoot.transform : null;
        public bool IsSynthesisInFlight { get; private set; }

        private static readonly double[] HomeTheta = { 0.25, 0.45, -0.65 };
        private static readonly double[] DemoPresetWidthsMm = { 70.0, 145.0, 40.0, 210.0, 105.0 };
        private int _demoPresetIndex = 0;

        // Keep MechanismRoot at (0,0,0) on DeskWorkspaceOrigin so PoE math and desk coordinates match 1:1
        private static readonly Vector3 MechanismRootLocalPos = Vector3.zero;
        private static readonly Color HealthyCyan = new Color(0.1f, 0.85f, 0.95f);
        private static readonly Color WarningRed = new Color(1.0f, 0.2f, 0.2f);
        private static readonly Color AuditGold = new Color(1.0f, 0.82f, 0.15f);
        private static readonly Color ExportGreen = new Color(0.15f, 1.0f, 0.45f);

        private double[] _theta;
        private double[] _minLimits;
        private double[] _maxLimits;

        private GameObject _mechanismRoot;
        private Transform[] _jointPivots;
        private Renderer[] _linkRenderers;
        private Renderer[] _pinRenderers;
        private Transform _eeMarker;
        private Transform _leftJawFinger;
        private Transform _rightJawFinger;
        private Renderer _targetRenderer;
        private Transform _sampleWorkpiece;
        private Renderer _workpieceRenderer;
        private Transform _dropZonePad;
        private Renderer _dropZoneRenderer;
        private TextMesh _dropZoneLabel;
        private Transform _vrHudTransform;
        private TextMesh _vrHologramText;
        private Transform _urdfInspectorTransform;
        private TextMesh _urdfInspectorText;
        private Transform _specSheetTransform;
        private TextMesh _specSheetText;
        private TextMesh _workpieceLabel;
        private Camera _headCamera;
        private MaterialPropertyBlock _mpb;
        private bool _hasCenteredToHmd = false;
        private bool _mrukTableLocked = false;
        private float _vrStartupTimer = 0f;

        // Multi-Topology & Guided Onboarding state
        private int _topologyVariantIndex = 0;
        private bool _stepSynthDone = false;
        private bool _stepPuppetDone = false;
        private bool _stepAuditOrExportDone = false;

        // Interactive Pick-and-Place & 5-Level Robotics Challenge state
        public int CompletedGraspCount { get; private set; } = 0;
        public int CurrentChallengeLevel { get; private set; } = 1;
        public string LastDeliveryStarRating { get; private set; } = "★★★";
        private static readonly string[] ChallengeTitles =
        {
            "Lv1: Standard 70mm Transfer",
            "Lv2: Wide-Span 145mm SCARA",
            "Lv3: Micro 40mm Precision Probe",
            "Lv4: Heavy 210mm Turret Payload",
            "Lv5: Master Singularity-Free Run"
        };
        private bool _isHoldingWorkpiece = false;
        private double _minManipulabilityDuringCarry = 1.0;
        private float _jawCloseFactor = 0f;
        private float _graspCooldownTimer = 0f;
        private float _dropZoneFlashTimer = 0f;
        private Vector3 _workpieceHomeLocalPos = new Vector3(-0.12f, 0.008f, 0.04f);
        private Vector3 _dropZoneLocalPos = new Vector3(0.16f, 0.001f, 0.04f);
        private float _lastReachMeters = 0.25f;
        private Transform[] _reachEnvelopeSegments;
        private Renderer[] _reachEnvelopeRenderers;
        private Transform _objectiveBeaconRing;
        private Renderer _objectiveBeaconRenderer;

        // 3D Spatial Manipulability Trajectory Heatmap Trail
        private const int TrailLength = 14;
        private Transform[] _trailMarkers;
        private Renderer[] _trailRenderers;
        private Vector3[] _trailPositions;
        private Color[] _trailColors;
        private float _trailSampleTimer = 0f;

        // Visual feedback timers for button actions
        private float _spawnAnimTimer = 0f;
        private float _auditSweepTimer = 0f;
        private float _exportFlashTimer = 0f;
        private float _resetHoldTimer = 0f;

        // Flat Editor camera state (used only when Application.isEditor && !XRSettings.isDeviceActive)
        private Camera _flatCamera;
        private readonly List<Camera> _disabledXrCameras = new List<Camera>();
        private readonly List<Renderer> _disabledXrRenderers = new List<Renderer>();
        private float _orbitYaw = 0f;
        private float _orbitPitch = 25f;
        private float _orbitDistance = 0.64f;
        private static readonly Vector3 FlatCameraFocusLocal = new Vector3(0.08f, 0.09f, 0.0f);

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (Application.isEditor && !XRSettings.isDeviceActive)
            {
                BoostEditorRenderQuality();
            }

            if (DeskWorkspaceOrigin == null)
            {
                GameObject found = GameObject.Find("DeskWorkspaceOrigin");
                DeskWorkspaceOrigin = found != null ? found.transform : this.transform;
            }
            DeskWorkspaceOrigin.localScale = Vector3.one;
            EnsureTargetSphere();
        }

        private void Start()
        {
            var initSynth = new NebiusKinematicSynthesizer();
            ActiveNebiusModel = initSynth.HasValidKey ? NebiusKinematicSynthesizer.FastSuperModelId : "offline-parametric";
            Debug.Log($"[KineticCAD] Nebius key file path: {initSynth.KeyFilePath} | HasValidKey={initSynth.HasValidKey} | Source={initSynth.KeySource}");

            // On Quest 3 Android builds (!Application.isEditor), NEVER disable XR cameras or spawn a flat camera!
            if (!Application.isEditor || XRSettings.isDeviceActive)
            {
                FindHeadCamera();
            }
            else
            {
                SetupFlatEditorCamera();
                AutoOrbitTarget = true;
                PuppetActive = true;
            }

            CreateDeskEnvironment();
            CreateVrHologramHud();
            SynthesizeAndSpawnSync(ManualTestWidthMm);
            if (IsFlatEditorMode)
            {
                ResetPose();
            }
            AutoOrbitTarget = true; // Start with live figure-8 motion so user immediately sees IK working

            // If we have a valid Nebius API key, immediately run an initial live synthesis on Nebius Token Factory!
            if (initSynth.HasValidKey)
            {
                TriggerAsyncSynthesis(ManualTestWidthMm);
            }

            if (GetComponent<QuestHandAndCommandBridge>() == null)
            {
                var bridge = gameObject.AddComponent<QuestHandAndCommandBridge>();
                bridge.MechanismController = this;
                bridge.DeskWorkspaceOrigin = DeskWorkspaceOrigin;
                bridge.TargetTransform = TargetTransform;
            }

            StatusMessage = IsFlatEditorMode
                ? "Click 3D Desk Buttons or LMB-drag in scene | WASD/QE move | RMB orbit"
                : "HANDS-FIRST: Point Right Index to puppet | Point Both to caliper-measure | Poke or Gaze+Pinch buttons";

            WriteDiagnosticState("Start completed");
        }

        private void WriteDiagnosticState(string eventNote)
        {
            if (!Application.isEditor) return;
            try
            {
                string logDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
                if (Directory.Exists(logDir))
                {
                    string info = $"Time={DateTime.UtcNow:HH:mm:ss} | Event={eventNote} | XR={XRSettings.isDeviceActive} | FlatCam={IsFlatEditorMode} | Width={ManualTestWidthMm:F1}mm | Orbit={AutoOrbitTarget} | Puppet={PuppetActive} | Status={StatusMessage}";
                    File.WriteAllText(Path.Combine(logDir, "kinetic_runtime_state.txt"), info);
                }
            }
            catch { }
        }

        private static void BoostEditorRenderQuality()
        {
            QualitySettings.antiAliasing = 4;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = 1.0f;
                urp.msaaSampleCount = 4;
            }
        }

        // ------------------------------------------------------------------ Cameras

        private void FindHeadCamera()
        {
            GameObject centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null && centerEye.TryGetComponent<Camera>(out Camera cam))
            {
                _headCamera = cam;
                if (!centerEye.CompareTag("MainCamera"))
                    centerEye.tag = "MainCamera";
            }
            else
            {
                _headCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            }

            if (_headCamera != null)
            {
                _headCamera.clearFlags = CameraClearFlags.SolidColor;
                _headCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _headCamera.nearClipPlane = 0.02f;
            }

            // Ensure Meta Quest 3 Insight Passthrough and 90 Hz refresh rate are actively enabled
            if (!IsFlatEditorMode)
            {
                try
                {
                    OVRPlugin.systemDisplayFrequency = 90.0f;
                    if (OVRManager.instance != null)
                        OVRManager.instance.isInsightPassthroughEnabled = true;

                    foreach (var pt in FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include))
                    {
                        pt.hidden = false;
                        pt.textureOpacity = 1.0f;
                        if (!pt.enabled) pt.enabled = true;
                    }
                }
                catch { }
            }
        }

        private void SetupFlatEditorCamera()
        {
            if (!Application.isEditor) return;

            _disabledXrCameras.Clear();
            foreach (Camera c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (c.enabled)
                {
                    c.enabled = false;
                    _disabledXrCameras.Add(c);
                }
            }

            _disabledXrRenderers.Clear();
            GameObject ovrRig = GameObject.Find("OVRCameraRigInteraction") ?? GameObject.Find("OVRCameraRig");
            if (ovrRig != null)
            {
                foreach (Renderer r in ovrRig.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                        _disabledXrRenderers.Add(r);
                    }
                }
            }

            GameObject camObj = new GameObject("KineticCAD_FlatEditorCamera");
            camObj.tag = "MainCamera";
            _flatCamera = camObj.AddComponent<Camera>();
            _flatCamera.clearFlags = CameraClearFlags.SolidColor;
            _flatCamera.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            _flatCamera.nearClipPlane = 0.01f;
            _flatCamera.farClipPlane = 50f;
            _flatCamera.fieldOfView = 54f;
            _flatCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _flatCamera.allowMSAA = true;

            var uacd = camObj.AddComponent<UniversalAdditionalCameraData>();
            uacd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            uacd.antialiasingQuality = AntialiasingQuality.High;
            uacd.renderPostProcessing = true;

            camObj.AddComponent<AudioListener>();

            _headCamera = _flatCamera;
            ResetFlatCameraOrbit();
        }

        private void TeardownFlatEditorCamera()
        {
            if (_flatCamera != null) Destroy(_flatCamera.gameObject);
            _flatCamera = null;
            foreach (Camera c in _disabledXrCameras)
                if (c != null) c.enabled = true;
            _disabledXrCameras.Clear();
            foreach (Renderer r in _disabledXrRenderers)
                if (r != null) r.enabled = true;
            _disabledXrRenderers.Clear();
            _headCamera = null;
            FindHeadCamera();
            _hasCenteredToHmd = false;
        }

        private void ResetFlatCameraOrbit()
        {
            _orbitYaw = 0f;
            _orbitPitch = 25f;
            _orbitDistance = 0.64f;
            ApplyFlatCameraOrbit();
        }

        private void ApplyFlatCameraOrbit()
        {
            if (_flatCamera == null || DeskWorkspaceOrigin == null) return;
            Quaternion localRot = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            Vector3 localPos = FlatCameraFocusLocal + localRot * new Vector3(0f, 0f, -_orbitDistance);
            Transform ct = _flatCamera.transform;
            ct.position = DeskWorkspaceOrigin.TransformPoint(localPos);
            ct.rotation = DeskWorkspaceOrigin.rotation * localRot;
        }

        private void UpdateFlatCameraControls()
        {
            if (_flatCamera == null) return;
            bool changed = false;
            if (KInput.GetMouseButton(1))
            {
                Vector2 d = KInput.MouseDelta();
                _orbitYaw += d.x * 0.25f;
                _orbitPitch = Mathf.Clamp(_orbitPitch - d.y * 0.25f, -5f, 85f);
                changed = true;
            }
            float scroll = KInput.ScrollDelta();
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _orbitDistance = Mathf.Clamp(_orbitDistance * (1f - scroll * 0.1f), 0.25f, 2.5f);
                changed = true;
            }
            if (changed) ApplyFlatCameraOrbit();
        }

        // ------------------------------------------------------------------ Public commands ([Preserve] for IL2CPP)

        [Preserve]
        public void NotifyDeskAnchored(Vector3 worldPos, Quaternion worldRot, string sourceLabel)
        {
            _mrukTableLocked = true;
            _hasCenteredToHmd = true;
            DeskWorkspaceOrigin.SetPositionAndRotation(worldPos, worldRot);
            StatusMessage = $"{sourceLabel} -> MRUK Table Anchor Locked";
            WriteDiagnosticState("NotifyDeskAnchored");
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                // Clean resume from sleep/guardian boundary: re-verify XR camera, 90Hz display, and Insight Passthrough
                FindHeadCamera();
            }
        }

        [Preserve]
        public void ResetPose()
        {
            if (IsFlatEditorMode)
            {
                ResetFlatCameraOrbit();
            }
            else
            {
                if (_headCamera == null) FindHeadCamera();
                if (_headCamera != null)
                {
                    Vector3 headPos = _headCamera.transform.position;
                    Vector3 forwardFlat = Vector3.ProjectOnPlane(_headCamera.transform.forward, Vector3.up).normalized;
                    if (forwardFlat.sqrMagnitude < 0.01f) forwardFlat = Vector3.forward;

                    // Default seated desk placement: 0.48m in front of HMD, 0.30m below eye level
                    Vector3 deskPos = headPos + forwardFlat * 0.48f + Vector3.down * 0.30f;

                    // If the user's Left Hand is held out on their physical desk surface when pressing Reset Pose / Y,
                    // snap the virtual desk height (Y) and position right to their hand on the physical desk!
                    var tipReader = FindAnyObjectByType<HandFingertipReader>();
                    if (tipReader != null && tipReader.LeftValid)
                    {
                        Vector3 leftHand = tipReader.LeftWorld;
                        float dy = headPos.y - leftHand.y;
                        float hDist = Vector3.Distance(new Vector3(headPos.x, 0f, headPos.z), new Vector3(leftHand.x, 0f, leftHand.z));
                        if (dy > 0.12f && dy < 0.75f && hDist > 0.15f && hDist < 0.85f)
                        {
                            deskPos.y = leftHand.y;
                        }
                    }

                    DeskWorkspaceOrigin.position = deskPos;
                    DeskWorkspaceOrigin.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
                }
            }

            if (_theta != null)
                for (int i = 0; i < _theta.Length && i < HomeTheta.Length; i++)
                    _theta[i] = HomeTheta[i];

            if (CurrentChain != null && _theta != null)
            {
                Vec3 homeEe = CurrentChain.EndEffectorPosition(_theta);
                ManualTargetLocalPos = MechanismRootLocalPos + new Vector3((float)homeEe.X, (float)homeEe.Y, (float)homeEe.Z);
                if (TargetTransform != null)
                    TargetTransform.position = DeskWorkspaceOrigin.TransformPoint(ManualTargetLocalPos);
            }

            AutoOrbitTarget = false;
            PuppetActive = true;
            _resetHoldTimer = 0.8f;
            StatusMessage = "RESET POSE -> Calibrated to Desk & Home [0.25, 0.45, -0.65] rad";
            WriteDiagnosticState("ResetPose");
        }

        [Preserve]
        public void SynthesizeFast()
        {
            _stepSynthDone = true;
            _topologyVariantIndex = (_topologyVariantIndex + 1) % 4;
            // If user is actively measuring an object with both hands in VR, use their live CaliperWidthMm;
            // otherwise cycle through distinct preset sizes so clicking Fast Synth shows a dramatic geometry change!
            var caliper = GetComponent<CaliperController>();
            if (caliper != null && caliper.IsMeasuringActive && caliper.CaliperWidthMm >= 20.0 && caliper.CaliperWidthMm <= 260.0)
            {
                ManualTestWidthMm = caliper.CaliperWidthMm;
            }
            else
            {
                _demoPresetIndex = (_demoPresetIndex + 1) % DemoPresetWidthsMm.Length;
                ManualTestWidthMm = DemoPresetWidthsMm[_demoPresetIndex];
            }
            TriggerAsyncSynthesis(ManualTestWidthMm);
        }

        [Preserve] public void SynthesizeDeep() => TriggerUltraEngineeringAudit();
        [Preserve] public void ExportUrdf() => ExportCurrentUrdf();

        [Preserve]
        public void TogglePuppet()
        {
            PuppetActive = !PuppetActive;
            if (_targetRenderer != null)
                SetColorSafe(_targetRenderer, PuppetActive ? Color.red : new Color(1.0f, 0.65f, 0.1f));
            StatusMessage = PuppetActive ? "PUPPET: ON (Tracking Target)" : "PUPPET: PAUSED (Arm Locked)";
            WriteDiagnosticState("TogglePuppet");
        }

        [Preserve]
        public void ToggleAutoOrbit()
        {
            AutoOrbitTarget = !AutoOrbitTarget;
            if (AutoOrbitTarget) PuppetActive = true;
            StatusMessage = AutoOrbitTarget ? "AUTO-ORBIT: Figure-8 ON" : "AUTO-ORBIT: OFF (Manual Control)";
            WriteDiagnosticState("ToggleAutoOrbit");
        }

        [Preserve]
        public void SetCaliperWidthMm(double w, bool triggerSynthesisNow = false)
        {
            ManualTestWidthMm = System.Math.Max(20.0, System.Math.Min(250.0, w));
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (triggerSynthesisNow)
                TriggerAsyncSynthesis(ManualTestWidthMm);
        }

        // ------------------------------------------------------------------ Scene construction

        private void CreateDeskEnvironment()
        {
            bool hasDirectionalLight = false;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.enabled && l.type == LightType.Directional) { hasDirectionalLight = true; break; }

            if (!hasDirectionalLight)
            {
                GameObject dirLightObj = new GameObject("KineticCAD_DirLight");
                dirLightObj.transform.SetParent(DeskWorkspaceOrigin, false);
                dirLightObj.transform.localPosition = new Vector3(0f, 1.5f, -0.5f);
                dirLightObj.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
                Light dl = dirLightObj.AddComponent<Light>();
                dl.type = LightType.Directional;
                dl.intensity = 1.5f;
            }

            GameObject deskMat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deskMat.name = "VirtualDeskPlatform";
            Destroy(deskMat.GetComponent<Collider>());
            deskMat.transform.SetParent(DeskWorkspaceOrigin, false);
            deskMat.transform.localPosition = new Vector3(0.08f, -0.004f, 0.0f);
            deskMat.transform.localScale = new Vector3(0.48f, 0.006f, 0.36f);
            SetColorSafe(deskMat.GetComponent<Renderer>(), new Color(0.14f, 0.20f, 0.30f));

            // Create a subtle, clearly labelled 3D reference caliper gauge on the desk showing the locked object width (mm)
            GameObject wp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wp.name = "MeasuredWorkpiece_Preview";
            Destroy(wp.GetComponent<Collider>());
            wp.transform.SetParent(DeskWorkspaceOrigin, false);
            _sampleWorkpiece = wp.transform;
            _workpieceRenderer = wp.GetComponent<Renderer>();
            SetColorSafe(_workpieceRenderer, new Color(0.18f, 0.55f, 0.72f));

            _workpieceLabel = WorldTextFactory.Create(
                "Workpiece_SizeLabel", DeskWorkspaceOrigin,
                new Vector3(-0.13f, 0.032f, 0.04f), Quaternion.Euler(35f, 0f, 0f),
                0.0016f, 34, new Color(0.55f, 0.92f, 1.0f), $"PICKUP WORKPIECE\n{ManualTestWidthMm:F0} mm");

            // Create interactive Pick-and-Place Target Drop Zone Pad on the right side of the desk
            GameObject dz = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dz.name = "DropZone_TargetPad";
            Destroy(dz.GetComponent<Collider>());
            dz.transform.SetParent(DeskWorkspaceOrigin, false);
            dz.transform.localPosition = _dropZoneLocalPos;
            dz.transform.localScale = new Vector3(0.088f, 0.0018f, 0.088f);
            _dropZonePad = dz.transform;
            _dropZoneRenderer = dz.GetComponent<Renderer>();
            SetColorSafe(_dropZoneRenderer, new Color(0.12f, 0.65f, 0.42f));

            _dropZoneLabel = WorldTextFactory.Create(
                "DropZone_Label", DeskWorkspaceOrigin,
                _dropZoneLocalPos + new Vector3(0f, 0.025f, 0f), Quaternion.Euler(35f, 0f, 0f),
                0.0015f, 32, new Color(0.35f, 1.0f, 0.65f), "DROP ZONE (Lv1)\nGrasps: 0 | ★★★");

            CreateReachEnvelopeRing();
            CreateObjectiveBeacon();
            CreateTrajectoryTrail();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
        }

        private void CreateObjectiveBeacon()
        {
            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "ObjectiveBeacon_Ring";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(DeskWorkspaceOrigin, false);
            beacon.transform.localScale = new Vector3(0.048f, 0.0018f, 0.048f);
            _objectiveBeaconRing = beacon.transform;
            _objectiveBeaconRenderer = beacon.GetComponent<Renderer>();
            SetColorSafe(_objectiveBeaconRenderer, new Color(0.20f, 0.95f, 1.0f));
        }

        private void CreateTrajectoryTrail()
        {
            _trailMarkers = new Transform[TrailLength];
            _trailRenderers = new Renderer[TrailLength];
            _trailPositions = new Vector3[TrailLength];
            _trailColors = new Color[TrailLength];

            GameObject trailRoot = new GameObject("SpatialManipulabilityTrail");
            trailRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            trailRoot.transform.localPosition = Vector3.zero;
            trailRoot.transform.localRotation = Quaternion.identity;

            for (int i = 0; i < TrailLength; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"TrailPoint_{i}";
                Destroy(dot.GetComponent<Collider>());
                dot.transform.SetParent(trailRoot.transform, false);
                float t = 1f - ((float)i / TrailLength);
                float diam = Mathf.Lerp(0.003f, 0.0095f, t);
                dot.transform.localScale = Vector3.one * diam;
                dot.transform.localPosition = ManualTargetLocalPos;
                _trailMarkers[i] = dot.transform;
                _trailRenderers[i] = dot.GetComponent<Renderer>();
                _trailPositions[i] = ManualTargetLocalPos;
                _trailColors[i] = HealthyCyan;
                SetColorSafe(_trailRenderers[i], HealthyCyan);
            }
        }

        private void UpdateTrajectoryTrail(Color currentHealthColor)
        {
            if (_trailMarkers == null || _eeMarker == null || DeskWorkspaceOrigin == null) return;
            _trailSampleTimer += Time.deltaTime;
            if (_trailSampleTimer < 0.035f) return;
            _trailSampleTimer = 0f;

            Vector3 eeLocal = DeskWorkspaceOrigin.InverseTransformPoint(_eeMarker.position);
            if (Vector3.Distance(eeLocal, _trailPositions[0]) < 0.004f) return;

            for (int i = TrailLength - 1; i > 0; i--)
            {
                _trailPositions[i] = _trailPositions[i - 1];
                _trailColors[i] = Color.Lerp(_trailColors[i - 1], new Color(0.08f, 0.16f, 0.28f), 0.12f);
            }
            _trailPositions[0] = eeLocal;
            _trailColors[0] = currentHealthColor;

            for (int i = 0; i < TrailLength; i++)
            {
                _trailMarkers[i].localPosition = _trailPositions[i];
                SetColorSafe(_trailRenderers[i], _trailColors[i]);
            }
        }

        private void CreateReachEnvelopeRing()
        {
            const int segmentCount = 20;
            _reachEnvelopeSegments = new Transform[segmentCount];
            _reachEnvelopeRenderers = new Renderer[segmentCount];
            GameObject ringRoot = new GameObject("KinematicReachEnvelopeRing");
            ringRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            ringRoot.transform.localPosition = Vector3.zero;
            ringRoot.transform.localRotation = Quaternion.identity;

            for (int i = 0; i < segmentCount; i++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"ReachArcSeg_{i}";
                Destroy(seg.GetComponent<Collider>());
                seg.transform.SetParent(ringRoot.transform, false);
                _reachEnvelopeRenderers[i] = seg.GetComponent<Renderer>();
                SetColorSafe(_reachEnvelopeRenderers[i], new Color(0.12f, 0.65f, 0.82f));
                _reachEnvelopeSegments[i] = seg.transform;
            }
            UpdateReachEnvelopeAndStationPositions(0.25f);
        }

        private void UpdateReachEnvelopeAndStationPositions(float reachMeters)
        {
            _lastReachMeters = Mathf.Clamp(reachMeters, 0.11f, 0.34f);
            float clampedReach = _lastReachMeters;

            // Dynamically place Pickup Workpiece and Drop Zone well inside (62% of) the current mechanism's reach
            // and vary their spatial angles/depths across the 5 Robotics Challenge levels!
            float stationRadius = Mathf.Clamp(clampedReach * 0.62f, 0.082f, 0.21f);
            float baseZ = Mathf.Clamp(clampedReach * 0.20f, 0.024f, 0.052f);
            int lvlIdx = (CurrentChallengeLevel - 1) % 5;
            float leftZOffset = (lvlIdx == 1) ? 0.022f : ((lvlIdx == 3) ? -0.015f : 0f);
            float rightZOffset = (lvlIdx == 2) ? 0.025f : ((lvlIdx == 4) ? 0.032f : 0f);

            _workpieceHomeLocalPos = new Vector3(-stationRadius * 0.88f, 0.008f, baseZ + leftZOffset);
            _dropZoneLocalPos = new Vector3(stationRadius, 0.001f, baseZ + rightZOffset);

            if (!_isHoldingWorkpiece && _sampleWorkpiece != null)
            {
                _sampleWorkpiece.localPosition = _workpieceHomeLocalPos;
                if (_workpieceLabel != null)
                    _workpieceLabel.transform.localPosition = _workpieceHomeLocalPos + new Vector3(0f, 0.024f, 0f);
            }

            if (_dropZonePad != null)
                _dropZonePad.localPosition = _dropZoneLocalPos;
            if (_dropZoneLabel != null)
                _dropZoneLabel.transform.localPosition = _dropZoneLocalPos + new Vector3(0f, 0.025f, 0f);

            if (_reachEnvelopeSegments != null)
            {
                int count = _reachEnvelopeSegments.Length;
                float ringRadius = Mathf.Clamp(clampedReach * 0.88f, 0.095f, 0.23f);
                for (int i = 0; i < count; i++)
                {
                    // Render an upper-workspace 200-degree reachability boundary arc on the desk
                    float t = (float)i / (count - 1);
                    float angleRad = Mathf.Lerp(-100f, 100f, t) * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Sin(angleRad) * ringRadius, 0.0005f, Mathf.Cos(angleRad) * ringRadius * 0.65f);
                    _reachEnvelopeSegments[i].localPosition = pos;
                    _reachEnvelopeSegments[i].localRotation = Quaternion.Euler(0f, angleRad * Mathf.Rad2Deg + 90f, 0f);
                    _reachEnvelopeSegments[i].localScale = new Vector3(0.014f, 0.0012f, 0.003f);
                }
            }
        }

        private void UpdateSampleWorkpieceVisual(double widthMm)
        {
            if (_sampleWorkpiece == null) return;
            float wMeters = Mathf.Clamp((float)(widthMm * 0.001), 0.02f, 0.25f);
            if (!_isHoldingWorkpiece)
            {
                _sampleWorkpiece.localPosition = _workpieceHomeLocalPos;
                if (_workpieceLabel != null)
                    _workpieceLabel.transform.localPosition = _workpieceHomeLocalPos + new Vector3(0f, 0.024f, 0f);
            }
            _sampleWorkpiece.localScale = new Vector3(wMeters, 0.014f, 0.024f);
            if (_workpieceLabel != null)
            {
                _workpieceLabel.text = _isHoldingWorkpiece
                    ? $"GRASPED ({widthMm:F0} mm)\nMove to Drop Zone ->"
                    : $"PICKUP WORKPIECE\n{widthMm:F0} mm";
            }
        }

        private void CreateVrHologramHud()
        {
            // Center Panel: Main Telemetry & Guided 4-Step Onboarding HUD
            TextMesh tm = WorldTextFactory.Create(
                "VR_3D_Hologram_HUD", DeskWorkspaceOrigin,
                new Vector3(0.05f, 0.245f, 0.09f), Quaternion.identity,
                0.00185f, 36, new Color(0.15f, 0.95f, 1.0f), "KineticCAD Physical AI Active");
            _vrHologramText = tm;
            _vrHudTransform = tm.transform;
            AttachPanelBackingSlate(_vrHudTransform, new Vector3(0.54f, 0.130f, 0.003f), new Color(0.05f, 0.08f, 0.13f), new Color(0.10f, 0.65f, 0.85f));

            // Right Panel: 3D URDF + STL CAD & Live 60Hz Telemetry Inspector Panel
            TextMesh urdfTm = WorldTextFactory.Create(
                "VR_3D_URDF_Inspector_HUD", DeskWorkspaceOrigin,
                new Vector3(0.34f, 0.195f, 0.05f), Quaternion.Euler(0f, -20f, 0f),
                0.00145f, 32, new Color(0.45f, 1.0f, 0.70f), "ROS 2 URDF & 3D-Print STL Inspector");
            _urdfInspectorText = urdfTm;
            _urdfInspectorTransform = urdfTm.transform;
            AttachPanelBackingSlate(_urdfInspectorTransform, new Vector3(0.38f, 0.105f, 0.003f), new Color(0.04f, 0.10f, 0.08f), new Color(0.15f, 0.75f, 0.45f));

            // Left Panel: PoE Screw Theory & Mechanical Spec Sheet Panel
            TextMesh specTm = WorldTextFactory.Create(
                "VR_3D_PoE_SpecSheet_HUD", DeskWorkspaceOrigin,
                new Vector3(-0.24f, 0.195f, 0.05f), Quaternion.Euler(0f, 20f, 0f),
                0.00145f, 32, new Color(1.0f, 0.85f, 0.35f), "PoE Screw Kinematics Spec Sheet");
            _specSheetText = specTm;
            _specSheetTransform = specTm.transform;
            AttachPanelBackingSlate(_specSheetTransform, new Vector3(0.38f, 0.105f, 0.003f), new Color(0.11f, 0.08f, 0.04f), new Color(0.85f, 0.65f, 0.18f));
        }

        private void AttachPanelBackingSlate(Transform textParent, Vector3 scaleMeters, Color slateColor, Color rimColor)
        {
            if (textParent == null) return;
            // Compensate for WorldTextFactory's parent scale (0.00185 or 0.00145) so the slate renders at true meter dimensions!
            float parentScale = Mathf.Max(0.0001f, textParent.localScale.x);

            GameObject slate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slate.name = "SpatialPanel_DarkGlassSlate";
            Destroy(slate.GetComponent<Collider>());
            slate.transform.SetParent(textParent, false);
            slate.transform.localPosition = new Vector3(0f, 0f, 0.008f / parentScale);
            slate.transform.localRotation = Quaternion.identity;
            slate.transform.localScale = scaleMeters / parentScale;
            SetColorSafe(slate.GetComponent<Renderer>(), slateColor);

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "SpatialPanel_AccentRim";
            Destroy(rim.GetComponent<Collider>());
            rim.transform.SetParent(textParent, false);
            rim.transform.localPosition = new Vector3(0f, 0f, 0.0105f / parentScale);
            rim.transform.localRotation = Quaternion.identity;
            rim.transform.localScale = new Vector3(scaleMeters.x + 0.008f, scaleMeters.y + 0.008f, 0.002f) / parentScale;
            SetColorSafe(rim.GetComponent<Renderer>(), rimColor);
        }

        [Preserve]
        public Transform EnsureTargetSphere()
        {
            if (TargetTransform != null)
            {
                _targetRenderer = TargetTransform.GetComponent<Renderer>();
                return TargetTransform;
            }
            if (DeskWorkspaceOrigin == null) return null;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "RightFingertip_Target_Red";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(DeskWorkspaceOrigin, false);
            sphere.transform.localPosition = ManualTargetLocalPos;
            sphere.transform.localScale = Vector3.one * 0.028f;
            _targetRenderer = sphere.GetComponent<Renderer>();
            SetColorSafe(_targetRenderer, Color.red);
            TargetTransform = sphere.transform;
            return TargetTransform;
        }

        // ------------------------------------------------------------------ Frame loop

        private void Update()
        {
            if (IsFlatEditorMode && XRSettings.isDeviceActive)
                TeardownFlatEditorCamera();

            if (!IsFlatEditorMode)
            {
                _vrStartupTimer += Time.unscaledDeltaTime;
                if (_headCamera == null) FindHeadCamera();
                if (!_hasCenteredToHmd && !_mrukTableLocked && _vrStartupTimer > 0.65f &&
                    _headCamera != null && _headCamera.transform.position.sqrMagnitude > 0.001f)
                {
                    _hasCenteredToHmd = true;
                    ResetPose();
                    AutoOrbitTarget = true;
                }
            }
#if UNITY_EDITOR
            else
            {
                HandleDesktopKeyboard();
                UpdateFlatCameraControls();
            }
#endif

            if (_vrHudTransform != null && _headCamera != null)
            {
                Vector3 toHud = _vrHudTransform.position - _headCamera.transform.position;
                if (toHud.sqrMagnitude > 0.001f)
                    _vrHudTransform.rotation = Quaternion.LookRotation(toHud.normalized, Vector3.up);
            }

            // Spawn scale-up animation when Fast Synth spawns a new mechanism
            if (_spawnAnimTimer > 0f && _mechanismRoot != null)
            {
                _spawnAnimTimer = Mathf.Max(0f, _spawnAnimTimer - Time.deltaTime * 3.2f);
                float s = 1f + 0.25f * Mathf.Sin(_spawnAnimTimer * Mathf.PI);
                _mechanismRoot.transform.localScale = Vector3.one * s;
            }
            else if (_mechanismRoot != null)
            {
                _mechanismRoot.transform.localScale = Vector3.one;
            }

            if (_exportFlashTimer > 0f)
                _exportFlashTimer = Mathf.Max(0f, _exportFlashTimer - Time.deltaTime);

            if (_resetHoldTimer > 0f)
                _resetHoldTimer = Mathf.Max(0f, _resetHoldTimer - Time.deltaTime);

            // Audit sweep animation when Deep Audit is clicked
            if (_auditSweepTimer > 0f && _theta != null && _theta.Length >= 3)
            {
                _auditSweepTimer = Mathf.Max(0f, _auditSweepTimer - Time.deltaTime);
                float phase = (1.2f - _auditSweepTimer) * 6.0f;
                _theta[0] = 0.65 * System.Math.Sin(phase);
                _theta[1] = 0.55 + 0.35 * System.Math.Cos(phase);
                _theta[2] = -0.75 + 0.40 * System.Math.Sin(phase * 1.5f);
                if (CurrentChain != null)
                {
                    Vec3 ee = CurrentChain.EndEffectorPosition(_theta);
                    ManualTargetLocalPos = MechanismRootLocalPos + new Vector3((float)ee.X, (float)ee.Y, (float)ee.Z);
                }
            }
            else if (AutoOrbitTarget && PuppetActive)
            {
                float t = Time.time * 1.35f;
                Vector3 c = MechanismRootLocalPos + new Vector3(0.12f, 0.10f, 0.0f);
                ManualTargetLocalPos = c + new Vector3(
                    0.045f * Mathf.Sin(t * 2f),
                    0.050f * Mathf.Sin(t * 2f + 0.8f),
                    0.09f * Mathf.Sin(t));
            }
            else if (PuppetActive && _auditSweepTimer <= 0f)
            {
                // Smart Tabletop Grasp Compliance: when the user hovers their fingertip or mouse over the
                // active Pickup Workpiece or Drop Zone in XZ, smoothly dip the target down to grasp/place height
                // so they never have to scrape their finger against the physical desk!
                Vector3 activeStation = _isHoldingWorkpiece ? _dropZoneLocalPos : _workpieceHomeLocalPos;
                float xzDist = Vector2.Distance(
                    new Vector2(ManualTargetLocalPos.x, ManualTargetLocalPos.z),
                    new Vector2(activeStation.x, activeStation.z));
                if (xzDist < 0.078f && _graspCooldownTimer <= 0f)
                {
                    float dipFactor = 1f - Mathf.Clamp01(xzDist / 0.078f);
                    float desiredY = Mathf.Lerp(ManualTargetLocalPos.y, 0.016f, dipFactor * 0.85f);
                    ManualTargetLocalPos = new Vector3(ManualTargetLocalPos.x, desiredY, ManualTargetLocalPos.z);
                }
            }

            if (TargetTransform != null)
                TargetTransform.position = DeskWorkspaceOrigin.TransformPoint(ManualTargetLocalPos);

            if (CurrentChain != null && _theta != null && TargetTransform != null && _mechanismRoot != null)
            {
                if (PuppetActive && _auditSweepTimer <= 0f && _resetHoldTimer <= 0f)
                {
                    Vector3 lp = _mechanismRoot.transform.InverseTransformPoint(TargetTransform.position);
                    Vec3 targetLocal = new Vec3(lp.x, lp.y, lp.z);

                    DlsInverseKinematics.SolvePosition(
                        CurrentChain, targetLocal, _theta, _minLimits, _maxLimits,
                        lambda: 0.06, maxIterations: 25, tolerance: 1e-4, maxStepRad: 0.15);
                }

                CurrentManipulability = DlsInverseKinematics.ComputeManipulabilityIndex(CurrentChain, _theta);
                CurrentEndEffectorPos = CurrentChain.EndEffectorPosition(_theta);
                UpdateVisualLinksFromPoE();
                UpdateVrHologramText();
            }
        }

        private void HandleDesktopKeyboard()
        {
            bool shift = KInput.GetKey(KeyCode.LeftShift) || KInput.GetKey(KeyCode.RightShift);
            float speed = KeyboardMoveSpeed * (shift ? 3f : 1f) * Time.deltaTime;
            Vector3 move = Vector3.zero;
            if (KInput.GetKey(KeyCode.W) || KInput.GetKey(KeyCode.UpArrow)) move += Vector3.forward;
            if (KInput.GetKey(KeyCode.S) || KInput.GetKey(KeyCode.DownArrow)) move += Vector3.back;
            if (KInput.GetKey(KeyCode.A) || KInput.GetKey(KeyCode.LeftArrow)) move += Vector3.left;
            if (KInput.GetKey(KeyCode.D) || KInput.GetKey(KeyCode.RightArrow)) move += Vector3.right;
            if (KInput.GetKey(KeyCode.E)) move += Vector3.up;
            if (KInput.GetKey(KeyCode.Q)) move += Vector3.down;

            if (move.sqrMagnitude > 0.0001f)
            {
                AutoOrbitTarget = false;
                PuppetActive = true;
                ManualTargetLocalPos += move.normalized * speed;
            }

            if (KInput.GetKeyDown(KeyCode.G)) SynthesizeFast();
            if (KInput.GetKeyDown(KeyCode.H)) SynthesizeDeep();
            if (KInput.GetKeyDown(KeyCode.U)) ExportUrdf();
            if (KInput.GetKeyDown(KeyCode.P)) TogglePuppet();
            if (KInput.GetKeyDown(KeyCode.O)) ToggleAutoOrbit();
            if (KInput.GetKeyDown(KeyCode.Space) || KInput.GetKeyDown(KeyCode.R)) ResetPose();
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!IsFlatEditorMode) return;

            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                float step = (e.shift ? 0.03f : 0.012f);
                switch (e.keyCode)
                {
                    case KeyCode.W: case KeyCode.UpArrow:    AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.forward * step; e.Use(); break;
                    case KeyCode.S: case KeyCode.DownArrow:  AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.back * step; e.Use(); break;
                    case KeyCode.A: case KeyCode.LeftArrow:  AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.left * step; e.Use(); break;
                    case KeyCode.D: case KeyCode.RightArrow: AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.right * step; e.Use(); break;
                    case KeyCode.E: AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.up * step; e.Use(); break;
                    case KeyCode.Q: AutoOrbitTarget = false; PuppetActive = true; ManualTargetLocalPos += Vector3.down * step; e.Use(); break;
                }
            }

            GUI.Box(new Rect(12, 12, 515, 228), "");
            GUILayout.BeginArea(new Rect(20, 18, 498, 216));
            GUILayout.Label($"<b>KINETIC-CAD XR | NVIDIA NEMOTRON x NEBIUS PHYSICAL AI</b>  ({ManualTestWidthMm:F0} mm)");
            GUILayout.Label($"Engine: {ActiveNebiusModel} ({LastApiLatencyMs} ms) | {(LastSpecFromAi ? "<color=#20FF60>[AI Spec ✓]</color>" : "<color=#FFB020>[Parametric Fallback]</color>")} | μ = {CurrentManipulability:E2}");
            GUILayout.Label($"Audit: {EngineeringAuditReport}");
            GUILayout.Label($"Status: {StatusMessage}");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Caliper: {ManualTestWidthMm:F0}mm", GUILayout.Width(100));
            double newWidth = GUILayout.HorizontalSlider((float)ManualTestWidthMm, 20f, 250f, GUILayout.Width(210));
            if (System.Math.Abs(newWidth - ManualTestWidthMm) > 0.5)
            {
                ManualTestWidthMm = newWidth;
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fast Synth [G]")) SynthesizeFast();
            if (GUILayout.Button("Deep Audit [H]")) SynthesizeDeep();
            if (GUILayout.Button("Export URDF [U]")) ExportUrdf();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(PuppetActive ? "Puppet: ON [P]" : "Puppet: OFF [P]")) TogglePuppet();
            if (GUILayout.Button(AutoOrbitTarget ? "Orbit: ON [O]" : "Orbit: OFF [O]")) ToggleAutoOrbit();
            if (GUILayout.Button("Reset [R]")) ResetPose();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
#endif

        private void UpdateVrHologramText()
        {
            if (_vrHologramText == null || _theta == null || _theta.Length < 3) return;
            if (!AutoOrbitTarget && PuppetActive) _stepPuppetDone = true;

            string specTag = LastSpecFromAi ? "[AI Spec ✓]" : "[PoE Parametric]";
            string c1 = _stepSynthDone ? "✓" : "1";
            string c2 = _stepPuppetDone ? "✓" : "2";
            string c3 = CompletedGraspCount > 0 ? "✓" : "3";
            string c4 = _stepAuditOrExportDone ? "✓" : "4";
            string workflowBar = $"[{c1}]Synth  [{c2}]Puppet  [{c3}]Pick&Place({CompletedGraspCount})  [{c4}]Audit/URDF";
            string challengeLabel = ChallengeTitles[Mathf.Clamp(CurrentChallengeLevel - 1, 0, ChallengeTitles.Length - 1)];
            string healthPlain = CurrentManipulability > 4e-4 ? "OPTIMAL DEXTERITY (Cyan)" : "SINGULARITY RISK (Red)";

            _vrHologramText.text =
                $"KINETIC-CAD XR | SPATIAL PHYSICAL AI STUDIO ({ManualTestWidthMm:F0}mm)\n" +
                $"Workflow: {workflowBar} | {challengeLabel} {LastDeliveryStarRating}\n" +
                $"J1={_theta[0] * Mathf.Rad2Deg:F1}°  J2={_theta[1] * Mathf.Rad2Deg:F1}°  J3={_theta[2] * Mathf.Rad2Deg:F1}° | {healthPlain} (μ={CurrentManipulability:E2})\n" +
                $"Audit: {EngineeringAuditReport}\n" +
                $"{StatusMessage}";

            if (_urdfInspectorText != null && CurrentSpec != null)
            {
                if (_urdfInspectorTransform != null && _headCamera != null)
                {
                    Vector3 toInsp = _urdfInspectorTransform.position - _headCamera.transform.position;
                    if (toInsp.sqrMagnitude > 0.001f)
                        _urdfInspectorTransform.rotation = Quaternion.LookRotation(toInsp.normalized, Vector3.up);
                }

                string rName = CurrentSpec.robotName ?? "kinetic_claw";
                double l1 = (CurrentSpec.links != null && CurrentSpec.links.Length > 1) ? CurrentSpec.links[1].lengthM : 0.14;
                double l2 = (CurrentSpec.links != null && CurrentSpec.links.Length > 2) ? CurrentSpec.links[2].lengthM : 0.11;
                _urdfInspectorText.text =
                    $"=== ROS 2 URDF + 3D-PRINT STL INSPECTOR ===\n" +
                    $"<robot name=\"{rName}\"> | {rName}_cad.stl\n" +
                    $"  Links: L1={l1 * 1000:F0}mm, L2={l2 * 1000:F0}mm, Jaw={ManualTestWidthMm:F0}mm\n" +
                    $"  UDP :9870 -> {{\"q\":[{_theta[0]:F2},{_theta[1]:F2},{_theta[2]:F2}],\"mu\":{CurrentManipulability:F4}}}\n" +
                    $"  Pick-and-Place: {(_isHoldingWorkpiece ? "CARRYING -> Move to Drop Zone" : $"Ready ({CompletedGraspCount} placed | {LastDeliveryStarRating})")}";
            }

            if (_specSheetText != null && CurrentSpec != null)
            {
                if (_specSheetTransform != null && _headCamera != null)
                {
                    Vector3 toSpec = _specSheetTransform.position - _headCamera.transform.position;
                    if (toSpec.sqrMagnitude > 0.001f)
                        _specSheetTransform.rotation = Quaternion.LookRotation(toSpec.normalized, Vector3.up);
                }

                double mTotal = 0.0;
                if (CurrentSpec.links != null)
                    for (int i = 0; i < CurrentSpec.links.Length; i++)
                        mTotal += CurrentSpec.links[i].massKg;
                double l1 = (CurrentSpec.links != null && CurrentSpec.links.Length > 1) ? CurrentSpec.links[1].lengthM : 0.14;
                double l2 = (CurrentSpec.links != null && CurrentSpec.links.Length > 2) ? CurrentSpec.links[2].lengthM : 0.11;
                double tauPeak = 9.81 * (0.50 + mTotal * 0.4) * (l1 + l2);
                string[] topoNames = { "Articulated 3R Claw", "SCARA Selective Arm", "Heavy Turret Palletizer", "Precision Wrist Probe" };
                string topoLabel = topoNames[((_topologyVariantIndex % 4) + 4) % 4];

                _specSheetText.text =
                    $"=== PoE SCREW & TORQUE SHEET ===\n" +
                    $"Topology: {topoLabel} ({specTag})\n" +
                    $"Reach={(l1 + l2) * 1000:F0}mm | Mass={mTotal:F2}kg | Peak τ={tauPeak:F2}Nm\n" +
                    $"DLS IK: λ=0.06 (90Hz) | Health={(CurrentManipulability > 4e-4 ? "OPTIMAL (CYAN)" : "NEAR-SINGULAR")}\n" +
                    $"Input: Direct Finger Poke + Gaze & Pinch";
            }
        }

        // ------------------------------------------------------------------ Synthesis / audit / export

        public async void TriggerAsyncSynthesis(double widthMm)
        {
            // Always apply the selected topology immediately (<1ms) so hands-first button pokes and caliper locks
            // give instant 90Hz visual + tactile audio response on Quest 3 without waiting on network latency!
            UpdateSampleWorkpieceVisual(widthMm);
            MechanismSpec instantSpec = NebiusKinematicSynthesizer.BuildDeterministicMechanism(widthMm, _topologyVariantIndex);
            ApplyMechanismSpec(instantSpec);
            _spawnAnimTimer = 1.0f;

            string[] topoNames = { "Articulated 3R Claw", "SCARA Selective Arm", "Heavy Turret Palletizer", "Precision Wrist Probe" };
            string topoLabel = topoNames[((_topologyVariantIndex % 4) + 4) % 4];
            StatusMessage = $"SYNTHESIZED: {topoLabel} ({widthMm:F0}mm jaw span)";
            WriteDiagnosticState("SynthesizeFast_Instant");

            if (IsSynthesisInFlight) return;
            var synth = new NebiusKinematicSynthesizer();
            if (!synth.HasValidKey)
            {
                ActiveNebiusModel = "On-Device PoE Parametric Engine";
                LastSpecSource = "on-device PoE parametric";
                LastSpecFromAi = false;
                LastApiLatencyMs = 0;
                return;
            }

            IsSynthesisInFlight = true;
            try
            {
                MechanismSpec aiSpec = await synth.SynthesizeMechanismAsync(widthMm, $"{UserIntentPrompt} ({topoLabel})", _topologyVariantIndex);
                if (this == null) return;
                ActiveNebiusModel = synth.LastModelUsed;
                LastSpecSource = synth.LastSpecSource;
                LastSpecFromAi = synth.LastSpecFromAi;
                LastApiLatencyMs = synth.LastLatencyMs;
                if (LastSpecFromAi && aiSpec != null && _topologyVariantIndex == 0)
                {
                    ApplyMechanismSpec(aiSpec);
                    _spawnAnimTimer = 0.7f;
                }
                StatusMessage = $"SYNTHESIZED: {topoLabel} ({widthMm:F0}mm | {LastSpecSource}, {LastApiLatencyMs}ms)";
                WriteDiagnosticState("SynthesizeFast");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                IsSynthesisInFlight = false;
            }
        }

        public async void TriggerUltraEngineeringAudit()
        {
            if (CurrentSpec == null) return;
            try
            {
                _stepAuditOrExportDone = true;
                _auditSweepTimer = 1.2f; // Trigger visible 3D kinematic inspection sweep!
                StatusMessage = "DEEP AUDIT: Running Kinematic & Torque Sweep...";
                var synth = new NebiusKinematicSynthesizer();
                string report = await synth.RunDeepKinematicAuditUltraAsync(CurrentSpec, CurrentManipulability);
                if (this == null) return;
                EngineeringAuditReport = report;
                ActiveNebiusModel = synth.LastModelUsed;
                LastApiLatencyMs = synth.LastLatencyMs;
                StatusMessage = $"DEEP AUDIT DONE ({LastApiLatencyMs}ms): {report}";
                WriteDiagnosticState("SynthesizeDeep");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusMessage = "Audit failed";
            }
        }

        public void SynthesizeAndSpawnSync(double widthMm)
        {
            UpdateSampleWorkpieceVisual(widthMm);
            MechanismSpec spec = NebiusKinematicSynthesizer.BuildDeterministicMechanism(widthMm, _topologyVariantIndex);
            ApplyMechanismSpec(spec);
        }

        public void ExportCurrentUrdf()
        {
            if (CurrentSpec == null) return;
            try
            {
                _stepAuditOrExportDone = true;
                LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, Application.persistentDataPath);
                string stlPath = UrdfSerializer.SaveStlToFile(CurrentSpec, Application.persistentDataPath);
                if (Application.isEditor)
                {
                    try
                    {
                        DirectoryInfo rootDir = Directory.GetParent(Application.dataPath);
                        if (rootDir != null)
                        {
                            string exportsDir = Path.Combine(rootDir.FullName, "Exports");
                            LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, exportsDir);
                            stlPath = UrdfSerializer.SaveStlToFile(CurrentSpec, exportsDir);
                        }
                    }
                    catch { }
                }
                _exportFlashTimer = 0.8f; // Flash robot links bright emerald green to confirm URDF + STL export!
                SpatialTactileAudio.Instance?.PlayExportSuccess();
                StatusMessage = $"EXPORTED -> {Path.GetFileName(LastExportedUrdfPath)} + {Path.GetFileName(stlPath)} (ROS 2 & 3D Print Ready)";
                Debug.Log($"[KineticCAD] Exported URDF: {LastExportedUrdfPath} | STL: {stlPath}");
                WriteDiagnosticState("ExportUrdf");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusMessage = "URDF/STL export failed (see log)";
            }
        }

        private void ApplyMechanismSpec(MechanismSpec spec)
        {
            if (_isHoldingWorkpiece)
            {
                _isHoldingWorkpiece = false;
                if (_sampleWorkpiece != null)
                {
                    _sampleWorkpiece.localRotation = Quaternion.identity;
                    if (_workpieceRenderer != null)
                        SetColorSafe(_workpieceRenderer, new Color(0.18f, 0.55f, 0.72f));
                }
            }
            CurrentSpec = spec;
            CurrentChain = spec.ToPoEChain(out _minLimits, out _maxLimits);
            _theta = new double[CurrentChain.Joints.Length];
            for (int i = 0; i < _theta.Length && i < HomeTheta.Length; i++)
                _theta[i] = HomeTheta[i];

            ReferenceManipulability = System.Math.Max(1e-5, DlsInverseKinematics.ComputeManipulabilityIndex(CurrentChain, _theta));
            double l1 = (spec.links != null && spec.links.Length > 1) ? spec.links[1].lengthM : 0.14;
            double l2 = (spec.links != null && spec.links.Length > 2) ? spec.links[2].lengthM : 0.11;
            UpdateReachEnvelopeAndStationPositions((float)(l1 + l2));
            RebuildVisualHierarchy(spec);
            CurrentManipulability = ReferenceManipulability;
            CurrentEndEffectorPos = CurrentChain.EndEffectorPosition(_theta);
            UpdateVisualLinksFromPoE();
            SpatialTactileAudio.Instance?.PlaySynthChime();
        }

        // ------------------------------------------------------------------ Visuals

        private void RebuildVisualHierarchy(MechanismSpec spec)
        {
            if (_mechanismRoot != null) Destroy(_mechanismRoot);

            _mechanismRoot = new GameObject($"Mechanism_{spec.robotName}");
            _mechanismRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            _mechanismRoot.transform.localPosition = MechanismRootLocalPos;
            _mechanismRoot.transform.localRotation = Quaternion.identity;

            int linkCount = spec.links != null ? spec.links.Length : 0;
            if (linkCount > 0)
            {
                LinkSpec b = spec.links[0];
                GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseObj.name = b.name;
                Destroy(baseObj.GetComponent<Collider>());
                baseObj.transform.SetParent(_mechanismRoot.transform, false);
                baseObj.transform.localPosition = new Vector3((float)b.centerX, (float)b.centerY, (float)b.centerZ);
                baseObj.transform.localScale = new Vector3((float)b.widthM, (float)(b.heightM * 0.5), (float)b.lengthM);
                SetColorSafe(baseObj.GetComponent<Renderer>(), new Color(0.35f, 0.42f, 0.52f));
            }

            int n = spec.joints != null ? spec.joints.Length : 0;
            _jointPivots = new Transform[n];
            _linkRenderers = new Renderer[n];
            _pinRenderers = new Renderer[n];

            for (int i = 0; i < n; i++)
            {
                JointSpec j = spec.joints[i];
                GameObject pivot = new GameObject($"Joint_{i}_{j.name}");
                pivot.transform.SetParent(_mechanismRoot.transform, false);
                _jointPivots[i] = pivot.transform;

                GameObject pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pin.name = "ScrewAxisPin";
                Destroy(pin.GetComponent<Collider>());
                pin.transform.SetParent(pivot.transform, false);
                pin.transform.localPosition = new Vector3((float)j.originX, (float)j.originY, (float)j.originZ);
                Vector3 axisDir = new Vector3((float)j.axisX, (float)j.axisY, (float)j.axisZ).normalized;
                if (axisDir.sqrMagnitude > 0.001f)
                    pin.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axisDir);
                pin.transform.localScale = new Vector3(0.020f, 0.024f, 0.020f);
                _pinRenderers[i] = pin.GetComponent<Renderer>();
                SetColorSafe(_pinRenderers[i], new Color(0.0f, 0.95f, 1.0f));
            }

            int movingLinks = System.Math.Max(0, linkCount - 1);
            int offset = System.Math.Max(0, n - movingLinks);
            for (int k = 1; k < linkCount; k++)
            {
                int pivotIndex = System.Math.Min(n - 1, k - 1 + offset);
                if (pivotIndex < 0) break;
                LinkSpec lk = spec.links[k];
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = lk.name;
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(_jointPivots[pivotIndex], false);
                box.transform.localPosition = new Vector3((float)lk.centerX, (float)lk.centerY, (float)lk.centerZ);
                box.transform.localScale = new Vector3((float)lk.lengthM, (float)lk.heightM, (float)lk.widthM);
                Renderer r = box.GetComponent<Renderer>();
                if (_linkRenderers[pivotIndex] == null) _linkRenderers[pivotIndex] = r;
                SetColorSafe(r, HealthyCyan);
            }

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "EndEffector_Green";
            Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(_mechanismRoot.transform, false);
            tip.transform.localScale = Vector3.one * 0.022f;
            SetColorSafe(tip.GetComponent<Renderer>(), new Color(0.2f, 1.0f, 0.4f));
            _eeMarker = tip.transform;

            // Articulated Parallel Jaw Gripper Assembly scaled to measured caliper width
            float jawSpanM = Mathf.Clamp((float)(spec.targetCaliperWidthMm * 0.001), 0.024f, 0.22f);
            GameObject crossbar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crossbar.name = "Gripper_CrossbarBracket";
            Destroy(crossbar.GetComponent<Collider>());
            crossbar.transform.SetParent(_eeMarker, false);
            crossbar.transform.localPosition = new Vector3(0f, 0f, 0f);
            crossbar.transform.localScale = new Vector3(0.45f, 0.35f, (jawSpanM + 0.016f) / 0.022f);
            SetColorSafe(crossbar.GetComponent<Renderer>(), new Color(0.22f, 0.28f, 0.36f));

            GameObject leftJaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftJaw.name = "Gripper_LeftJawFinger";
            Destroy(leftJaw.GetComponent<Collider>());
            leftJaw.transform.SetParent(_eeMarker, false);
            leftJaw.transform.localScale = new Vector3(1.35f, 0.55f, 0.28f);
            SetColorSafe(leftJaw.GetComponent<Renderer>(), new Color(0.15f, 0.95f, 0.55f));
            _leftJawFinger = leftJaw.transform;

            GameObject rightJaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightJaw.name = "Gripper_RightJawFinger";
            Destroy(rightJaw.GetComponent<Collider>());
            rightJaw.transform.SetParent(_eeMarker, false);
            rightJaw.transform.localScale = new Vector3(1.35f, 0.55f, 0.28f);
            SetColorSafe(rightJaw.GetComponent<Renderer>(), new Color(0.15f, 0.95f, 0.55f));
            _rightJawFinger = rightJaw.transform;
        }

        private void UpdateVisualLinksFromPoE()
        {
            if (_jointPivots == null) return;
            Mat4[] jointTransforms = CurrentChain.ComputeCumulativeJointTransforms(_theta);
            float health = Mathf.Clamp01((float)(CurrentManipulability / ReferenceManipulability));
            Color linkColor = Color.Lerp(WarningRed, HealthyCyan, health);

            if (_auditSweepTimer > 0f)
                linkColor = AuditGold;
            else if (_exportFlashTimer > 0f)
                linkColor = ExportGreen;

            Quaternion lastRot = Quaternion.identity;
            for (int i = 0; i < jointTransforms.Length && i < _jointPivots.Length; i++)
            {
                Vec3 pos = jointTransforms[i].GetTranslation();
                jointTransforms[i].ToQuaternion(out double qx, out double qy, out double qz, out double qw);
                _jointPivots[i].localPosition = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                lastRot = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
                _jointPivots[i].localRotation = lastRot;
                if (_linkRenderers[i] != null)
                    SetColorSafe(_linkRenderers[i], linkColor);

                if (_pinRenderers != null && i < _pinRenderers.Length && _pinRenderers[i] != null &&
                    _minLimits != null && _maxLimits != null && i < _minLimits.Length)
                {
                    double span = System.Math.Max(0.2, _maxLimits[i] - _minLimits[i]);
                    double margin = System.Math.Min(_theta[i] - _minLimits[i], _maxLimits[i] - _theta[i]);
                    float limitSafety = Mathf.Clamp01((float)(margin / (span * 0.22)));
                    Color pinCol = Color.Lerp(new Color(1.0f, 0.35f, 0.15f), new Color(0.0f, 0.95f, 1.0f), limitSafety);
                    SetColorSafe(_pinRenderers[i], pinCol);
                }
            }

            if (_reachEnvelopeRenderers != null)
            {
                for (int i = 0; i < _reachEnvelopeRenderers.Length; i++)
                {
                    float wave = 0.70f + 0.30f * Mathf.Sin(Time.time * 3.2f - i * 0.32f);
                    SetColorSafe(_reachEnvelopeRenderers[i], linkColor * wave);
                }
            }

            if (_objectiveBeaconRing != null)
            {
                Vector3 beaconBase = _isHoldingWorkpiece ? _dropZoneLocalPos : _workpieceHomeLocalPos;
                float bob = 0.044f + 0.006f * Mathf.Sin(Time.time * 4.2f);
                _objectiveBeaconRing.localPosition = beaconBase + new Vector3(0f, bob, 0f);
                float pulse = 0.044f + 0.006f * Mathf.Cos(Time.time * 4.2f);
                _objectiveBeaconRing.localScale = new Vector3(pulse, 0.0018f, pulse);
                if (_objectiveBeaconRenderer != null)
                {
                    Color beaconCol = _isHoldingWorkpiece
                        ? new Color(0.20f, 1.0f, 0.55f)
                        : new Color(0.20f, 0.92f, 1.0f);
                    SetColorSafe(_objectiveBeaconRenderer, beaconCol);
                }
            }
            if (_eeMarker != null)
            {
                _eeMarker.localPosition = new Vector3((float)CurrentEndEffectorPos.X, (float)CurrentEndEffectorPos.Y, (float)CurrentEndEffectorPos.Z);
                _eeMarker.localRotation = lastRot;
                UpdateTrajectoryTrail(linkColor);
                UpdateInteractivePickAndPlace();
            }
        }

        private void UpdateInteractivePickAndPlace()
        {
            if (_eeMarker == null || _sampleWorkpiece == null || _auditSweepTimer > 0f) return;

            if (_graspCooldownTimer > 0f)
                _graspCooldownTimer = Mathf.Max(0f, _graspCooldownTimer - Time.deltaTime);

            if (_dropZoneFlashTimer > 0f)
            {
                _dropZoneFlashTimer = Mathf.Max(0f, _dropZoneFlashTimer - Time.deltaTime);
                if (_dropZoneRenderer != null)
                    SetColorSafe(_dropZoneRenderer, _dropZoneFlashTimer > 0.05f ? ExportGreen : new Color(0.12f, 0.65f, 0.42f));
            }

            Vector3 eeLocal = DeskWorkspaceOrigin.InverseTransformPoint(_eeMarker.position);

            if (!_isHoldingWorkpiece)
            {
                float distToWp = Vector3.Distance(eeLocal, _sampleWorkpiece.localPosition);
                if (distToWp < 0.058f && _graspCooldownTimer <= 0f && !AutoOrbitTarget)
                {
                    _isHoldingWorkpiece = true;
                    _minManipulabilityDuringCarry = CurrentManipulability;
                    SpatialTactileAudio.Instance?.PlayCaliperLock(_eeMarker.position);
                    if (_workpieceRenderer != null)
                        SetColorSafe(_workpieceRenderer, new Color(0.25f, 1.0f, 0.55f));
                    if (_workpieceLabel != null)
                        _workpieceLabel.text = $"GRASPED ({ManualTestWidthMm:F0} mm)\nMove to Drop Zone ->";
                    StatusMessage = $"GRASP LOCKED (Lv{CurrentChallengeLevel}) -> Keep arm cyan & deliver to Drop Zone!";
                }
            }
            else
            {
                if (CurrentManipulability < _minManipulabilityDuringCarry)
                    _minManipulabilityDuringCarry = CurrentManipulability;

                // Carry workpiece inside parallel gripper jaws
                _sampleWorkpiece.position = _eeMarker.position + _eeMarker.right * 0.014f;
                _sampleWorkpiece.rotation = _eeMarker.rotation;

                if (_workpieceLabel != null)
                    _workpieceLabel.transform.localPosition = DeskWorkspaceOrigin.InverseTransformPoint(_sampleWorkpiece.position) + new Vector3(0f, 0.026f, 0f);

                float distToDrop = Vector2.Distance(
                    new Vector2(eeLocal.x, eeLocal.z),
                    new Vector2(_dropZoneLocalPos.x, _dropZoneLocalPos.z));

                if (distToDrop < 0.068f && eeLocal.y < 0.095f)
                {
                    _isHoldingWorkpiece = false;
                    _graspCooldownTimer = 1.4f;
                    _dropZoneFlashTimer = 0.9f;
                    CompletedGraspCount++;
                    LastDeliveryStarRating = _minManipulabilityDuringCarry > 4.5e-4 ? "★★★" : (_minManipulabilityDuringCarry > 2.2e-4 ? "★★☆" : "★☆☆");
                    CurrentChallengeLevel = (CompletedGraspCount % ChallengeTitles.Length) + 1;
                    SpatialTactileAudio.Instance?.PlayExportSuccess();

                    UpdateReachEnvelopeAndStationPositions(_lastReachMeters);
                    _sampleWorkpiece.localPosition = _workpieceHomeLocalPos;
                    _sampleWorkpiece.localRotation = Quaternion.identity;
                    if (_workpieceRenderer != null)
                        SetColorSafe(_workpieceRenderer, new Color(0.18f, 0.55f, 0.72f));
                    if (_workpieceLabel != null)
                    {
                        _workpieceLabel.transform.localPosition = _workpieceHomeLocalPos + new Vector3(0f, 0.024f, 0f);
                        _workpieceLabel.text = $"PICKUP WORKPIECE\n{ManualTestWidthMm:F0} mm";
                    }
                    if (_dropZoneLabel != null)
                        _dropZoneLabel.text = $"DROP ZONE (Lv{CurrentChallengeLevel})\nGrasps: {CompletedGraspCount} | {LastDeliveryStarRating}";

                    StatusMessage = $"DELIVERY #{CompletedGraspCount} COMPLETE ({LastDeliveryStarRating}) -> Advanced to {ChallengeTitles[CurrentChallengeLevel - 1]}!";
                }
            }

            // Animate parallel jaw fingers opening/closing
            float targetClose = _isHoldingWorkpiece ? 1.0f : 0.0f;
            _jawCloseFactor = Mathf.MoveTowards(_jawCloseFactor, targetClose, Time.deltaTime * 6.5f);
            if (_leftJawFinger != null && _rightJawFinger != null)
            {
                float jawSpanM = Mathf.Clamp((float)(ManualTestWidthMm * 0.001), 0.024f, 0.22f);
                float openHalfLocal = (jawSpanM * 0.56f) / 0.022f;
                float closedHalfLocal = (jawSpanM * 0.36f) / 0.022f;
                float zOffset = Mathf.Lerp(openHalfLocal, closedHalfLocal, _jawCloseFactor);
                _leftJawFinger.localPosition = new Vector3(0.65f, 0f, -zOffset);
                _rightJawFinger.localPosition = new Vector3(0.65f, 0f, zOffset);
            }
        }

        private static Material _sharedSolidLitMaterial;

        public static Material GetSharedSolidLitMaterial()
        {
            if (_sharedSolidLitMaterial != null) return _sharedSolidLitMaterial;
            Shader s = Resources.Load<Shader>("KineticSolidLit");
            if (s == null) s = Shader.Find("KineticCore/SolidLit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null)
            {
                _sharedSolidLitMaterial = new Material(s);
                _sharedSolidLitMaterial.name = "KineticSolidLit_SharedMat";
                _sharedSolidLitMaterial.enableInstancing = false;
            }
            return _sharedSolidLitMaterial;
        }

        public void SetColorSafe(Renderer r, Color c)
        {
            if (r == null) return;
            Material solidMat = GetSharedSolidLitMaterial();
            if (solidMat != null && r.sharedMaterial != solidMat)
                r.sharedMaterial = solidMat;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
