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
        public bool PureCadStudioMode = true;
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
        private static readonly double[] DemoPresetWidthsMm = { 70.0, 145.0, 240.0, 380.0, 520.0 };
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
        private bool _stepSynthDone = true; // Step 1 ready on boot via AEGIS Starter Template
        private bool _stepPuppetDone = false;
        private bool _stepAuditOrExportDone = false;

        // "Spatial Fusion 360" Real-World 3D Object Capture, Left-Hand Spatial CAD Palette, Multi-Body Tree & 1:1 Right-Hand Print Preview
        public double CapturedDepthMm = 55.0;
        public double CapturedHeightMm = 42.0;
        public Vector3 ModelTranslationMm = Vector3.zero;
        public Vector3 ModelTiltDeg = Vector3.zero;
        private int _tiltPresetStepIndex = 0;
        public string LastExportedStlPath { get; private set; } = "";
        public string LastExportedObjPath { get; private set; } = "";
        public int CadPrimitiveShapeIndex { get; private set; } = 0;
        public int CadBooleanModeIndex { get; private set; } = 0;
        public int CadPatternModeIndex { get; private set; } = 0;
        public bool DirectPushPullActive { get; private set; } = false;
        private int _pushPullStepIndex = 0;
        public int CadFeatureOpIndex { get; private set; } = 0;
        public int CadMaterialIndex { get; private set; } = 0;
        public int SnappedModuleCount { get; private set; } = 0;
        public bool HandInspectActive { get; private set; } = false;
        public double CadPartVolumeCm3 { get; private set; } = 38.0;
        public double CadPartMassGrams { get; private set; } = 47.1;
        public int EstimatedPrintMinutes { get; private set; } = 26;
        private readonly List<StampedCadBodySpec> _stampedCadBodies = new List<StampedCadBodySpec>();
        public IReadOnlyList<StampedCadBodySpec> StampedCadBodies => _stampedCadBodies;
        public int StampedBodyCount => _stampedCadBodies.Count;
        public string ActivePrimitiveShapeName => CadPrimitiveShapeNames[CadPrimitiveShapeIndex % CadPrimitiveShapeNames.Length];
        public string ActiveBooleanModeName => CadBooleanModeNames[CadBooleanModeIndex % CadBooleanModeNames.Length];
        public string ActivePatternModeName => CadPatternModeNames[CadPatternModeIndex % CadPatternModeNames.Length];

        private static readonly string[] CadPrimitiveShapeNames =
        {
            "Box / Cube",
            "Cylinder",
            "Sphere",
            "Cone / Frustum",
            "Torus Ring",
            "Hollow Tube",
            "Hex Prism (M-Nut)",
            "Wedge / 45° Ramp",
            "L-Bracket",
            "U-Channel",
            "T-Beam Extrusion",
            "Flanged Bearing"
        };
        private static readonly string[] CadBooleanModeNames =
        {
            "Join (Add Solid)",
            "Cut (Hole / Subtract)",
            "Intersect"
        };
        private static readonly string[] CadPatternModeNames =
        {
            "Single (1x)",
            "Linear Pattern (3x)",
            "Circular Pattern (6x)"
        };
        private static readonly string[] CadFeatureOpNames =
        {
            "Solid Extrude",
            "Shell (2.5mm Hollow)",
            "Chamfer + M4 Bosses",
            "L-Bracket Ribbed"
        };
        private static readonly string[] CadMaterialNames =
        {
            "PLA+ (1.24g/cm³)",
            "PETG-CF (1.38g/cm³)",
            "6061-T6 Al (2.70g/cm³)",
            "Ti-6Al-4V (4.43g/cm³)"
        };
        private static readonly double[] CadMaterialDensities = { 1.24, 1.38, 2.70, 4.43 };
        private static readonly Color[] CadMaterialColors =
        {
            new Color(0.18f, 0.58f, 0.78f),
            new Color(0.24f, 0.28f, 0.34f),
            new Color(0.70f, 0.77f, 0.85f),
            new Color(0.58f, 0.54f, 0.64f)
        };
        private readonly List<GameObject> _workpieceCadSubParts = new List<GameObject>();
        private GameObject _handInspectRoot;
        private readonly List<GameObject> _handInspectSubParts = new List<GameObject>();
        private TextMesh _handInspectLabel;
        private GameObject _exportedGalleryRoot;
        private Transform _exportedGalleryModelContainer;
        private readonly List<GameObject> _exportedGallerySubParts = new List<GameObject>();
        private TextMesh _exportedGalleryLabel;
        private Transform _distalSnapPortRing;
        private HandFingertipReader _cachedTipReader;

        // Upgrade 1: In-Place Passthrough Caliper Bounding Twin & Direct 3D Face-Pull + 3D Tilt Gizmo Handles
        private GameObject _inPlaceTwinRoot;
        private readonly Transform[] _inPlaceTwinEdges = new Transform[12];
        private readonly Renderer[] _inPlaceTwinEdgeRenderers = new Renderer[12];
        private TextMesh _inPlaceTwinLabel;
        private Vector3 _inPlaceTwinLocalCenter = new Vector3(-0.02f, 0.021f, -0.045f);
        private bool _hasInPlaceTwinCapture = false;
        private readonly Transform[] _facePullHandles = new Transform[3];
        private readonly Renderer[] _facePullRenderers = new Renderer[3];
        private readonly Color[] _facePullBaseColors =
        {
            new Color(1.0f, 0.32f, 0.22f), // +X Width Handle (Coral Red)
            new Color(0.20f, 0.95f, 0.45f), // +Y Height Handle (Emerald Green)
            new Color(0.18f, 0.78f, 1.0f)   // +Z Depth Handle (Sky Blue)
        };
        public Transform[] FacePullHandles => _facePullHandles;
        private int _activeFacePullAxis = -1;
        private int _lastFacePullTickBucket = -1;

        // 3D Angular Rotation / Tilt Handles (Pitch X°, Yaw Y°, Roll Z°)
        private readonly Transform[] _tiltHandles = new Transform[3];
        private readonly Renderer[] _tiltHandleRenderers = new Renderer[3];
        private readonly Color[] _tiltHandleBaseColors =
        {
            new Color(1.0f, 0.62f, 0.15f), // TILT X° Pitch (Amber Orange)
            new Color(0.88f, 0.32f, 1.0f), // TILT Y° Yaw (Neon Violet)
            new Color(1.0f, 0.90f, 0.22f)  // TILT Z° Roll (Electric Gold)
        };
        public Transform[] TiltHandles => _tiltHandles;
        private int _activeTiltAxis = -1;
        private int _lastTiltTickBucket = -999;

        // Upgrade 2: Interactive 3D Boolean CSG Positioning Cursor (2mm CAD grid snapping before stamping)
        private GameObject _liveBooleanCursorObj;
        private Renderer _liveBooleanCursorRenderer;
        private bool _hasAimedBooleanCursor = false;
        private Vector3 _aimedBooleanOffsetMm = Vector3.zero;
        private float _booleanCursorActiveTimer = 0f;

        // Upgrade 3: True Drag-Carry-and-Snap Module Carrier & Magnetic Snap Tether
        private GameObject _dragCarryGhostObj;
        private Renderer _dragCarryGhostRenderer;
        private readonly Transform[] _magneticTetherSegments = new Transform[6];
        private readonly Renderer[] _magneticTetherRenderers = new Renderer[6];
        private bool _isCarryingSnapModule = false;
        private float _carrySnapCooldown = 0f;
        private readonly List<StampedCadBodySpec> _snappedModuleCustomSpecs = new List<StampedCadBodySpec>();

        // Upgrade 4: Direct Screw-Axis Joint Pin Touch Inspector (S_i Axis & Revolute/Prismatic Flipper)
        private Transform[] _pinTransforms;
        public Transform[] PinTransforms => _pinTransforms;
        private readonly int[] _jointAxisOverrides = new int[8];
        private int _lastInspectedJointIdx = -1;
        private float _pinPokeCooldown = 0f;

        // Swept-Volume CSG "Ghost Carving" (W_i) & Control Barrier Function (h(x) >= 0) state
        public bool RecordEnvelopeActive { get; private set; } = true;
        public bool IsHousingCarved { get; private set; } = false;
        public double CurrentBarrierHxMm { get; private set; } = -11.4;
        public int SweptSampleCount { get; private set; } = 0;
        public int CarvedVoxelCount { get; private set; } = 0;
        public int ClashedVoxelCount { get; private set; } = 0;
        public string AgentRecommendation { get; private set; } = "FUSION 360 TIP: Drag +X / +Y / +Z handles to Extrude, or poke Left-Hand Palette buttons";
        public int CareerDeliveries { get; private set; } = 0;
        public int CareerHousingsCarved { get; private set; } = 0;
        public int CareerUrdfExports { get; private set; } = 0;
        private const int MaxSweptGhostCapsules = 24;
        private Transform[] _sweptGhostCapsules;
        private Renderer[] _sweptGhostRenderers;
        private Vector3[] _sweptCapsuleCenters;
        private Vector3[] _sweptSegmentStarts;
        private Vector3[] _sweptSegmentEnds;
        private float[] _sweptCapsuleRadii;
        private int _sweptWriteIdx = 0;
        private double[] _lastSampledTheta = new double[3];

        // AEGIS Drone Payload Bay Mounting Housing Block (Voxelized Boolean CSG Grid)
        private const int HousingGridX = 6;
        private const int HousingGridY = 3;
        private const int HousingGridZ = 4;
        private const int TotalHousingVoxels = HousingGridX * HousingGridY * HousingGridZ;
        private static readonly Vector3 VoxelLatticeScale = new Vector3(0.0115f, 0.0115f, 0.0125f);
        private static readonly Vector3 VoxelClashScale = new Vector3(0.0170f, 0.0170f, 0.0185f);
        private Transform[] _housingVoxelTransforms;
        private Renderer[] _housingVoxelRenderers;
        private Vector3[] _housingVoxelLocalCenters;
        private bool[] _housingVoxelClashed;
        private bool[] _housingVoxelCarved;
        private TextMesh _housingLabel;

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
        private Vector3 _workpieceHomeLocalPos = new Vector3(0.05f, 0.008f, 0.01f);
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
            CareerDeliveries = PlayerPrefs.GetInt("KineticCAD_TotalDeliveries", 0);
            CareerHousingsCarved = PlayerPrefs.GetInt("KineticCAD_TotalCarves", 0);
            CareerUrdfExports = PlayerPrefs.GetInt("KineticCAD_TotalExports", 0);

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
                ? "FUSION 360 STUDIO: Drag +X / +Y / +Z handles or click 3D CAD buttons | RMB orbit"
                : "FUSION 360 STUDIO: Drag +X / +Y / +Z handles to Extrude | Point Both Index Tips to measure | Poke buttons";

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
            ResetPayloadHousingAndEnvelope();
            StatusMessage = "CENTER DESK -> Centered 3D CAD Workbench in front of your headset";
            WriteDiagnosticState("ResetPose");
        }

        [Preserve]
        public void SynthesizeFast()
        {
            _stepSynthDone = true;
            _topologyVariantIndex = (_topologyVariantIndex + 1) % 4;
            ResetPayloadHousingAndEnvelope();
            // If user is actively measuring an object with both hands in VR, use their live CaliperWidthMm;
            // otherwise cycle through distinct preset sizes so clicking Fast Synth shows a dramatic geometry change!
            var caliper = GetComponent<CaliperController>();
            if (caliper != null && caliper.IsMeasuringActive && caliper.CaliperWidthMm >= 10.0 && caliper.CaliperWidthMm <= 600.0)
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
        public void ScrubDimensionMm(double deltaMm)
        {
            _stepSynthDone = true;
            ManualTestWidthMm = System.Math.Max(10.0, System.Math.Min(600.0, ManualTestWidthMm + deltaMm));
            SpatialTactileAudio.Instance?.PlayCaliperTick();
            SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"EXTRUDE WIDTH -> Resized Width to {ManualTestWidthMm:F0} mm ({ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm | Max 600mm)";
            WriteDiagnosticState("ScrubDimensionMm");
        }

        [Preserve]
        public void ToggleEnvelopeRecording()
        {
            if (IsHousingCarved && !RecordEnvelopeActive)
            {
                // Re-enabling recording after a carve restores the housing lattice for a fresh sweep
                ResetPayloadHousingAndEnvelope();
                RecordEnvelopeActive = true;
            }
            else
            {
                RecordEnvelopeActive = !RecordEnvelopeActive;
            }
            SpatialTactileAudio.Instance?.PlayPokeClick();
            StatusMessage = RecordEnvelopeActive
                ? $"RECORD ENVELOPE [ON] -> Puppet arm to sweep W_i ({SweptSampleCount} capsules)"
                : "RECORD ENVELOPE [PAUSED] -> Thumb-Tap or Poke [GHOST CARVE] to cut housing";
            WriteDiagnosticState("ToggleEnvelopeRecording");
        }

        [Preserve]
        public void ResetPayloadHousingAndEnvelope()
        {
            if (_housingVoxelTransforms == null) return;
            IsHousingCarved = false;
            RecordEnvelopeActive = true;
            CarvedVoxelCount = 0;
            ClashedVoxelCount = 0;
            SweptSampleCount = 0;
            _sweptWriteIdx = 0;

            for (int v = 0; v < TotalHousingVoxels; v++)
            {
                _housingVoxelCarved[v] = false;
                _housingVoxelClashed[v] = false;
                if (_housingVoxelTransforms[v] != null)
                {
                    _housingVoxelTransforms[v].gameObject.SetActive(true);
                    _housingVoxelTransforms[v].localScale = VoxelLatticeScale;
                }
                if (_housingVoxelRenderers[v] != null)
                {
                    SetColorSafe(_housingVoxelRenderers[v], new Color(0.20f, 0.32f, 0.48f));
                }
            }

            if (_sweptGhostCapsules != null)
            {
                for (int i = 0; i < _sweptGhostCapsules.Length; i++)
                {
                    if (_sweptGhostCapsules[i] != null)
                        _sweptGhostCapsules[i].gameObject.SetActive(false);
                }
            }

            if (_housingLabel != null)
                _housingLabel.text = "AEGIS PAYLOAD HOUSING\n[Sweep W_i -> Thumb-Tap GHOST CARVE]";

            RecomputeControlBarrierMarginHx();
        }

        [Preserve]
        public void ExecuteGhostCarve()
        {
            if (_housingVoxelTransforms == null) return;
            _stepAuditOrExportDone = true;

            double rLinkM = (CurrentSpec?.links != null && CurrentSpec.links.Length > 1) ? CurrentSpec.links[1].widthM * 0.5 : 0.013;
            double rMarginM = 0.010;
            // Carve every voxel within r_link + r_margin + 9.5mm clearance of the swept envelope W_i & current link segments
            // so the remaining uncarved housing lattice is guaranteed to have h(x) >= +8.5mm >= 0!
            float carveThreshold = (float)(rLinkM + rMarginM) + 0.0095f;

            Vector3 shoulderLocal = (_jointPivots != null && _jointPivots.Length > 0) ? _jointPivots[0].localPosition : new Vector3(0f, 0.05f, 0f);
            Vector3 elbowLocal = (_jointPivots != null && _jointPivots.Length > 0) ? _jointPivots[_jointPivots.Length - 1].localPosition : new Vector3(0.08f, 0.06f, 0f);
            Vector3 eeLocal = (_eeMarker != null && DeskWorkspaceOrigin != null)
                ? DeskWorkspaceOrigin.InverseTransformPoint(_eeMarker.position)
                : new Vector3(0.18f, 0.06f, 0f);

            int activeSamples = Mathf.Min(SweptSampleCount, MaxSweptGhostCapsules);
            int newlyCarved = 0;

            for (int v = 0; v < TotalHousingVoxels; v++)
            {
                if (_housingVoxelCarved[v]) continue;
                Vector3 vPos = _housingVoxelLocalCenters[v];

                float minDist = Mathf.Min(
                    DistancePointToSegment(vPos, shoulderLocal, elbowLocal),
                    DistancePointToSegment(vPos, elbowLocal, eeLocal));

                for (int s = 0; s < activeSamples; s++)
                {
                    float dSeg = DistancePointToSegment(vPos, _sweptSegmentStarts[s], _sweptSegmentEnds[s]);
                    if (dSeg < minDist) minDist = dSeg;
                }

                if (_housingVoxelClashed[v] || minDist < carveThreshold)
                {
                    _housingVoxelCarved[v] = true;
                    _housingVoxelClashed[v] = false;
                    if (_housingVoxelTransforms[v] != null)
                        _housingVoxelTransforms[v].gameObject.SetActive(false);
                    newlyCarved++;
                }
            }

            CarvedVoxelCount += newlyCarved;
            IsHousingCarved = true;
            CareerHousingsCarved++;
            PlayerPrefs.SetInt("KineticCAD_TotalCarves", CareerHousingsCarved);
            PlayerPrefs.Save();

            // Color the recorded swept ghost capsules bright emerald-cyan to show verified zero-clash clearance
            if (_sweptGhostRenderers != null)
            {
                for (int i = 0; i < activeSamples; i++)
                {
                    if (_sweptGhostRenderers[i] != null)
                        SetColorSafe(_sweptGhostRenderers[i], new Color(0.15f, 0.95f, 0.72f));
                }
            }

            RecomputeControlBarrierMarginHx();

            if (_housingLabel != null)
                _housingLabel.text = $"AEGIS PAYLOAD HOUSING\n[✓ CARVED: -{CarvedVoxelCount} vox | h(x)=+{CurrentBarrierHxMm:F1}mm >= 0 SAFE]";

            SpatialTactileAudio.Instance?.PlayCaliperLock();
            ExportCurrentUrdf();
            StatusMessage = $"GHOST CARVE COMPLETE -> Carved {CarvedVoxelCount} clashing voxels | CBF h(x)=+{CurrentBarrierHxMm:F1}mm >= 0 SAFE!";
            WriteDiagnosticState("ExecuteGhostCarve");
        }

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
            ManualTestWidthMm = System.Math.Max(10.0, System.Math.Min(600.0, w));
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (triggerSynthesisNow)
                TriggerAsyncSynthesis(ManualTestWidthMm);
        }

        [Preserve]
        public void SetCapturedObjectDimensionsMm(double wMm, double dMm, double hMm, bool triggerSynthesisNow = false, Vector3? inPlaceMidpointWorld = null)
        {
            ManualTestWidthMm = System.Math.Max(10.0, System.Math.Min(600.0, wMm));
            CapturedDepthMm = System.Math.Max(10.0, System.Math.Min(600.0, dMm));
            CapturedHeightMm = System.Math.Max(10.0, System.Math.Min(600.0, hMm));

            if (inPlaceMidpointWorld.HasValue && DeskWorkspaceOrigin != null)
            {
                Vector3 localMid = DeskWorkspaceOrigin.InverseTransformPoint(inPlaceMidpointWorld.Value);
                float halfH = Mathf.Clamp((float)(CapturedHeightMm * 0.0005), 0.008f, 0.300f);
                _inPlaceTwinLocalCenter = new Vector3(
                    Mathf.Clamp(localMid.x, -0.35f, 0.35f),
                    Mathf.Clamp(localMid.y, halfH, 0.35f),
                    Mathf.Clamp(localMid.z, -0.30f, 0.30f));
                _hasInPlaceTwinCapture = true;
            }

            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (triggerSynthesisNow)
                TriggerAsyncSynthesis(ManualTestWidthMm);
        }

        [Preserve]
        public void CycleCadPrimitiveShape()
        {
            CadPrimitiveShapeIndex = (CadPrimitiveShapeIndex + 1) % CadPrimitiveShapeNames.Length;
            SpatialTactileAudio.Instance?.PlayPokeClick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"CREATE SHAPE ({CadPrimitiveShapeIndex + 1}/{CadPrimitiveShapeNames.Length}) -> [{ActivePrimitiveShapeName}] ({ManualTestWidthMm:F0} x {CapturedDepthMm:F0} x {CapturedHeightMm:F0} mm)";
            WriteDiagnosticState("CycleCadPrimitiveShape");
        }

        [Preserve]
        public void StampNewCadBodyToAssembly()
        {
            if (_stampedCadBodies.Count >= 8)
            {
                _stampedCadBodies.Clear();
                _hasAimedBooleanCursor = false;
                SpatialTactileAudio.Instance?.PlayCaliperTick();
                UpdateSampleWorkpieceVisual(ManualTestWidthMm);
                StatusMessage = "CAD BODIES -> Cleared extra bodies (Reset to 1 Base Body)";
                WriteDiagnosticState("ClearStampedCadBodies");
                return;
            }

            int idx = _stampedCadBodies.Count;
            // Coaxially stack or radially mate each newly stamped primitive onto the parametric CAD assembly,
            // OR use the live 3D Combine cursor position aimed by the user's fingertip!
            double[] offX = { 0.0,  22.0, -22.0,  0.0,  18.0, -18.0,  28.0, -28.0 };
            double[] offZ = { 0.0,   0.0,   0.0, 20.0, -18.0,  18.0,  20.0, -20.0 };
            double[] offY = { CapturedHeightMm * 0.55, 8.0, 8.0, 10.0, 14.0, 14.0, 18.0, 18.0 };
            double scale = (CadBooleanModeIndex == 1) ? 0.52 : 0.64; // Cut Hole creates precision negative bore/slot

            double targetOffX = _hasAimedBooleanCursor ? _aimedBooleanOffsetMm.x : offX[idx % offX.Length];
            double targetOffY = _hasAimedBooleanCursor ? _aimedBooleanOffsetMm.y : offY[idx % offY.Length];
            double targetOffZ = _hasAimedBooleanCursor ? _aimedBooleanOffsetMm.z : offZ[idx % offZ.Length];
            bool usedAimedCursor = _hasAimedBooleanCursor;
            _hasAimedBooleanCursor = false;

            var spec = new StampedCadBodySpec
            {
                bodyName = $"Body_{idx + 2}_{CadPrimitiveShapeNames[CadPrimitiveShapeIndex].Split(' ')[0]}",
                primitiveShape = CadPrimitiveShapeIndex,
                booleanMode = CadBooleanModeIndex,
                patternMode = CadPatternModeIndex,
                featureOp = CadFeatureOpIndex,
                widthMm = System.Math.Max(10.0, System.Math.Min(600.0, ManualTestWidthMm * scale)),
                depthMm = System.Math.Max(10.0, System.Math.Min(600.0, CapturedDepthMm * scale)),
                heightMm = System.Math.Max(10.0, System.Math.Min(600.0, CapturedHeightMm * 0.65)),
                localOffsetXMm = targetOffX,
                localOffsetYMm = targetOffY,
                localOffsetZMm = targetOffZ,
                localRotXDeg = ModelTiltDeg.x,
                localRotYDeg = ModelTiltDeg.y,
                localRotZDeg = ModelTiltDeg.z
            };
            _stampedCadBodies.Add(spec);

            SpatialTactileAudio.Instance?.PlayCaliperLock();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = usedAimedCursor
                ? $"COMBINE BODY #{_stampedCadBodies.Count + 1} @ ({targetOffX:F0},{targetOffY:F0},{targetOffZ:F0})mm Tilt({ModelTiltDeg.x:F0}°,{ModelTiltDeg.y:F0}°,{ModelTiltDeg.z:F0}°) -> [{ActivePrimitiveShapeName}]"
                : $"COMBINE BODY #{_stampedCadBodies.Count + 1} -> [{ActivePrimitiveShapeName}] ({ActiveBooleanModeName} | Tilt {ModelTiltDeg.x:F0}°,{ModelTiltDeg.y:F0}°,{ModelTiltDeg.z:F0}°)";
            WriteDiagnosticState("StampNewCadBodyToAssembly");
        }

        [Preserve]
        public void CycleCadBooleanMode()
        {
            CadBooleanModeIndex = (CadBooleanModeIndex + 1) % CadBooleanModeNames.Length;
            SpatialTactileAudio.Instance?.PlayPokeClick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            StatusMessage = $"COMBINE MODE -> [{ActiveBooleanModeName}] (Touch 3D model to aim position, then poke [+ADD / CUT BODY])";
            WriteDiagnosticState("CycleCadBooleanMode");
        }

        [Preserve]
        public void CycleCadPatternArray()
        {
            CadPatternModeIndex = (CadPatternModeIndex + 1) % CadPatternModeNames.Length;
            SpatialTactileAudio.Instance?.PlaySynthChime();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"PATTERN -> [{ActivePatternModeName}] ({ActivePrimitiveShapeName})";
            WriteDiagnosticState("CycleCadPatternArray");
        }

        [Preserve]
        public void CycleModelTiltAngles()
        {
            _tiltPresetStepIndex = (_tiltPresetStepIndex + 1) % 7;
            switch (_tiltPresetStepIndex)
            {
                case 1:
                    ModelTiltDeg = new Vector3(15f, 0f, 0f);
                    break;
                case 2:
                    ModelTiltDeg = new Vector3(30f, 0f, 0f);
                    break;
                case 3:
                    ModelTiltDeg = new Vector3(45f, 30f, 0f);
                    break;
                case 4:
                    ModelTiltDeg = new Vector3(0f, 45f, 30f);
                    break;
                case 5:
                    ModelTiltDeg = new Vector3(-30f, 45f, -25f);
                    break;
                case 6:
                    ModelTiltDeg = new Vector3(90f, 0f, 0f);
                    break;
                default:
                    ModelTiltDeg = Vector3.zero;
                    ModelTranslationMm = Vector3.zero;
                    break;
            }
            SpatialTactileAudio.Instance?.PlayCaliperTick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            StatusMessage = $"ROTATE / TILT 3D -> Pitch X:{ModelTiltDeg.x:F0}° | Yaw Y:{ModelTiltDeg.y:F0}° | Roll Z:{ModelTiltDeg.z:F0}° (Or drag TILT X°/Y°/Z° handles!)";
            WriteDiagnosticState("CycleModelTiltAngles");
        }

        [Preserve]
        public void TranslateModelMm(Vector3 deltaMm)
        {
            ModelTranslationMm = new Vector3(
                Mathf.Clamp(ModelTranslationMm.x + deltaMm.x, -200f, 200f),
                Mathf.Clamp(ModelTranslationMm.y + deltaMm.y, -100f, 250f),
                Mathf.Clamp(ModelTranslationMm.z + deltaMm.z, -200f, 200f));
            SpatialTactileAudio.Instance?.PlayCaliperTick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            StatusMessage = $"MOVE 3D (X/Y/Z) -> Offset ({ModelTranslationMm.x:F0}, {ModelTranslationMm.y:F0}, {ModelTranslationMm.z:F0}) mm | Tilt ({ModelTiltDeg.x:F0}°, {ModelTiltDeg.y:F0}°, {ModelTiltDeg.z:F0}°)";
            WriteDiagnosticState("TranslateModelMm");
        }

        [Preserve]
        public void CycleDirectPushPullExtrude()
        {
            _pushPullStepIndex = (_pushPullStepIndex + 1) % 6;
            DirectPushPullActive = (_pushPullStepIndex % 2 == 1);
            switch (_pushPullStepIndex)
            {
                case 1:
                    CapturedHeightMm = System.Math.Min(600.0, CapturedHeightMm + 30.0);
                    break;
                case 2:
                    CapturedDepthMm = System.Math.Min(600.0, CapturedDepthMm + 35.0);
                    break;
                case 3:
                    ManualTestWidthMm = System.Math.Min(600.0, ManualTestWidthMm + 45.0);
                    CapturedDepthMm = System.Math.Min(600.0, CapturedDepthMm + 45.0);
                    CapturedHeightMm = System.Math.Min(600.0, CapturedHeightMm + 40.0);
                    break;
                case 4:
                    // Large-Scale 250x200x160mm Engineering Block Preset
                    ManualTestWidthMm = System.Math.Max(ManualTestWidthMm, 250.0);
                    CapturedDepthMm = 200.0;
                    CapturedHeightMm = 160.0;
                    break;
                case 5:
                    // Isometric Equal-Dimension Precision Lock
                    CapturedDepthMm = ManualTestWidthMm;
                    CapturedHeightMm = System.Math.Max(20.0, ManualTestWidthMm * 0.75);
                    break;
                default:
                    CapturedDepthMm = 55.0;
                    CapturedHeightMm = 42.0;
                    break;
            }
            SpatialTactileAudio.Instance?.PlayCaliperLock();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"EXTRUDE / PULL -> {ManualTestWidthMm:F0} x {CapturedDepthMm:F0} x {CapturedHeightMm:F0} mm (Max 600mm) | Vol={CadPartVolumeCm3:F1}cm³ ({CadPartMassGrams:F1}g)";
            WriteDiagnosticState("CycleDirectPushPullExtrude");
        }

        [Preserve]
        public void CycleCadFeatureOp()
        {
            CadFeatureOpIndex = (CadFeatureOpIndex + 1) % CadFeatureOpNames.Length;
            SpatialTactileAudio.Instance?.PlayPokeClick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"MODIFY: SHELL / CHAMFER -> [{CadFeatureOpNames[CadFeatureOpIndex]}] ({ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm | {CadPartMassGrams:F1}g)";
            WriteDiagnosticState("CycleCadFeatureOp");
        }

        [Preserve]
        public void CycleCadMaterial()
        {
            CadMaterialIndex = (CadMaterialIndex + 1) % CadMaterialNames.Length;
            SpatialTactileAudio.Instance?.PlayCaliperTick();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            if (SnappedModuleCount >= 1)
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"PHYSICAL MATERIAL -> {CadMaterialNames[CadMaterialIndex]} | Mass={CadPartMassGrams:F1}g (~{EstimatedPrintMinutes}m 3D print)";
            WriteDiagnosticState("CycleCadMaterial");
        }

        [Preserve]
        public void SnapAddModularJointAndLink()
        {
            SnappedModuleCount = (SnappedModuleCount + 1) % 4;
            if (SnappedModuleCount == 0)
            {
                _snappedModuleCustomSpecs.Clear();
            }
            else
            {
                // Capture the user's current sculpted 1:1 CAD solid dimensions, primitive shape, and mass (g)
                // so any snapped module carries the real custom CAD part into the kinematic chain & URDF!
                var customModule = new StampedCadBodySpec
                {
                    bodyName = $"Custom_{ActivePrimitiveShapeName.Split(' ')[0]}_{SnappedModuleCount}",
                    primitiveShape = CadPrimitiveShapeIndex,
                    booleanMode = CadBooleanModeIndex,
                    patternMode = CadPatternModeIndex,
                    featureOp = CadFeatureOpIndex,
                    widthMm = ManualTestWidthMm,
                    depthMm = CapturedDepthMm,
                    heightMm = CapturedHeightMm,
                    localOffsetXMm = CadPartMassGrams // store exact computed mass (g)
                };
                if (_snappedModuleCustomSpecs.Count >= SnappedModuleCount)
                    _snappedModuleCustomSpecs[SnappedModuleCount - 1] = customModule;
                else
                    _snappedModuleCustomSpecs.Add(customModule);
            }

            _spawnAnimTimer = 0.85f;
            SpatialTactileAudio.Instance?.PlayCaliperLock();
            SynthesizeAndSpawnSync(ManualTestWidthMm);

            string partTag = $"{ActivePrimitiveShapeName} ({ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm, {CadPartMassGrams:F0}g)";
            string[] snapDescriptions =
            {
                "Reset to Base 3-DoF Mechanism",
                $"Snapped +Prismatic Slider + {partTag} (4-DoF Screw S_pris)",
                $"Snapped +Revolute Pitch Wrist + {partTag} (5-DoF Screw S_rev)",
                $"Snapped +Revolute Roll Flange + {partTag} (6-DoF Full Assembly!)"
            };
            StatusMessage = $"MAGNETIC SNAP -> {snapDescriptions[SnappedModuleCount]}";
            WriteDiagnosticState("SnapAddModularJointAndLink");
        }

        [Preserve]
        public void CycleJointScrewAxis(int jointIndex = -1)
        {
            int n = (CurrentSpec?.joints != null) ? CurrentSpec.joints.Length : 3;
            if (n <= 0) return;

            if (jointIndex < 0 || jointIndex >= n)
            {
                _lastInspectedJointIdx = (_lastInspectedJointIdx + 1) % n;
                jointIndex = _lastInspectedJointIdx;
            }
            else
            {
                _lastInspectedJointIdx = jointIndex;
            }

            int idx = Mathf.Clamp(jointIndex, 0, _jointAxisOverrides.Length - 1);
            // 0 = Default, 1 = Revolute Pitch Z [0,0,1], 2 = Revolute Yaw Y [0,1,0], 3 = Revolute Roll X [1,0,0], 4 = Prismatic Slider X [1,0,0]
            _jointAxisOverrides[idx] = (_jointAxisOverrides[idx] + 1) % 5;
            int mode = _jointAxisOverrides[idx];

            string[] axisLabels =
            {
                "Default Screw S_i",
                "Revolute Pitch Z [0,0,1]",
                "Revolute Yaw Y [0,1,0]",
                "Revolute Roll X [1,0,0]",
                "Prismatic Linear Slider X [1,0,0]"
            };

            SpatialTactileAudio.Instance?.PlayCaliperLock();
            SynthesizeAndSpawnSync(ManualTestWidthMm);
            StatusMessage = $"SCREW AXIS PIN J{idx + 1} FLIPPED -> {axisLabels[mode]} (Live PoE SE(3) Recompiled)";
            WriteDiagnosticState("CycleJointScrewAxis");
        }

        [Preserve]
        public void ToggleHandInspectMode()
        {
            HandInspectActive = !HandInspectActive;
            if (_handInspectRoot != null)
                _handInspectRoot.SetActive(HandInspectActive);
            SpatialTactileAudio.Instance?.PlaySynthChime();
            StatusMessage = HandInspectActive
                ? $"1:1 MODEL ON RIGHT HAND [ON] -> Hold out Right Hand to inspect {ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm ({CadPartMassGrams:F1}g {CadMaterialNames[CadMaterialIndex]})"
                : "1:1 MODEL ON RIGHT HAND [OFF] -> Returned to Tabletop Studio";
            WriteDiagnosticState("ToggleHandInspectMode");
        }

        private MechanismSpec AppendSnappedModulesToSpec(MechanismSpec baseSpec)
        {
            if (baseSpec == null || baseSpec.joints == null || baseSpec.links == null)
                return baseSpec;

            var links = new List<LinkSpec>(baseSpec.links);
            var joints = new List<JointSpec>(baseSpec.joints);
            double curEeX = (baseSpec.endEffectorHomePosition != null && baseSpec.endEffectorHomePosition.Length == 3) ? baseSpec.endEffectorHomePosition[0] : 0.22;
            double curEeY = (baseSpec.endEffectorHomePosition != null && baseSpec.endEffectorHomePosition.Length == 3) ? baseSpec.endEffectorHomePosition[1] : 0.06;
            double curEeZ = (baseSpec.endEffectorHomePosition != null && baseSpec.endEffectorHomePosition.Length == 3) ? baseSpec.endEffectorHomePosition[2] : 0.0;

            double cadLenDefault = System.Math.Max(0.028, System.Math.Min(0.088, CapturedDepthMm * 0.001));
            double cadWidDefault = System.Math.Max(0.020, System.Math.Min(0.095, ManualTestWidthMm * 0.001));
            double cadHgtDefault = System.Math.Max(0.016, System.Math.Min(0.070, CapturedHeightMm * 0.001));
            double cadMassKgDefault = System.Math.Max(0.03, CadPartMassGrams * 0.001);

            // Module 1 (4th DoF): Prismatic Linear Actuator Slider along +X (scaled by user's sculpted CAD part!)
            if (SnappedModuleCount >= 1)
            {
                StampedCadBodySpec mSpec = (_snappedModuleCustomSpecs.Count >= 1) ? _snappedModuleCustomSpecs[0] : null;
                double sliderLen = mSpec != null ? System.Math.Max(0.032, System.Math.Min(0.085, mSpec.depthMm * 0.001)) : System.Math.Max(0.042, cadLenDefault * 0.85);
                double sliderWid = mSpec != null ? System.Math.Max(0.016, System.Math.Min(0.055, mSpec.widthMm * 0.00045)) : 0.020;
                double sliderHgt = mSpec != null ? System.Math.Max(0.016, System.Math.Min(0.050, mSpec.heightMm * 0.00055)) : 0.020;
                double sliderMass = mSpec != null ? System.Math.Max(0.04, mSpec.localOffsetXMm * 0.001) : cadMassKgDefault;

                string pLink = links[links.Count - 1].name;
                string cLink = "snapped_prismatic_slider_link";
                joints.Add(new JointSpec
                {
                    name = "joint_4_prismatic_slider",
                    jointType = "prismatic",
                    parentLink = pLink,
                    childLink = cLink,
                    originX = curEeX,
                    originY = curEeY,
                    originZ = curEeZ,
                    axisX = 1.0,
                    axisY = 0.0,
                    axisZ = 0.0,
                    minLimitRad = -0.045,
                    maxLimitRad = 0.065
                });
                links.Add(new LinkSpec
                {
                    name = cLink,
                    lengthM = sliderLen,
                    widthM = sliderWid,
                    heightM = sliderHgt,
                    massKg = sliderMass,
                    centerX = curEeX + sliderLen * 0.5,
                    centerY = curEeY,
                    centerZ = curEeZ
                });
                curEeX += sliderLen;
            }

            // Module 2 (5th DoF): Revolute Pitch Wrist around Z (scaled by user's sculpted CAD part!)
            if (SnappedModuleCount >= 2)
            {
                StampedCadBodySpec mSpec = (_snappedModuleCustomSpecs.Count >= 2) ? _snappedModuleCustomSpecs[1] : null;
                double wristLen = mSpec != null ? System.Math.Max(0.030, System.Math.Min(0.080, mSpec.depthMm * 0.0009)) : System.Math.Max(0.038, cadLenDefault * 0.80);
                double wristWid = mSpec != null ? System.Math.Max(0.016, System.Math.Min(0.055, mSpec.widthMm * 0.00045)) : 0.018;
                double wristHgt = mSpec != null ? System.Math.Max(0.016, System.Math.Min(0.050, mSpec.heightMm * 0.00055)) : 0.018;
                double wristMass = mSpec != null ? System.Math.Max(0.04, mSpec.localOffsetXMm * 0.001) : cadMassKgDefault;

                string pLink = links[links.Count - 1].name;
                string cLink = "snapped_revolute_wrist_link";
                joints.Add(new JointSpec
                {
                    name = "joint_5_wrist_pitch",
                    jointType = "revolute",
                    parentLink = pLink,
                    childLink = cLink,
                    originX = curEeX,
                    originY = curEeY,
                    originZ = curEeZ,
                    axisX = 0.0,
                    axisY = 0.0,
                    axisZ = 1.0,
                    minLimitRad = -2.0,
                    maxLimitRad = 2.0
                });
                links.Add(new LinkSpec
                {
                    name = cLink,
                    lengthM = wristLen,
                    widthM = wristWid,
                    heightM = wristHgt,
                    massKg = wristMass,
                    centerX = curEeX + wristLen * 0.5,
                    centerY = curEeY,
                    centerZ = curEeZ
                });
                curEeX += wristLen;
            }

            // Module 3 (6th DoF): Revolute Roll Flange around X carrying the Captured 1:1 Real-World CAD Part!
            if (SnappedModuleCount >= 3)
            {
                StampedCadBodySpec mSpec = (_snappedModuleCustomSpecs.Count >= 3) ? _snappedModuleCustomSpecs[2] : null;
                double cadLen = mSpec != null ? System.Math.Max(0.024, System.Math.Min(0.095, mSpec.depthMm * 0.001)) : cadLenDefault;
                double cadWid = mSpec != null ? System.Math.Max(0.024, System.Math.Min(0.110, mSpec.widthMm * 0.001)) : cadWidDefault;
                double cadHgt = mSpec != null ? System.Math.Max(0.016, System.Math.Min(0.075, mSpec.heightMm * 0.001)) : cadHgtDefault;
                double cadMass = mSpec != null ? System.Math.Max(0.02, mSpec.localOffsetXMm * 0.001) : cadMassKgDefault;

                string pLink = links[links.Count - 1].name;
                string cLink = "snapped_captured_cad_part";
                joints.Add(new JointSpec
                {
                    name = "joint_6_tool_roll",
                    jointType = "revolute",
                    parentLink = pLink,
                    childLink = cLink,
                    originX = curEeX,
                    originY = curEeY,
                    originZ = curEeZ,
                    axisX = 1.0,
                    axisY = 0.0,
                    axisZ = 0.0,
                    minLimitRad = -3.14,
                    maxLimitRad = 3.14
                });
                links.Add(new LinkSpec
                {
                    name = cLink,
                    lengthM = cadLen,
                    widthM = cadWid,
                    heightM = cadHgt,
                    massKg = cadMass,
                    centerX = curEeX + cadLen * 0.5,
                    centerY = curEeY,
                    centerZ = curEeZ
                });
                curEeX += cadLen;
            }

            // Apply per-joint Screw Axis overrides (S_i Inspector: Revolute Z / Y / X or Prismatic Slider X)
            for (int i = 0; i < joints.Count && i < _jointAxisOverrides.Length; i++)
            {
                int ov = _jointAxisOverrides[i];
                if (ov <= 0) continue;
                JointSpec j = joints[i];
                switch (ov)
                {
                    case 1: // Revolute Pitch Z [0,0,1]
                        j.jointType = "revolute";
                        j.axisX = 0.0; j.axisY = 0.0; j.axisZ = 1.0;
                        j.minLimitRad = -2.2; j.maxLimitRad = 2.2;
                        break;
                    case 2: // Revolute Yaw Y [0,1,0]
                        j.jointType = "revolute";
                        j.axisX = 0.0; j.axisY = 1.0; j.axisZ = 0.0;
                        j.minLimitRad = -2.6; j.maxLimitRad = 2.6;
                        break;
                    case 3: // Revolute Roll X [1,0,0]
                        j.jointType = "revolute";
                        j.axisX = 1.0; j.axisY = 0.0; j.axisZ = 0.0;
                        j.minLimitRad = -3.14; j.maxLimitRad = 3.14;
                        break;
                    case 4: // Prismatic Linear Slider X [1,0,0]
                        j.jointType = "prismatic";
                        j.axisX = 1.0; j.axisY = 0.0; j.axisZ = 0.0;
                        j.minLimitRad = -0.045; j.maxLimitRad = 0.065;
                        break;
                }
            }

            return new MechanismSpec
            {
                robotName = $"{baseSpec.robotName}_{joints.Count}dof",
                targetCaliperWidthMm = baseSpec.targetCaliperWidthMm,
                links = links.ToArray(),
                joints = joints.ToArray(),
                endEffectorHomePosition = new[] { curEeX, curEeY, curEeZ }
            };
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

            if (PureCadStudioMode)
            {
                dz.SetActive(false);
                _dropZoneLabel.gameObject.SetActive(false);
            }

            CreateReachEnvelopeRing();
            CreateObjectiveBeacon();
            CreateTrajectoryTrail();
            CreatePayloadHousingAndSweptEnvelope();
            CreateHandInspectPreview();
            CreateExportedModelGallery();
            CreateDirectManipulationGizmos();
            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
        }

        private void CreatePayloadHousingAndSweptEnvelope()
        {
            // 1. Spawn the AEGIS Drone Payload Bay Mounting Housing Block (72 open-frame lattice voxels for real-time Boolean Ghost Carving)
            _housingVoxelTransforms = new Transform[TotalHousingVoxels];
            _housingVoxelRenderers = new Renderer[TotalHousingVoxels];
            _housingVoxelLocalCenters = new Vector3[TotalHousingVoxels];
            _housingVoxelClashed = new bool[TotalHousingVoxels];
            _housingVoxelCarved = new bool[TotalHousingVoxels];

            GameObject housingRoot = new GameObject("AEGIS_PayloadMountingHousing_CSG");
            housingRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            housingRoot.transform.localPosition = Vector3.zero;
            housingRoot.transform.localRotation = Quaternion.identity;

            Vector3 blockCenter = new Vector3(0.115f, 0.052f, 0.0f);
            Vector3 voxelStep = new Vector3(0.019f, 0.019f, 0.021f);

            int idx = 0;
            for (int ix = 0; ix < HousingGridX; ix++)
            {
                for (int iy = 0; iy < HousingGridY; iy++)
                {
                    for (int iz = 0; iz < HousingGridZ; iz++)
                    {
                        float ox = (ix - (HousingGridX - 1) * 0.5f) * voxelStep.x;
                        float oy = (iy - (HousingGridY - 1) * 0.5f) * voxelStep.y;
                        float oz = (iz - (HousingGridZ - 1) * 0.5f) * voxelStep.z;
                        Vector3 localPos = blockCenter + new Vector3(ox, oy, oz);

                        GameObject vox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        vox.name = $"HousingVoxel_{idx}";
                        Destroy(vox.GetComponent<Collider>());
                        vox.transform.SetParent(housingRoot.transform, false);
                        vox.transform.localPosition = localPos;
                        vox.transform.localScale = VoxelLatticeScale;

                        Renderer r = vox.GetComponent<Renderer>();
                        SetColorSafe(r, new Color(0.20f, 0.32f, 0.48f));

                        _housingVoxelTransforms[idx] = vox.transform;
                        _housingVoxelRenderers[idx] = r;
                        _housingVoxelLocalCenters[idx] = localPos;
                        idx++;
                    }
                }
            }

            _housingLabel = WorldTextFactory.Create(
                "AEGIS_Housing_Label", DeskWorkspaceOrigin,
                blockCenter + new Vector3(0f, 0.048f, 0f), Quaternion.Euler(25f, 0f, 0f),
                0.00135f, 30, new Color(0.95f, 0.55f, 0.90f),
                "AEGIS PAYLOAD HOUSING\n[Sweep W_i -> Thumb-Tap GHOST CARVE]");

            // 2. Pre-allocate 24 Swept-Volume Ghost Capsules (W_i)
            _sweptGhostCapsules = new Transform[MaxSweptGhostCapsules];
            _sweptGhostRenderers = new Renderer[MaxSweptGhostCapsules];
            _sweptCapsuleCenters = new Vector3[MaxSweptGhostCapsules];
            _sweptSegmentStarts = new Vector3[MaxSweptGhostCapsules];
            _sweptSegmentEnds = new Vector3[MaxSweptGhostCapsules];
            _sweptCapsuleRadii = new float[MaxSweptGhostCapsules];

            GameObject ghostRoot = new GameObject("SweptVolume_GhostEnvelope_Wi");
            ghostRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            ghostRoot.transform.localPosition = Vector3.zero;
            ghostRoot.transform.localRotation = Quaternion.identity;

            for (int i = 0; i < MaxSweptGhostCapsules; i++)
            {
                GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cap.name = $"SweptGhostCapsule_{i}";
                Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(ghostRoot.transform, false);
                cap.transform.localScale = Vector3.one * 0.026f;
                cap.SetActive(false);
                _sweptGhostCapsules[i] = cap.transform;
                _sweptGhostRenderers[i] = cap.GetComponent<Renderer>();
                SetColorSafe(_sweptGhostRenderers[i], new Color(0.85f, 0.25f, 0.95f));
            }

            if (PureCadStudioMode)
            {
                housingRoot.SetActive(false);
                _housingLabel.gameObject.SetActive(false);
                ghostRoot.SetActive(false);
            }
        }

        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-8f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
            return Vector3.Distance(p, a + t * ab);
        }

        private void UpdateSweptVolumeAndCbf()
        {
            if (PureCadStudioMode) return;
            if (_theta == null || _theta.Length < 3 || _eeMarker == null || DeskWorkspaceOrigin == null) return;

            // Sample swept-volume W_i whenever joint configuration changes by >= 5 degrees (0.087 rad)
            double deltaSq = 0.0;
            for (int i = 0; i < 3; i++)
            {
                double d = _theta[i] - _lastSampledTheta[i];
                deltaSq += d * d;
            }

            Vector3 eeLocal = DeskWorkspaceOrigin.InverseTransformPoint(_eeMarker.position);
            Vector3 shoulderLocal = (_jointPivots != null && _jointPivots.Length > 0)
                ? _jointPivots[0].localPosition
                : new Vector3(0f, 0.05f, 0f);
            Vector3 elbowLocal = (_jointPivots != null && _jointPivots.Length > 0)
                ? _jointPivots[_jointPivots.Length - 1].localPosition
                : new Vector3(0.08f, 0.06f, 0f);
            Vector3 midForearm = (elbowLocal + eeLocal) * 0.5f;

            if (RecordEnvelopeActive && deltaSq >= (0.087 * 0.087))
            {
                for (int i = 0; i < 3; i++) _lastSampledTheta[i] = _theta[i];

                int slot = _sweptWriteIdx % MaxSweptGhostCapsules;
                _sweptWriteIdx++;
                SweptSampleCount = Mathf.Min(SweptSampleCount + 1, 999);

                _sweptCapsuleCenters[slot] = midForearm;
                _sweptSegmentStarts[slot] = elbowLocal;
                _sweptSegmentEnds[slot] = eeLocal;
                _sweptCapsuleRadii[slot] = 0.018f;
                if (_sweptGhostCapsules != null && _sweptGhostCapsules[slot] != null)
                {
                    _sweptGhostCapsules[slot].gameObject.SetActive(true);
                    _sweptGhostCapsules[slot].localPosition = midForearm;
                    Vector3 segDir = eeLocal - elbowLocal;
                    if (segDir.sqrMagnitude > 0.0001f)
                        _sweptGhostCapsules[slot].localRotation = Quaternion.LookRotation(segDir.normalized, Vector3.up);
                    _sweptGhostCapsules[slot].localScale = new Vector3(0.020f, 0.020f, Mathf.Clamp(segDir.magnitude * 0.65f, 0.024f, 0.065f));
                    SetColorSafe(_sweptGhostRenderers[slot], IsHousingCarved
                        ? new Color(0.15f, 0.95f, 0.72f)
                        : new Color(0.90f, 0.25f, 0.85f));
                }
            }

            RecomputeControlBarrierMarginHx(shoulderLocal, elbowLocal, eeLocal);
        }

        private void RecomputeControlBarrierMarginHx(Vector3? shoulderLocalOpt = null, Vector3? elbowLocalOpt = null, Vector3? eeLocalOpt = null)
        {
            if (PureCadStudioMode)
            {
                AgentRecommendation = $"FUSION 360 TIP: Drag +X / +Y / +Z handles to Extrude | Touch model + poke [+ADD / CUT BODY] ({ActiveBooleanModeName})";
                return;
            }
            if (_housingVoxelTransforms == null) return;

            Vector3 shoulderLocal = shoulderLocalOpt ?? ((_jointPivots != null && _jointPivots.Length > 0) ? _jointPivots[0].localPosition : new Vector3(0f, 0.05f, 0f));
            Vector3 elbowLocal = elbowLocalOpt ?? ((_jointPivots != null && _jointPivots.Length > 0) ? _jointPivots[_jointPivots.Length - 1].localPosition : new Vector3(0.08f, 0.06f, 0f));
            Vector3 eeLocal = eeLocalOpt ?? (_eeMarker != null && DeskWorkspaceOrigin != null ? DeskWorkspaceOrigin.InverseTransformPoint(_eeMarker.position) : new Vector3(0.18f, 0.06f, 0f));

            double rLinkM = (CurrentSpec?.links != null && CurrentSpec.links.Length > 1) ? CurrentSpec.links[1].widthM * 0.5 : 0.013;
            float rSafety = (float)rLinkM + 0.010f; // r_link + 10mm safety margin
            float minHxMeters = 0.095f;
            int activeSamples = Mathf.Min(SweptSampleCount, MaxSweptGhostCapsules);
            int clashCount = 0;

            for (int v = 0; v < TotalHousingVoxels; v++)
            {
                if (_housingVoxelCarved[v]) continue;

                Vector3 vPos = _housingVoxelLocalCenters[v];
                float dUpper = DistancePointToSegment(vPos, shoulderLocal, elbowLocal) - rSafety;
                float dFore = DistancePointToSegment(vPos, elbowLocal, eeLocal) - rSafety;
                float dMin = Mathf.Min(dUpper, dFore);

                for (int s = 0; s < activeSamples; s++)
                {
                    float dSwept = DistancePointToSegment(vPos, _sweptSegmentStarts[s], _sweptSegmentEnds[s]) - rSafety;
                    if (dSwept < dMin) dMin = dSwept;
                }

                if (dMin < minHxMeters)
                    minHxMeters = dMin;

                if (dMin < 0f && !_housingVoxelClashed[v])
                {
                    _housingVoxelClashed[v] = true;
                    if (_housingVoxelTransforms[v] != null)
                        _housingVoxelTransforms[v].localScale = VoxelClashScale;
                    if (_housingVoxelRenderers[v] != null)
                        SetColorSafe(_housingVoxelRenderers[v], new Color(1.0f, 0.18f, 0.50f));
                }

                if (_housingVoxelClashed[v])
                    clashCount++;
            }

            ClashedVoxelCount = clashCount;
            CurrentBarrierHxMm = minHxMeters * 1000.0;

            if (!IsHousingCarved && _housingLabel != null)
            {
                _housingLabel.text = clashCount > 0
                    ? $"AEGIS PAYLOAD HOUSING\n[■ {clashCount} VOX CLASH (h={CurrentBarrierHxMm:F1}mm) -> GHOST CARVE]"
                    : "AEGIS PAYLOAD HOUSING\n[Sweep W_i -> Thumb-Tap GHOST CARVE]";
            }

            // Update Autonomous AEGIS Physical AI Co-Pilot Recommendation
            if (!IsHousingCarved && clashCount > 0)
            {
                AgentRecommendation = $"AGENTIC CO-PILOT: ⚠ {clashCount} housing voxels clash (h={CurrentBarrierHxMm:F1}mm < 0) -> Thumb-Tap or Poke [GHOST CARVE]";
            }
            else if (!IsHousingCarved && SweptSampleCount < 4)
            {
                AgentRecommendation = $"AGENTIC CO-PILOT: Sweep arm through bay to map W_i envelope ({SweptSampleCount}/4 capsules)";
            }
            else if (CurrentManipulability < ReferenceManipulability * 0.42)
            {
                AgentRecommendation = "AGENTIC CO-PILOT: ⚠ Near singularity [▼ LOW μ] -> Retract elbow or Thumb-Swipe Fwd to switch topology";
            }
            else if (_isHoldingWorkpiece)
            {
                AgentRecommendation = $"AGENTIC CO-PILOT: Payload grasped ({ManualTestWidthMm:F0}mm) -> Guide along cyan arc to Drop Zone";
            }
            else
            {
                AgentRecommendation = $"AGENTIC CO-PILOT: ✓ CBF Safe (h=+{CurrentBarrierHxMm:F1}mm >= 0) | [▲ OPTIMAL μ] -> Ready for Pick-and-Place or URDF Export";
            }
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
            if (PureCadStudioMode) beacon.SetActive(false);
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
            if (PureCadStudioMode) trailRoot.SetActive(false);
        }

        private void UpdateTrajectoryTrail(Color currentHealthColor)
        {
            if (PureCadStudioMode) return;
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
            if (PureCadStudioMode) ringRoot.SetActive(false);
            UpdateReachEnvelopeAndStationPositions(0.25f);
        }

        private void UpdateReachEnvelopeAndStationPositions(float reachMeters)
        {
            _lastReachMeters = Mathf.Clamp(reachMeters, 0.11f, 0.34f);
            float clampedReach = _lastReachMeters;

            if (PureCadStudioMode)
            {
                // Keep the 3D CAD Model centered right in the middle of the desk workbench!
                _workpieceHomeLocalPos = new Vector3(0.05f, 0.008f, 0.01f);
                if (_sampleWorkpiece != null)
                {
                    float hM = Mathf.Clamp((float)(CapturedHeightMm * 0.001), 0.010f, 0.600f);
                    Vector3 transOffsetM = ModelTranslationMm * 0.001f;
                    _sampleWorkpiece.localPosition = new Vector3(
                        _workpieceHomeLocalPos.x + transOffsetM.x,
                        hM * 0.5f + transOffsetM.y,
                        _workpieceHomeLocalPos.z + transOffsetM.z);
                    _sampleWorkpiece.localRotation = Quaternion.Euler(ModelTiltDeg);
                    if (_workpieceLabel != null)
                        _workpieceLabel.transform.localPosition = _sampleWorkpiece.localPosition + new Vector3(0f, hM * 0.5f + 0.028f, 0f);
                }
                return;
            }

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
                _sampleWorkpiece.localPosition = _workpieceHomeLocalPos + ModelTranslationMm * 0.001f;
                _sampleWorkpiece.localRotation = Quaternion.Euler(ModelTiltDeg);
                if (_workpieceLabel != null)
                    _workpieceLabel.transform.localPosition = _sampleWorkpiece.localPosition + new Vector3(0f, 0.024f, 0f);
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

        private void CreateHandInspectPreview()
        {
            _handInspectRoot = new GameObject("HandInspect_1to1_PrintPreview");
            _handInspectRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            _handInspectRoot.transform.localPosition = new Vector3(0.16f, 0.145f, -0.06f);
            _handInspectRoot.transform.localRotation = Quaternion.identity;

            _handInspectLabel = WorldTextFactory.Create(
                "HandInspect_SpecLabel", _handInspectRoot.transform,
                new Vector3(0f, 0.052f, 0f), Quaternion.identity,
                0.00145f, 32, new Color(0.30f, 1.0f, 0.75f),
                "1:1 MODEL PREVIEW ON RIGHT HAND\nHold out Right Hand to inspect before 3D printing");

            _handInspectRoot.SetActive(HandInspectActive);
        }

        private void CreateExportedModelGallery()
        {
            _exportedGalleryRoot = new GameObject("ExportedModel_3D_GalleryPedestal");
            _exportedGalleryRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            _exportedGalleryRoot.transform.localPosition = new Vector3(0.24f, 0.001f, 0.02f);
            _exportedGalleryRoot.transform.localRotation = Quaternion.identity;

            GameObject pedestalPad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestalPad.name = "ExportedGallery_TurntablePad";
            Destroy(pedestalPad.GetComponent<Collider>());
            pedestalPad.transform.SetParent(_exportedGalleryRoot.transform, false);
            pedestalPad.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            pedestalPad.transform.localScale = new Vector3(0.115f, 0.0025f, 0.115f);
            SetColorSafe(pedestalPad.GetComponent<Renderer>(), new Color(0.10f, 0.55f, 0.36f));

            GameObject modelHolder = new GameObject("ExportedGallery_ModelContainer");
            modelHolder.transform.SetParent(_exportedGalleryRoot.transform, false);
            modelHolder.transform.localPosition = new Vector3(0f, 0.032f, 0f);
            modelHolder.transform.localRotation = Quaternion.identity;
            _exportedGalleryModelContainer = modelHolder.transform;

            _exportedGalleryLabel = WorldTextFactory.Create(
                "ExportedGallery_InfoLabel", _exportedGalleryRoot.transform,
                new Vector3(0f, 0.105f, 0f), Quaternion.Euler(20f, -15f, 0f),
                0.00130f, 28, new Color(0.35f, 1.0f, 0.68f),
                "EXPORTED 3D MODEL GALLERY\nPoke [EXPORT .STL / .OBJ] to save & preview");

            _exportedGalleryRoot.SetActive(false);
        }

        private void RefreshExportedModelGallery(string stlPath, string objPath)
        {
            if (_exportedGalleryRoot == null || _exportedGalleryModelContainer == null) return;
            _exportedGalleryRoot.SetActive(true);

            float rawW = Mathf.Clamp((float)(ManualTestWidthMm * 0.001), 0.010f, 0.600f);
            float rawD = Mathf.Clamp((float)(CapturedDepthMm * 0.001), 0.010f, 0.600f);
            float rawH = Mathf.Clamp((float)(CapturedHeightMm * 0.001), 0.010f, 0.600f);
            // Fit large 600mm models neatly onto the 12cm gallery turntable while keeping proportions 100% exact
            float maxDim = Mathf.Max(rawW, Mathf.Max(rawD, rawH));
            float galleryFitScale = (maxDim > 0.13f) ? (0.13f / maxDim) : 1.0f;

            float gw = rawW * galleryFitScale;
            float gd = rawD * galleryFitScale;
            float gh = rawH * galleryFitScale;

            _exportedGalleryModelContainer.localPosition = new Vector3(0f, gh * 0.5f + 0.008f, 0f);
            _exportedGalleryModelContainer.localRotation = Quaternion.Euler(ModelTiltDeg);

            RebuildCadFeatureSubParts(
                _exportedGalleryModelContainer, _exportedGallerySubParts,
                gw, gd, gh, new Color(0.22f, 0.95f, 0.62f), galleryFitScale);

            if (_exportedGalleryLabel != null)
            {
                _exportedGalleryLabel.transform.localPosition = new Vector3(0f, gh + 0.036f, 0f);
                string stlName = string.IsNullOrEmpty(stlPath) ? "kinetic_cad.stl" : Path.GetFileName(stlPath);
                string objName = string.IsNullOrEmpty(objPath) ? "kinetic_cad.obj" : Path.GetFileName(objPath);
                string folderHint = Application.isEditor
                    ? "PC Folder: E:\\Unity\\KineticCAD_XR\\Exports\\"
                    : "Quest 3: /sdcard/Android/data/com.DefaultCompany.KineticCADXR/files/";
                _exportedGalleryLabel.text =
                    $"✓ SAVED 3D MESH #{CareerUrdfExports}: {stlName} + {objName}\n" +
                    $"{ActivePrimitiveShapeName} | {ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm | Tilt({ModelTiltDeg.x:F0}°,{ModelTiltDeg.y:F0}°,{ModelTiltDeg.z:F0}°)\n" +
                    $"{folderHint}";
            }
        }

        private void CreateDirectManipulationGizmos()
        {
            // 1. In-Place Passthrough Caliper Bounding Box Twin (12 glowing wireframe edges at the real-world object midpoint)
            _inPlaceTwinRoot = new GameObject("InPlace_Passthrough_TwinBox");
            _inPlaceTwinRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            _inPlaceTwinRoot.transform.localPosition = _inPlaceTwinLocalCenter;
            _inPlaceTwinRoot.transform.localRotation = Quaternion.identity;

            Color twinEdgeCol = new Color(0.10f, 0.95f, 1.0f);
            for (int i = 0; i < 12; i++)
            {
                GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.name = $"InPlaceTwinEdge_{i}";
                Destroy(edge.GetComponent<Collider>());
                edge.transform.SetParent(_inPlaceTwinRoot.transform, false);
                Renderer r = edge.GetComponent<Renderer>();
                SetColorSafe(r, twinEdgeCol);
                _inPlaceTwinEdges[i] = edge.transform;
                _inPlaceTwinEdgeRenderers[i] = r;
            }

            _inPlaceTwinLabel = WorldTextFactory.Create(
                "InPlaceTwin_Label", _inPlaceTwinRoot.transform,
                new Vector3(0f, 0.042f, 0f), Quaternion.Euler(30f, 0f, 0f),
                0.0013f, 28, new Color(0.30f, 1.0f, 0.95f),
                "1:1 MEASURED OBJECT TWIN\nPoint Both Index Tips on Real Object to Capture");

            if (PureCadStudioMode)
                _inPlaceTwinRoot.SetActive(false);

            // 2. Direct 3D Extrude / Pull Gizmo Handles (+X Width, +Y Height, +Z Depth) on the CAD Solid
            string[] handleNames = { "FacePullHandle_X_Width", "FacePullHandle_Y_Height", "FacePullHandle_Z_Depth" };
            string[] handleTags = { "+X WIDTH", "+Y HEIGHT", "+Z DEPTH" };
            for (int a = 0; a < 3; a++)
            {
                GameObject hGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hGo.name = handleNames[a];
                // Keep Collider enabled so Desktop Mouse Raycast and VR Fingertip distance checks both work!
                hGo.transform.SetParent(DeskWorkspaceOrigin, false);
                hGo.transform.localScale = Vector3.one * 0.018f;
                Renderer hr = hGo.GetComponent<Renderer>();
                SetColorSafe(hr, _facePullBaseColors[a]);
                _facePullHandles[a] = hGo.transform;
                _facePullRenderers[a] = hr;

                WorldTextFactory.Create(
                    $"FacePullLabel_{a}", hGo.transform,
                    new Vector3(0f, 0.82f, 0f), Quaternion.Euler(20f, 0f, 0f),
                    0.068f, 28, _facePullBaseColors[a], handleTags[a]);
            }

            // 2B. Direct 3D Angular Rotation / Tilt Gizmo Handles (TILT X° Pitch, TILT Y° Yaw, TILT Z° Roll)
            string[] tiltNames = { "TiltHandle_X_Pitch", "TiltHandle_Y_Yaw", "TiltHandle_Z_Roll" };
            string[] tiltTags = { "TILT X° (Pitch)", "TILT Y° (Yaw)", "TILT Z° (Roll)" };
            for (int t = 0; t < 3; t++)
            {
                GameObject tGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tGo.name = tiltNames[t];
                // Keep Collider enabled so Desktop Mouse Raycast and VR Fingertip drag both work!
                tGo.transform.SetParent(DeskWorkspaceOrigin, false);
                tGo.transform.localScale = new Vector3(0.018f, 0.005f, 0.018f);
                Renderer tr = tGo.GetComponent<Renderer>();
                SetColorSafe(tr, _tiltHandleBaseColors[t]);
                _tiltHandles[t] = tGo.transform;
                _tiltHandleRenderers[t] = tr;

                WorldTextFactory.Create(
                    $"TiltHandleLabel_{t}", tGo.transform,
                    new Vector3(0f, 2.2f, 0f), Quaternion.Euler(20f, 0f, 0f),
                    0.068f, 26, _tiltHandleBaseColors[t], tiltTags[t]);
            }

            // 3. Interactive 3D Combine Positioning Cursor (snaps to 2mm CAD grid inside model before adding/cutting)
            _liveBooleanCursorObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _liveBooleanCursorObj.name = "LiveBoolean_3D_Cursor";
            Destroy(_liveBooleanCursorObj.GetComponent<Collider>());
            _liveBooleanCursorObj.transform.SetParent(DeskWorkspaceOrigin, false);
            _liveBooleanCursorObj.transform.localScale = new Vector3(0.018f, 0.015f, 0.018f);
            _liveBooleanCursorRenderer = _liveBooleanCursorObj.GetComponent<Renderer>();
            SetColorSafe(_liveBooleanCursorRenderer, new Color(1.0f, 0.18f, 0.48f));
            _liveBooleanCursorObj.SetActive(false);

            // 4. Drag-Carry Shape Ghost & 6-Segment Glowing Snap Line to the 3D CAD Model
            _dragCarryGhostObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _dragCarryGhostObj.name = "DragCarry_ModuleGhost";
            Destroy(_dragCarryGhostObj.GetComponent<Collider>());
            _dragCarryGhostObj.transform.SetParent(DeskWorkspaceOrigin, false);
            _dragCarryGhostObj.transform.localScale = new Vector3(0.024f, 0.018f, 0.024f);
            _dragCarryGhostRenderer = _dragCarryGhostObj.GetComponent<Renderer>();
            SetColorSafe(_dragCarryGhostRenderer, new Color(1.0f, 0.84f, 0.20f));
            _dragCarryGhostObj.SetActive(false);

            for (int i = 0; i < _magneticTetherSegments.Length; i++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                seg.name = $"MagneticSnapTether_{i}";
                Destroy(seg.GetComponent<Collider>());
                seg.transform.SetParent(DeskWorkspaceOrigin, false);
                seg.transform.localScale = Vector3.one * 0.0075f;
                Renderer sr = seg.GetComponent<Renderer>();
                SetColorSafe(sr, new Color(1.0f, 0.85f, 0.18f));
                seg.SetActive(false);
                _magneticTetherSegments[i] = seg.transform;
                _magneticTetherRenderers[i] = sr;
            }
        }

        private void UpdateInPlaceTwinAndFaceHandles(float wMeters, float dMeters, float hMeters)
        {
            // Position the 3 Direct Extrude Handles (+X Width, +Y Height, +Z Depth) and 3 Direct Tilt Handles on _sampleWorkpiece
            if (_sampleWorkpiece != null && _facePullHandles[0] != null)
            {
                bool showHandles = !_isHoldingWorkpiece;
                Vector3 wpCenter = _sampleWorkpiece.localPosition;
                Vector3[] faceOffsets =
                {
                    new Vector3(wMeters * 0.5f + 0.016f, 0f, 0f), // +X Width face
                    new Vector3(0f, hMeters * 0.5f + 0.016f, 0f), // +Y Top/Height face
                    new Vector3(0f, 0f, dMeters * 0.5f + 0.016f)  // +Z Depth face
                };
                for (int a = 0; a < 3; a++)
                {
                    if (_facePullHandles[a] == null) continue;
                    _facePullHandles[a].gameObject.SetActive(showHandles);
                    _facePullHandles[a].localPosition = wpCenter + faceOffsets[a];
                    bool isPulled = (_activeFacePullAxis == a);
                    _facePullHandles[a].localScale = Vector3.one * (isPulled ? 0.023f : 0.018f);
                    SetColorSafe(_facePullRenderers[a], isPulled ? Color.white : _facePullBaseColors[a]);
                }

                // Position the 3 Angular Rotation / Tilt Handles around the upper corners of the 3D CAD Model
                Vector3[] tiltOffsets =
                {
                    new Vector3(-wMeters * 0.5f - 0.018f, hMeters * 0.35f + 0.012f, 0f),                // TILT X° (Pitch)
                    new Vector3(-wMeters * 0.35f - 0.010f, hMeters * 0.5f + 0.020f, -dMeters * 0.35f), // TILT Y° (Yaw)
                    new Vector3( wMeters * 0.35f + 0.010f, hMeters * 0.5f + 0.020f, -dMeters * 0.35f)  // TILT Z° (Roll)
                };
                for (int t = 0; t < 3; t++)
                {
                    if (_tiltHandles[t] == null) continue;
                    _tiltHandles[t].gameObject.SetActive(showHandles);
                    _tiltHandles[t].localPosition = wpCenter + tiltOffsets[t];
                    bool isTilted = (_activeTiltAxis == t);
                    _tiltHandles[t].localScale = isTilted
                        ? new Vector3(0.024f, 0.007f, 0.024f)
                        : new Vector3(0.018f, 0.005f, 0.018f);
                    SetColorSafe(_tiltHandleRenderers[t], isTilted ? Color.white : _tiltHandleBaseColors[t]);
                }
            }

            // Update the 12 wireframe edge rails of the In-Place Passthrough Bounding Twin
            if (_inPlaceTwinRoot != null && _inPlaceTwinEdges[0] != null)
            {
                if (PureCadStudioMode)
                    _inPlaceTwinRoot.SetActive(_hasInPlaceTwinCapture);

                float hx = wMeters * 0.5f;
                float hy = hMeters * 0.5f;
                float hz = dMeters * 0.5f;
                float t = _hasInPlaceTwinCapture ? 0.0024f : 0.0016f;
                _inPlaceTwinRoot.transform.localPosition = new Vector3(_inPlaceTwinLocalCenter.x, Mathf.Max(hy, _inPlaceTwinLocalCenter.y), _inPlaceTwinLocalCenter.z);

                // 4 X-aligned edges
                _inPlaceTwinEdges[0].localPosition = new Vector3(0f, -hy, -hz); _inPlaceTwinEdges[0].localScale = new Vector3(wMeters, t, t);
                _inPlaceTwinEdges[1].localPosition = new Vector3(0f,  hy, -hz); _inPlaceTwinEdges[1].localScale = new Vector3(wMeters, t, t);
                _inPlaceTwinEdges[2].localPosition = new Vector3(0f, -hy,  hz); _inPlaceTwinEdges[2].localScale = new Vector3(wMeters, t, t);
                _inPlaceTwinEdges[3].localPosition = new Vector3(0f,  hy,  hz); _inPlaceTwinEdges[3].localScale = new Vector3(wMeters, t, t);
                // 4 Y-aligned edges
                _inPlaceTwinEdges[4].localPosition = new Vector3(-hx, 0f, -hz); _inPlaceTwinEdges[4].localScale = new Vector3(t, hMeters, t);
                _inPlaceTwinEdges[5].localPosition = new Vector3( hx, 0f, -hz); _inPlaceTwinEdges[5].localScale = new Vector3(t, hMeters, t);
                _inPlaceTwinEdges[6].localPosition = new Vector3(-hx, 0f,  hz); _inPlaceTwinEdges[6].localScale = new Vector3(t, hMeters, t);
                _inPlaceTwinEdges[7].localPosition = new Vector3( hx, 0f,  hz); _inPlaceTwinEdges[7].localScale = new Vector3(t, hMeters, t);
                // 4 Z-aligned edges
                _inPlaceTwinEdges[8].localPosition  = new Vector3(-hx, -hy, 0f); _inPlaceTwinEdges[8].localScale  = new Vector3(t, t, dMeters);
                _inPlaceTwinEdges[9].localPosition  = new Vector3( hx, -hy, 0f); _inPlaceTwinEdges[9].localScale  = new Vector3(t, t, dMeters);
                _inPlaceTwinEdges[10].localPosition = new Vector3(-hx,  hy, 0f); _inPlaceTwinEdges[10].localScale = new Vector3(t, t, dMeters);
                _inPlaceTwinEdges[11].localPosition = new Vector3( hx,  hy, 0f); _inPlaceTwinEdges[11].localScale = new Vector3(t, t, dMeters);

                if (_inPlaceTwinLabel != null)
                {
                    _inPlaceTwinLabel.transform.localPosition = new Vector3(0f, hy + 0.018f, 0f);
                    _inPlaceTwinLabel.text = _hasInPlaceTwinCapture
                        ? $"1:1 MEASURED OBJECT TWIN [LOCKED]\n{ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm"
                        : $"1:1 MEASURED OBJECT TWIN ({ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm)";
                }
            }
        }

        [Preserve]
        public bool TryHandleDirectSpatialCadManipulation(
            Vector3 pointerWorldPos,
            bool isActivePointOrDrag,
            int forcedFaceAxis = -1,
            Vector3? leftPalettePreviewPos = null,
            bool aimBooleanRequested = false,
            int forcedTiltAxis = -1)
        {
            if (DeskWorkspaceOrigin == null || _sampleWorkpiece == null) return false;

            if (_carrySnapCooldown > 0f)
                _carrySnapCooldown = Mathf.Max(0f, _carrySnapCooldown - Time.deltaTime);

            if (!isActivePointOrDrag && forcedFaceAxis < 0 && forcedTiltAxis < 0)
            {
                if (_activeFacePullAxis >= 0 || _activeTiltAxis >= 0)
                {
                    _activeFacePullAxis = -1;
                    _activeTiltAxis = -1;
                    UpdateSampleWorkpieceVisual(ManualTestWidthMm);
                }
                if (_isCarryingSnapModule)
                {
                    _isCarryingSnapModule = false;
                    if (_dragCarryGhostObj != null) _dragCarryGhostObj.SetActive(false);
                    for (int i = 0; i < _magneticTetherSegments.Length; i++)
                        if (_magneticTetherSegments[i] != null) _magneticTetherSegments[i].gameObject.SetActive(false);
                }
                return false;
            }

            Vector3 localPtr = DeskWorkspaceOrigin.InverseTransformPoint(pointerWorldPos);
            Vector3 wpCenter = _sampleWorkpiece.localPosition;

            // 1A. Direct 3D Angular Rotation / Tilt Handles (TILT X° Pitch, TILT Y° Yaw, TILT Z° Roll)
            if (!_isHoldingWorkpiece && forcedFaceAxis < 0)
            {
                if (forcedTiltAxis >= 0 && forcedTiltAxis <= 2)
                {
                    _activeTiltAxis = forcedTiltAxis;
                }
                else if (_activeTiltAxis < 0 && _activeFacePullAxis < 0)
                {
                    for (int t = 0; t < 3; t++)
                    {
                        if (_tiltHandles[t] != null && Vector3.Distance(pointerWorldPos, _tiltHandles[t].position) < 0.036f)
                        {
                            _activeTiltAxis = t;
                            SpatialTactileAudio.Instance?.PlayPokeClick(_tiltHandles[t].position);
                            break;
                        }
                    }
                }
                else if (_activeTiltAxis >= 0 && _tiltHandles[_activeTiltAxis] != null &&
                         Vector3.Distance(pointerWorldPos, _tiltHandles[_activeTiltAxis].position) > 0.22f)
                {
                    _activeTiltAxis = -1;
                }

                if (_activeTiltAxis >= 0)
                {
                    Vector3 rel = localPtr - wpCenter;
                    float rawDeg;
                    string tiltTag;
                    if (_activeTiltAxis == 0) // Pitch around X (drag in YZ plane)
                    {
                        rawDeg = Mathf.Atan2(rel.z, Mathf.Max(0.01f, rel.y)) * Mathf.Rad2Deg * 1.5f;
                        float snapped = Mathf.Clamp(Mathf.Round(rawDeg / 5f) * 5f, -180f, 180f);
                        ModelTiltDeg = new Vector3(snapped, ModelTiltDeg.y, ModelTiltDeg.z);
                        tiltTag = $"TILT X (Pitch) = {ModelTiltDeg.x:F0}°";
                    }
                    else if (_activeTiltAxis == 1) // Yaw around Y (drag in XZ plane)
                    {
                        rawDeg = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg;
                        float snapped = Mathf.Clamp(Mathf.Round(rawDeg / 5f) * 5f, -180f, 180f);
                        ModelTiltDeg = new Vector3(ModelTiltDeg.x, snapped, ModelTiltDeg.z);
                        tiltTag = $"TILT Y (Yaw) = {ModelTiltDeg.y:F0}°";
                    }
                    else // Roll around Z (drag in XY plane)
                    {
                        rawDeg = Mathf.Atan2(-rel.x, Mathf.Max(0.01f, rel.y)) * Mathf.Rad2Deg * 1.5f;
                        float snapped = Mathf.Clamp(Mathf.Round(rawDeg / 5f) * 5f, -180f, 180f);
                        ModelTiltDeg = new Vector3(ModelTiltDeg.x, ModelTiltDeg.y, snapped);
                        tiltTag = $"TILT Z (Roll) = {ModelTiltDeg.z:F0}°";
                    }

                    int tiltBucket = Mathf.RoundToInt((ModelTiltDeg.x + ModelTiltDeg.y + ModelTiltDeg.z) / 5f);
                    if (tiltBucket != _lastTiltTickBucket)
                    {
                        _lastTiltTickBucket = tiltBucket;
                        SpatialTactileAudio.Instance?.PlayCaliperTick(pointerWorldPos);
                    }

                    UpdateSampleWorkpieceVisual(ManualTestWidthMm);
                    StatusMessage = $"3D TILT HANDLE DRAG [{tiltTag}] -> Angles: Pitch X={ModelTiltDeg.x:F0}° | Yaw Y={ModelTiltDeg.y:F0}° | Roll Z={ModelTiltDeg.z:F0}°";
                    return true;
                }
            }

            // 1B. Direct 3D Extrude / Pull Handles (+X Width, +Y Height, +Z Depth up to 600mm!)
            if (!_isHoldingWorkpiece)
            {
                if (forcedFaceAxis >= 0 && forcedFaceAxis <= 2)
                {
                    _activeFacePullAxis = forcedFaceAxis;
                }
                else if (_activeFacePullAxis < 0)
                {
                    for (int a = 0; a < 3; a++)
                    {
                        if (_facePullHandles[a] != null && Vector3.Distance(pointerWorldPos, _facePullHandles[a].position) < 0.036f)
                        {
                            _activeFacePullAxis = a;
                            SpatialTactileAudio.Instance?.PlayPokeClick(_facePullHandles[a].position);
                            break;
                        }
                    }
                }
                else if (_facePullHandles[_activeFacePullAxis] != null &&
                         Vector3.Distance(pointerWorldPos, _facePullHandles[_activeFacePullAxis].position) > 0.25f)
                {
                    _activeFacePullAxis = -1;
                }

                if (_activeFacePullAxis >= 0)
                {
                    DirectPushPullActive = true;
                    string axisTag;
                    int tickBucket;
                    if (_activeFacePullAxis == 0) // +X Width (10..600mm)
                    {
                        double newW = System.Math.Round(System.Math.Abs(localPtr.x - wpCenter.x) * 2000.0);
                        ManualTestWidthMm = System.Math.Max(10.0, System.Math.Min(600.0, newW));
                        axisTag = $"+X WIDTH = {ManualTestWidthMm:F0}mm";
                        tickBucket = (int)(ManualTestWidthMm / 2.0);
                    }
                    else if (_activeFacePullAxis == 1) // +Y Height (10..600mm)
                    {
                        double newH = System.Math.Round(System.Math.Max(0.010f, localPtr.y) * 1000.0);
                        CapturedHeightMm = System.Math.Max(10.0, System.Math.Min(600.0, newH));
                        axisTag = $"+Y HEIGHT = {CapturedHeightMm:F0}mm";
                        tickBucket = (int)(CapturedHeightMm / 2.0);
                    }
                    else // +Z Depth (10..600mm)
                    {
                        double newD = System.Math.Round(System.Math.Abs(localPtr.z - wpCenter.z) * 2000.0);
                        CapturedDepthMm = System.Math.Max(10.0, System.Math.Min(600.0, newD));
                        axisTag = $"+Z DEPTH = {CapturedDepthMm:F0}mm";
                        tickBucket = (int)(CapturedDepthMm / 2.0);
                    }

                    if (tickBucket != _lastFacePullTickBucket)
                    {
                        _lastFacePullTickBucket = tickBucket;
                        SpatialTactileAudio.Instance?.PlayCaliperTick(pointerWorldPos);
                    }

                    UpdateSampleWorkpieceVisual(ManualTestWidthMm);
                    if (SnappedModuleCount >= 1)
                        SynthesizeAndSpawnSync(ManualTestWidthMm);

                    StatusMessage = $"EXTRUDE HANDLE DRAG [{axisTag}] -> {ManualTestWidthMm:F0} x {CapturedDepthMm:F0} x {CapturedHeightMm:F0} mm (Max 600mm) | {CadPartMassGrams:F1}g";
                    return true;
                }
            }

            // 2. Interactive 3D Combine Positioning Cursor (when hovering inside/over the 3D CAD Model)
            float distToWpCenter = Vector3.Distance(localPtr, wpCenter);
            if (!_isHoldingWorkpiece && distToWpCenter < 0.18f && (PureCadStudioMode || CadBooleanModeIndex == 1 || CadBooleanModeIndex == 2 || aimBooleanRequested))
            {
                float maxX = Mathf.Max(8f, (float)ManualTestWidthMm * 0.48f);
                float maxY = Mathf.Max(6f, (float)CapturedHeightMm * 0.58f);
                float maxZ = Mathf.Max(8f, (float)CapturedDepthMm * 0.48f);
                float offXMm = Mathf.Clamp(Mathf.Round((localPtr.x - wpCenter.x) * 500f) * 2f, -maxX, maxX);
                float offYMm = Mathf.Clamp(Mathf.Round((localPtr.y - wpCenter.y) * 500f) * 2f, -maxY * 0.5f, maxY);
                float offZMm = Mathf.Clamp(Mathf.Round((localPtr.z - wpCenter.z) * 500f) * 2f, -maxZ, maxZ);

                _aimedBooleanOffsetMm = new Vector3(offXMm, offYMm, offZMm);
                _hasAimedBooleanCursor = true;
                _booleanCursorActiveTimer = 2.5f;

                if (_liveBooleanCursorObj != null)
                {
                    _liveBooleanCursorObj.SetActive(true);
                    _liveBooleanCursorObj.transform.localPosition = wpCenter + _aimedBooleanOffsetMm * 0.001f;
                    _liveBooleanCursorObj.transform.localRotation = Quaternion.Euler(ModelTiltDeg);
                    float scaleFactor = (CadBooleanModeIndex == 1) ? 0.48f : 0.58f;
                    float cw = Mathf.Clamp((float)(ManualTestWidthMm * 0.001 * scaleFactor), 0.012f, 0.250f);
                    float ch = Mathf.Clamp((float)(CapturedHeightMm * 0.0005 * 0.7f), 0.006f, 0.150f);
                    float cd = Mathf.Clamp((float)(CapturedDepthMm * 0.001 * scaleFactor), 0.012f, 0.250f);
                    _liveBooleanCursorObj.transform.localScale = new Vector3(cw, ch, cd);
                    Color curCol = (CadBooleanModeIndex == 1)
                        ? new Color(1.0f, 0.15f, 0.45f)
                        : ((CadBooleanModeIndex == 2) ? new Color(1.0f, 0.80f, 0.15f) : new Color(0.20f, 1.0f, 0.65f));
                    SetColorSafe(_liveBooleanCursorRenderer, curCol);
                }

                StatusMessage = $"3D COMBINE AIM [{ActiveBooleanModeName}] @ ({offXMm:F0},{offYMm:F0},{offZMm:F0})mm -> Poke [+ADD / CUT BODY] to apply!";
            }

            // 3. Drag-and-Snap Shape from the Left-Hand Palette 3D Preview directly onto the Center 3D CAD Model!
            Transform snapTargetTransform = PureCadStudioMode ? _sampleWorkpiece : _distalSnapPortRing;
            if (_carrySnapCooldown <= 0f && snapTargetTransform != null)
            {
                if (!_isCarryingSnapModule && leftPalettePreviewPos.HasValue &&
                    Vector3.Distance(pointerWorldPos, leftPalettePreviewPos.Value) < 0.052f)
                {
                    _isCarryingSnapModule = true;
                    SpatialTactileAudio.Instance?.PlayPokeClick(leftPalettePreviewPos.Value);
                }

                float distToSnapPort = Vector3.Distance(pointerWorldPos, snapTargetTransform.position);
                bool showTether = _isCarryingSnapModule || (!PureCadStudioMode && distToSnapPort < 0.125f && distToSnapPort > 0.015f);

                if (showTether)
                {
                    if (_dragCarryGhostObj != null)
                    {
                        _dragCarryGhostObj.SetActive(true);
                        _dragCarryGhostObj.transform.position = pointerWorldPos;
                        _dragCarryGhostObj.transform.Rotate(Vector3.up, Time.deltaTime * 90f, Space.Self);
                        float gw = Mathf.Clamp((float)(ManualTestWidthMm * 0.00045), 0.016f, 0.085f);
                        float gh = Mathf.Clamp((float)(CapturedHeightMm * 0.00045), 0.012f, 0.075f);
                        float gd = Mathf.Clamp((float)(CapturedDepthMm * 0.00045), 0.016f, 0.085f);
                        _dragCarryGhostObj.transform.localScale = new Vector3(gw, gh, gd);
                    }

                    for (int i = 0; i < _magneticTetherSegments.Length; i++)
                    {
                        if (_magneticTetherSegments[i] == null) continue;
                        _magneticTetherSegments[i].gameObject.SetActive(true);
                        float t = (i + 1f) / (_magneticTetherSegments.Length + 1f);
                        _magneticTetherSegments[i].position = Vector3.Lerp(pointerWorldPos, snapTargetTransform.position, t);
                        float pulse = 0.0065f + 0.0025f * Mathf.Sin(Time.time * 10f - i * 0.7f);
                        _magneticTetherSegments[i].localScale = Vector3.one * pulse;
                        SetColorSafe(_magneticTetherRenderers[i], Color.Lerp(new Color(0.20f, 0.95f, 1.0f), new Color(1.0f, 0.82f, 0.15f), t));
                    }

                    float snapThreshold = PureCadStudioMode ? 0.075f : 0.042f;
                    if (distToSnapPort < snapThreshold)
                    {
                        _isCarryingSnapModule = false;
                        _carrySnapCooldown = 1.25f;
                        if (_dragCarryGhostObj != null) _dragCarryGhostObj.SetActive(false);
                        for (int i = 0; i < _magneticTetherSegments.Length; i++)
                            if (_magneticTetherSegments[i] != null) _magneticTetherSegments[i].gameObject.SetActive(false);

                        if (PureCadStudioMode)
                            StampNewCadBodyToAssembly();
                        else
                            SnapAddModularJointAndLink();
                        return true;
                    }
                    else if (_isCarryingSnapModule)
                    {
                        StatusMessage = $"DRAGGING [{ActivePrimitiveShapeName}] -> Bring to Center 3D Model ({distToSnapPort * 100f:F1}cm) to {ActiveBooleanModeName}!";
                        return true;
                    }
                }
                else
                {
                    if (_dragCarryGhostObj != null && _dragCarryGhostObj.activeSelf) _dragCarryGhostObj.SetActive(false);
                    for (int i = 0; i < _magneticTetherSegments.Length; i++)
                    {
                        if (_magneticTetherSegments[i] != null && _magneticTetherSegments[i].gameObject.activeSelf)
                            _magneticTetherSegments[i].gameObject.SetActive(false);
                    }
                }
            }

            return false;
        }

        private void RebuildCadFeatureSubParts(
            Transform parentContainer, List<GameObject> partList,
            float wM, float dM, float hM, Color matColor, float scaleMultiplier = 1.0f)
        {
            for (int i = 0; i < partList.Count; i++)
            {
                if (partList[i] != null) Destroy(partList[i]);
            }
            partList.Clear();

            Color primaryColor = GetBooleanTintedColor(matColor, CadBooleanModeIndex);
            BuildPatternedPrimitiveVisual(
                parentContainer, partList, "Primary", Vector3.zero, Quaternion.identity, wM, dM, hM,
                CadPrimitiveShapeIndex, CadFeatureOpIndex, CadPatternModeIndex, primaryColor);

            // Render every additional body stamped from the Left-Hand Spatial CAD Palette (including its 3D offset & 3D tilt!)
            for (int b = 0; b < _stampedCadBodies.Count; b++)
            {
                StampedCadBodySpec body = _stampedCadBodies[b];
                if (body == null) continue;
                Vector3 localOff = new Vector3(
                    (float)(body.localOffsetXMm * 0.001) * scaleMultiplier,
                    (float)(body.localOffsetYMm * 0.001) * scaleMultiplier,
                    (float)(body.localOffsetZMm * 0.001) * scaleMultiplier);
                Quaternion localRot = Quaternion.Euler(
                    (float)body.localRotXDeg,
                    (float)body.localRotYDeg,
                    (float)body.localRotZDeg);
                float bw = Mathf.Clamp((float)(body.widthMm * 0.001), 0.008f, 0.600f) * scaleMultiplier;
                float bd = Mathf.Clamp((float)(body.depthMm * 0.001), 0.008f, 0.600f) * scaleMultiplier;
                float bh = Mathf.Clamp((float)(body.heightMm * 0.001), 0.008f, 0.600f) * scaleMultiplier;
                Color bodyCol = GetBooleanTintedColor(matColor, body.booleanMode);
                BuildPatternedPrimitiveVisual(
                    parentContainer, partList, body.bodyName, localOff, localRot, bw, bd, bh,
                    body.primitiveShape, body.featureOp, body.patternMode, bodyCol);
            }
        }

        private static Color GetBooleanTintedColor(Color baseMatColor, int booleanMode)
        {
            if (booleanMode == 1) // BOOLEAN CUT (Subtract) -> High-contrast glowing crimson/magenta negative cutter volume
                return new Color(0.95f, 0.18f, 0.45f);
            if (booleanMode == 2) // BOOLEAN INTERSECT -> Precision gold-amber shared volume
                return new Color(0.95f, 0.78f, 0.18f);
            return baseMatColor;  // BOOLEAN JOIN (Union)
        }

        private void BuildPatternedPrimitiveVisual(
            Transform parentContainer, List<GameObject> partList, string prefix,
            Vector3 centerOffset, Quaternion bodyRot, float wM, float dM, float hM,
            int primitiveShape, int featureOp, int patternMode, Color col)
        {
            // Create a dedicated sub-pivot if rotated so all sub-parts tilt cleanly together
            Transform targetParent = parentContainer;
            Vector3 baseOff = centerOffset;
            if (Quaternion.Angle(bodyRot, Quaternion.identity) > 0.5f)
            {
                GameObject pivotGo = new GameObject($"{prefix}_TiltPivot");
                pivotGo.transform.SetParent(parentContainer, false);
                pivotGo.transform.localPosition = centerOffset;
                pivotGo.transform.localRotation = bodyRot;
                pivotGo.transform.localScale = Vector3.one;
                partList.Add(pivotGo);
                targetParent = pivotGo.transform;
                baseOff = Vector3.zero;
            }

            if (patternMode == 1) // 3x Linear Pattern along X
            {
                float pitch = Mathf.Max(0.018f, wM * 0.65f);
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 pos = baseOff + new Vector3(k * pitch, 0f, 0f);
                    BuildSinglePrimitiveVisual(targetParent, partList, $"{prefix}_Lin{k + 1}", pos, wM * 0.52f, dM * 0.52f, hM, primitiveShape, featureOp, col);
                }
            }
            else if (patternMode == 2) // 6x Circular Radial Flange Pattern
            {
                float radius = Mathf.Max(0.016f, Mathf.Min(wM, dM) * 0.42f);
                for (int k = 0; k < 6; k++)
                {
                    float ang = k * Mathf.PI / 3f;
                    Vector3 pos = baseOff + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
                    BuildSinglePrimitiveVisual(targetParent, partList, $"{prefix}_Circ{k}", pos, wM * 0.36f, dM * 0.36f, hM, primitiveShape, featureOp, col);
                }
            }
            else
            {
                BuildSinglePrimitiveVisual(targetParent, partList, prefix, baseOff, wM, dM, hM, primitiveShape, featureOp, col);
            }
        }

        private void BuildSinglePrimitiveVisual(
            Transform parentContainer, List<GameObject> partList, string prefix,
            Vector3 offset, float wM, float dM, float hM, int primitiveShape, int featureOp, Color matColor)
        {
            // 12 Engineering & Fusion 360 Shapes:
            // 0: Box / Cube, 1: Cylinder, 2: Sphere, 3: Cone / Frustum, 4: Torus Ring, 5: Hollow Tube,
            // 6: Hex Prism (M-Nut), 7: Wedge / 45° Ramp, 8: L-Bracket, 9: U-Channel, 10: T-Beam Extrusion, 11: Flanged Bearing
            if (primitiveShape == 1) // Cylinder
            {
                AddSubCylinder(parentContainer, partList, $"{prefix}_Cylinder", offset, new Vector3(wM, hM * 0.5f, dM), matColor);
                return;
            }
            if (primitiveShape == 2) // Sphere
            {
                AddSubSphere(parentContainer, partList, $"{prefix}_Sphere", offset, new Vector3(wM, hM, dM), matColor);
                return;
            }
            if (primitiveShape == 3) // Cone / Frustum (4-tier stepped tapered frustum)
            {
                int tiers = 5;
                float tierH = hM / tiers;
                for (int t = 0; t < tiers; t++)
                {
                    float frac = 1f - (t / (float)tiers) * 0.72f;
                    float yPos = -hM * 0.5f + (t + 0.5f) * tierH;
                    AddSubCylinder(parentContainer, partList, $"{prefix}_ConeTier_{t}", offset + new Vector3(0f, yPos, 0f), new Vector3(wM * frac, tierH * 0.52f, dM * frac), matColor);
                }
                return;
            }
            if (primitiveShape == 4) // Torus Ring (10 radial segments)
            {
                float rRing = Mathf.Max(0.008f, Mathf.Min(wM, dM) * 0.38f);
                float segSize = Mathf.Max(0.004f, wM * 0.24f);
                for (int k = 0; k < 10; k++)
                {
                    float ang = k * Mathf.PI * 0.20f;
                    Vector3 p = offset + new Vector3(Mathf.Cos(ang) * rRing, 0f, Mathf.Sin(ang) * rRing);
                    AddSubSphere(parentContainer, partList, $"{prefix}_Torus_{k}", p, new Vector3(segSize, hM * 0.55f, segSize), matColor);
                }
                return;
            }
            if (primitiveShape == 5) // Hollow Tube (outer sleeve + dark inner bore collar)
            {
                AddSubCylinder(parentContainer, partList, $"{prefix}_TubeOuter", offset, new Vector3(wM, hM * 0.5f, dM), matColor);
                AddSubCylinder(parentContainer, partList, $"{prefix}_TubeBore", offset, new Vector3(wM * 0.68f, hM * 0.52f, dM * 0.68f), new Color(0.08f, 0.11f, 0.16f));
                return;
            }
            if (primitiveShape == 6) // Hex Prism (M-Nut) - 3 intersecting 60° rotated flats + center bore
            {
                for (int k = 0; k < 3; k++)
                {
                    GameObject hexBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    hexBar.name = $"{prefix}_HexFlat_{k}";
                    Destroy(hexBar.GetComponent<Collider>());
                    hexBar.transform.SetParent(parentContainer, false);
                    hexBar.transform.localPosition = offset;
                    hexBar.transform.localRotation = Quaternion.Euler(0f, k * 60f, 0f);
                    hexBar.transform.localScale = new Vector3(wM * 0.86f, hM, dM * 0.50f);
                    SetColorSafe(hexBar.GetComponent<Renderer>(), matColor);
                    partList.Add(hexBar);
                }
                AddSubCylinder(parentContainer, partList, $"{prefix}_HexBore", offset, new Vector3(wM * 0.44f, hM * 0.52f, dM * 0.44f), new Color(0.08f, 0.11f, 0.16f));
                return;
            }
            if (primitiveShape == 7) // Wedge / 45° Ramp (base + sloped ramp plate)
            {
                float baseT = Mathf.Max(0.004f, hM * 0.18f);
                AddSubBox(parentContainer, partList, $"{prefix}_WedgeBase", offset + new Vector3(0f, -hM * 0.5f + baseT * 0.5f, 0f), new Vector3(wM, baseT, dM), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_WedgeBack", offset + new Vector3(-wM * 0.40f, 0f, 0f), new Vector3(wM * 0.20f, hM, dM), matColor * 0.94f);
                GameObject slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slope.name = $"{prefix}_WedgeSlope";
                Destroy(slope.GetComponent<Collider>());
                slope.transform.SetParent(parentContainer, false);
                slope.transform.localPosition = offset + new Vector3(wM * 0.05f, 0f, 0f);
                float tiltDeg = -Mathf.Atan2(hM * 0.82f, wM * 0.82f) * Mathf.Rad2Deg;
                slope.transform.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
                float hyp = Mathf.Sqrt(wM * wM * 0.72f + hM * hM * 0.72f);
                slope.transform.localScale = new Vector3(hyp, Mathf.Max(0.005f, hM * 0.22f), dM * 0.98f);
                SetColorSafe(slope.GetComponent<Renderer>(), matColor * 1.08f);
                partList.Add(slope);
                return;
            }

            float tWall = 0.0025f; // 2.5mm 3D-print shell wall
            if (primitiveShape == 8 || featureOp == 3) // L-Bracket Structural Mount with Gusset Ribs
            {
                float flangeT = Mathf.Max(0.004f, Mathf.Min(wM, hM) * 0.20f);
                AddSubBox(parentContainer, partList, $"{prefix}_LBracket_Base", offset + new Vector3(0f, -hM * 0.5f + flangeT * 0.5f, 0f), new Vector3(wM, flangeT, dM), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_LBracket_Vert", offset + new Vector3(-wM * 0.5f + flangeT * 0.5f, 0f, 0f), new Vector3(flangeT, hM, dM), matColor * 0.94f);
                AddSubBox(parentContainer, partList, $"{prefix}_LBracket_GussetF", offset + new Vector3(-wM * 0.18f, -hM * 0.12f, -dM * 0.40f), new Vector3(wM * 0.42f, hM * 0.42f, 0.003f), matColor * 1.15f);
                AddSubBox(parentContainer, partList, $"{prefix}_LBracket_GussetB", offset + new Vector3(-wM * 0.18f, -hM * 0.12f,  dM * 0.40f), new Vector3(wM * 0.42f, hM * 0.42f, 0.003f), matColor * 1.15f);
                return;
            }
            if (primitiveShape == 9) // U-Channel Structural Rail
            {
                float wallT = Mathf.Max(0.004f, Mathf.Min(wM, hM) * 0.18f);
                AddSubBox(parentContainer, partList, $"{prefix}_UChan_Base",  offset + new Vector3(0f, -hM * 0.5f + wallT * 0.5f, 0f), new Vector3(wM, wallT, dM), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_UChan_Left",  offset + new Vector3(-wM * 0.5f + wallT * 0.5f, 0f, 0f), new Vector3(wallT, hM, dM), matColor * 0.92f);
                AddSubBox(parentContainer, partList, $"{prefix}_UChan_Right", offset + new Vector3( wM * 0.5f - wallT * 0.5f, 0f, 0f), new Vector3(wallT, hM, dM), matColor * 0.92f);
                return;
            }
            if (primitiveShape == 10) // T-Beam Extrusion
            {
                float webT = Mathf.Max(0.004f, Mathf.Min(wM, hM) * 0.20f);
                AddSubBox(parentContainer, partList, $"{prefix}_TBeam_TopFlange", offset + new Vector3(0f, hM * 0.5f - webT * 0.5f, 0f), new Vector3(wM, webT, dM), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_TBeam_CenterWeb", offset + new Vector3(0f, -webT * 0.25f, 0f), new Vector3(webT, Mathf.Max(0.006f, hM - webT), dM), matColor * 0.92f);
                return;
            }
            if (primitiveShape == 11) // Flanged Bearing Housing (base flange + tall boss hub + precision center bore)
            {
                float flangeH = Mathf.Max(0.004f, hM * 0.28f);
                AddSubCylinder(parentContainer, partList, $"{prefix}_BearingFlange", offset + new Vector3(0f, -hM * 0.5f + flangeH * 0.5f, 0f), new Vector3(wM, flangeH * 0.5f, dM), matColor);
                AddSubCylinder(parentContainer, partList, $"{prefix}_BearingHub", offset, new Vector3(wM * 0.64f, hM * 0.5f, dM * 0.64f), matColor * 1.08f);
                AddSubCylinder(parentContainer, partList, $"{prefix}_BearingBore", offset, new Vector3(wM * 0.34f, hM * 0.53f, dM * 0.34f), new Color(0.08f, 0.11f, 0.16f));
                return;
            }

            if (featureOp == 1) // Shell 2.5mm Hollow Enclosure
            {
                AddSubBox(parentContainer, partList, $"{prefix}_Shell_Floor", offset + new Vector3(0f, -hM * 0.5f + tWall * 0.5f, 0f), new Vector3(wM, tWall, dM), matColor * 0.85f);
                AddSubBox(parentContainer, partList, $"{prefix}_Shell_Front", offset + new Vector3(0f, 0f, -dM * 0.5f + tWall * 0.5f), new Vector3(wM, hM, tWall), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_Shell_Back",  offset + new Vector3(0f, 0f,  dM * 0.5f - tWall * 0.5f), new Vector3(wM, hM, tWall), matColor);
                AddSubBox(parentContainer, partList, $"{prefix}_Shell_Left",  offset + new Vector3(-wM * 0.5f + tWall * 0.5f, 0f, 0f), new Vector3(tWall, hM, Mathf.Max(0.004f, dM - 2f * tWall)), matColor * 0.92f);
                AddSubBox(parentContainer, partList, $"{prefix}_Shell_Right", offset + new Vector3( wM * 0.5f - tWall * 0.5f, 0f, 0f), new Vector3(tWall, hM, Mathf.Max(0.004f, dM - 2f * tWall)), matColor * 0.92f);
            }
            else // Solid Extrude (0) or Chamfer + M4 Mounting Bosses (2)
            {
                AddSubBox(parentContainer, partList, $"{prefix}_Solid_Core", offset, new Vector3(wM, hM, dM), matColor);
                if (featureOp == 2)
                {
                    float bx = wM * 0.36f, bz = dM * 0.36f;
                    Color bossCol = new Color(0.90f, 0.78f, 0.25f);
                    AddSubCylinder(parentContainer, partList, $"{prefix}_M4_1", offset + new Vector3(-bx, hM * 0.5f + 0.002f, -bz), new Vector3(0.006f, 0.0025f, 0.006f), bossCol);
                    AddSubCylinder(parentContainer, partList, $"{prefix}_M4_2", offset + new Vector3( bx, hM * 0.5f + 0.002f, -bz), new Vector3(0.006f, 0.0025f, 0.006f), bossCol);
                    AddSubCylinder(parentContainer, partList, $"{prefix}_M4_3", offset + new Vector3(-bx, hM * 0.5f + 0.002f,  bz), new Vector3(0.006f, 0.0025f, 0.006f), bossCol);
                    AddSubCylinder(parentContainer, partList, $"{prefix}_M4_4", offset + new Vector3( bx, hM * 0.5f + 0.002f,  bz), new Vector3(0.006f, 0.0025f, 0.006f), bossCol);
                }
            }
        }

        private void AddSubSphere(Transform parent, List<GameObject> list, string partName, Vector3 localPos, Vector3 localScale, Color col)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            SetColorSafe(go.GetComponent<Renderer>(), col);
            list.Add(go);
        }

        private void AddSubBox(Transform parent, List<GameObject> list, string partName, Vector3 localPos, Vector3 localScale, Color col)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            SetColorSafe(go.GetComponent<Renderer>(), col);
            list.Add(go);
        }

        private void AddSubCylinder(Transform parent, List<GameObject> list, string partName, Vector3 localPos, Vector3 localScale, Color col)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            SetColorSafe(go.GetComponent<Renderer>(), col);
            list.Add(go);
        }

        private void UpdateSampleWorkpieceVisual(double widthMm)
        {
            if (_sampleWorkpiece == null) return;
            float wMeters = Mathf.Clamp((float)(widthMm * 0.001), 0.010f, 0.600f);
            float dMeters = Mathf.Clamp((float)(CapturedDepthMm * 0.001), 0.010f, 0.600f);
            float hMeters = Mathf.Clamp((float)(CapturedHeightMm * 0.001), 0.010f, 0.600f);

            // Calculate exact CAD solid volume (cm³), mass (g), and 3D-print slicer time (min) across primitive shape + multi-body tree
            double rawBoxCm3 = (widthMm * 0.1) * (CapturedDepthMm * 0.1) * (CapturedHeightMm * 0.1);
            double shapeFactor = (CadPrimitiveShapeIndex == 1 || CadPrimitiveShapeIndex == 6) ? 0.785
                : ((CadPrimitiveShapeIndex == 2 || CadPrimitiveShapeIndex == 3 || CadPrimitiveShapeIndex == 7) ? 0.524
                : ((CadPrimitiveShapeIndex == 4 || CadPrimitiveShapeIndex == 5 || CadPrimitiveShapeIndex >= 8) ? 0.44 : 1.0));
            double fillFactor = (CadFeatureOpIndex == 1) ? 0.28 : ((CadFeatureOpIndex == 3) ? 0.36 : 0.45);
            double patternFactor = (CadPatternModeIndex == 1) ? 1.45 : ((CadPatternModeIndex == 2) ? 1.80 : 1.0);
            double netCm3 = rawBoxCm3 * shapeFactor * fillFactor * patternFactor;

            for (int b = 0; b < _stampedCadBodies.Count; b++)
            {
                StampedCadBodySpec body = _stampedCadBodies[b];
                if (body == null) continue;
                double bVol = (body.widthMm * 0.1) * (body.depthMm * 0.1) * (body.heightMm * 0.1) * 0.35;
                netCm3 += (body.booleanMode == 1) ? (-bVol * 0.5) : bVol; // Boolean Cut subtracts mass!
            }

            CadPartVolumeCm3 = System.Math.Max(2.5, netCm3);
            double rho = CadMaterialDensities[CadMaterialIndex % CadMaterialDensities.Length];
            CadPartMassGrams = CadPartVolumeCm3 * rho;
            EstimatedPrintMinutes = Mathf.Max(6, Mathf.RoundToInt((float)(CadPartVolumeCm3 * 0.68)));
            int totalBodies = 1 + _stampedCadBodies.Count;

            if (!_isHoldingWorkpiece)
            {
                Vector3 transOffsetM = ModelTranslationMm * 0.001f;
                _sampleWorkpiece.localPosition = new Vector3(
                    _workpieceHomeLocalPos.x + transOffsetM.x,
                    hMeters * 0.5f + transOffsetM.y,
                    _workpieceHomeLocalPos.z + transOffsetM.z);
                _sampleWorkpiece.localRotation = Quaternion.Euler(ModelTiltDeg);
                if (_workpieceLabel != null)
                    _workpieceLabel.transform.localPosition = _sampleWorkpiece.localPosition + new Vector3(0f, hMeters * 0.5f + 0.028f, 0f);
            }

            // Hide root cube mesh and render the multi-part Parametric Fusion CAD Solid inside _sampleWorkpiece
            _sampleWorkpiece.localScale = Vector3.one;
            if (_workpieceRenderer != null) _workpieceRenderer.enabled = false;
            Color matCol = _isHoldingWorkpiece ? new Color(0.25f, 1.0f, 0.55f) : CadMaterialColors[CadMaterialIndex % CadMaterialColors.Length];
            RebuildCadFeatureSubParts(_sampleWorkpiece, _workpieceCadSubParts, wMeters, dMeters, hMeters, matCol);

            // Also update the 1:1 Right-Hand Print Preview assembly so holding it in your Right Hand shows the live CAD features!
            if (_handInspectRoot != null)
            {
                RebuildCadFeatureSubParts(_handInspectRoot.transform, _handInspectSubParts, wMeters, dMeters, hMeters, CadMaterialColors[CadMaterialIndex % CadMaterialColors.Length]);
                if (_handInspectLabel != null)
                {
                    _handInspectLabel.transform.localPosition = new Vector3(0f, hMeters * 0.5f + 0.032f, 0f);
                    _handInspectLabel.text =
                        $"1:1 MODEL ON RIGHT HAND [{ActivePrimitiveShapeName} | {totalBodies} Bod{(totalBodies == 1 ? "y" : "ies")} | {ActivePatternModeName}]\n" +
                        $"{widthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm | Tilt({ModelTiltDeg.x:F0}°,{ModelTiltDeg.y:F0}°,{ModelTiltDeg.z:F0}°) | {CadPartMassGrams:F1}g";
                }
            }

            if (_workpieceLabel != null)
            {
                _workpieceLabel.text = _isHoldingWorkpiece
                    ? $"GRASPED CAD ASSEMBLY ({totalBodies} Bod{(totalBodies == 1 ? "y" : "ies")})\nDeliver to Drop Zone ->"
                    : $"1:1 CAD [{ActivePrimitiveShapeName} | {CadFeatureOpNames[CadFeatureOpIndex]} | {totalBodies}B]\n{widthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm | Tilt({ModelTiltDeg.x:F0}°,{ModelTiltDeg.y:F0}°,{ModelTiltDeg.z:F0}°)";
            }

            UpdateInPlaceTwinAndFaceHandles(wMeters, dMeters, hMeters);
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
                if (PureCadStudioMode && _targetRenderer != null) _targetRenderer.enabled = false;
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
            if (PureCadStudioMode && _targetRenderer != null) _targetRenderer.enabled = false;
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

            if (_booleanCursorActiveTimer > 0f)
            {
                _booleanCursorActiveTimer = Mathf.Max(0f, _booleanCursorActiveTimer - Time.deltaTime);
                if (_booleanCursorActiveTimer <= 0f && _liveBooleanCursorObj != null && _liveBooleanCursorObj.activeSelf)
                    _liveBooleanCursorObj.SetActive(false);
            }

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

            // Update "Hold-In-Your-Hands" 1:1 Physical Print Preview on the RIGHT HAND (so it never collides with Left-Hand Palette!)
            if (HandInspectActive && _handInspectRoot != null)
            {
                if (_cachedTipReader == null) _cachedTipReader = FindAnyObjectByType<HandFingertipReader>();
                if (!IsFlatEditorMode && _cachedTipReader != null && (_cachedTipReader.RightValid || _cachedTipReader.LeftValid))
                {
                    Vector3 handPos = _cachedTipReader.RightValid ? _cachedTipReader.RightWorld : _cachedTipReader.LeftWorld;
                    _handInspectRoot.transform.position = Vector3.Lerp(_handInspectRoot.transform.position, handPos + Vector3.up * 0.055f, Time.deltaTime * 14f);
                    _handInspectRoot.transform.Rotate(Vector3.up, Time.deltaTime * 28f, Space.Self);
                }
                else
                {
                    _handInspectRoot.transform.localPosition = new Vector3(0.16f, 0.145f, -0.05f);
                    _handInspectRoot.transform.localRotation = Quaternion.Euler(ModelTiltDeg.x + 15f, Time.time * 38f + ModelTiltDeg.y, ModelTiltDeg.z);
                }
            }

            // Gently rotate the In-VR Exported 3D Model Gallery turntable on the right side of the desk
            if (_exportedGalleryRoot != null && _exportedGalleryRoot.activeSelf && _exportedGalleryModelContainer != null)
            {
                _exportedGalleryModelContainer.Rotate(Vector3.up, Time.deltaTime * 24f, Space.World);
            }

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
                if (PureCadStudioMode)
                {
                    TranslateModelMm(move.normalized * (speed * 250f));
                }
                else
                {
                    AutoOrbitTarget = false;
                    PuppetActive = true;
                    ManualTargetLocalPos += move.normalized * speed;
                }
            }

            if (KInput.GetKeyDown(KeyCode.G)) SynthesizeFast();
            if (KInput.GetKeyDown(KeyCode.N)) SnapAddModularJointAndLink();
            if (KInput.GetKeyDown(KeyCode.K)) CycleJointScrewAxis();
            if (KInput.GetKeyDown(KeyCode.T) || KInput.GetKeyDown(KeyCode.Alpha1)) CycleCadPrimitiveShape();
            if (KInput.GetKeyDown(KeyCode.B) || KInput.GetKeyDown(KeyCode.Alpha2)) StampNewCadBodyToAssembly();
            if (KInput.GetKeyDown(KeyCode.J) || KInput.GetKeyDown(KeyCode.Alpha3)) CycleCadBooleanMode();
            if (KInput.GetKeyDown(KeyCode.L) || KInput.GetKeyDown(KeyCode.Alpha4)) CycleCadPatternArray();
            if (KInput.GetKeyDown(KeyCode.X) || KInput.GetKeyDown(KeyCode.Alpha5)) CycleDirectPushPullExtrude();
            if (KInput.GetKeyDown(KeyCode.V) || KInput.GetKeyDown(KeyCode.Alpha6)) CycleModelTiltAngles();
            if (KInput.GetKeyDown(KeyCode.F)) CycleCadFeatureOp();
            if (KInput.GetKeyDown(KeyCode.M)) CycleCadMaterial();
            if (KInput.GetKeyDown(KeyCode.I)) ToggleHandInspectMode();
            if (KInput.GetKeyDown(KeyCode.H)) SynthesizeDeep();
            if (KInput.GetKeyDown(KeyCode.C)) ExecuteGhostCarve();
            if (KInput.GetKeyDown(KeyCode.LeftBracket)) ScrubDimensionMm(-10.0);
            if (KInput.GetKeyDown(KeyCode.RightBracket)) ScrubDimensionMm(+10.0);
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
                float stepMm = (e.shift ? 15f : 5f);
                switch (e.keyCode)
                {
                    case KeyCode.W: case KeyCode.UpArrow:    TranslateModelMm(new Vector3(0f, 0f, stepMm)); e.Use(); break;
                    case KeyCode.S: case KeyCode.DownArrow:  TranslateModelMm(new Vector3(0f, 0f, -stepMm)); e.Use(); break;
                    case KeyCode.A: case KeyCode.LeftArrow:  TranslateModelMm(new Vector3(-stepMm, 0f, 0f)); e.Use(); break;
                    case KeyCode.D: case KeyCode.RightArrow: TranslateModelMm(new Vector3(stepMm, 0f, 0f)); e.Use(); break;
                    case KeyCode.E: TranslateModelMm(new Vector3(0f, stepMm, 0f)); e.Use(); break;
                    case KeyCode.Q: TranslateModelMm(new Vector3(0f, -stepMm, 0f)); e.Use(); break;
                }
            }

            int totalBodies = 1 + _stampedCadBodies.Count;
            GUI.Box(new Rect(12, 12, 600, 325), "");
            GUILayout.BeginArea(new Rect(20, 18, 584, 312));
            GUILayout.Label($"<b>KINETIC-CAD XR | INTERACTIVE 3D CAD STUDIO (FUSION 360 FOR QUEST 3)</b> ({totalBodies} Bod{(totalBodies == 1 ? "y" : "ies")})");
            GUILayout.Label($"Create ({CadPrimitiveShapeIndex + 1}/12): [{ActivePrimitiveShapeName}] | Combine: [{ActiveBooleanModeName}] | {ManualTestWidthMm:F0}x{CapturedDepthMm:F0}x{CapturedHeightMm:F0}mm (Max 600mm)");
            GUILayout.Label($"6-DoF CAD Pose: Move({ModelTranslationMm.x:F0},{ModelTranslationMm.y:F0},{ModelTranslationMm.z:F0})mm | Tilt Pitch X:{ModelTiltDeg.x:F0}° Yaw Y:{ModelTiltDeg.y:F0}° Roll Z:{ModelTiltDeg.z:F0}°");
            GUILayout.Label($"Status: {StatusMessage}");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"W:{ManualTestWidthMm:F0}mm", GUILayout.Width(65));
            double newW = GUILayout.HorizontalSlider((float)ManualTestWidthMm, 10f, 600f, GUILayout.Width(105));
            GUILayout.Label($"D:{CapturedDepthMm:F0}mm", GUILayout.Width(65));
            double newD = GUILayout.HorizontalSlider((float)CapturedDepthMm, 10f, 600f, GUILayout.Width(105));
            GUILayout.Label($"H:{CapturedHeightMm:F0}mm", GUILayout.Width(65));
            double newH = GUILayout.HorizontalSlider((float)CapturedHeightMm, 10f, 600f, GUILayout.Width(105));
            if (System.Math.Abs(newW - ManualTestWidthMm) > 0.5 || System.Math.Abs(newD - CapturedDepthMm) > 0.5 || System.Math.Abs(newH - CapturedHeightMm) > 0.5)
            {
                ManualTestWidthMm = newW;
                CapturedDepthMm = newD;
                CapturedHeightMm = newH;
                SynthesizeAndSpawnSync(ManualTestWidthMm);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tilt X:{ModelTiltDeg.x:F0}°", GUILayout.Width(68));
            float newRx = Mathf.Round(GUILayout.HorizontalSlider(ModelTiltDeg.x, -180f, 180f, GUILayout.Width(100)) / 5f) * 5f;
            GUILayout.Label($"Tilt Y:{ModelTiltDeg.y:F0}°", GUILayout.Width(68));
            float newRy = Mathf.Round(GUILayout.HorizontalSlider(ModelTiltDeg.y, -180f, 180f, GUILayout.Width(100)) / 5f) * 5f;
            GUILayout.Label($"Tilt Z:{ModelTiltDeg.z:F0}°", GUILayout.Width(68));
            float newRz = Mathf.Round(GUILayout.HorizontalSlider(ModelTiltDeg.z, -180f, 180f, GUILayout.Width(100)) / 5f) * 5f;
            if (Mathf.Abs(newRx - ModelTiltDeg.x) > 0.5f || Mathf.Abs(newRy - ModelTiltDeg.y) > 0.5f || Mathf.Abs(newRz - ModelTiltDeg.z) > 0.5f)
            {
                ModelTiltDeg = new Vector3(newRx, newRy, newRz);
                UpdateSampleWorkpieceVisual(ManualTestWidthMm);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Create ({CadPrimitiveShapeIndex + 1}/12): {ActivePrimitiveShapeName} [T]")) CycleCadPrimitiveShape();
            if (GUILayout.Button($"+Add / Cut Body ({totalBodies}) [B]")) StampNewCadBodyToAssembly();
            if (GUILayout.Button($"Combine: {ActiveBooleanModeName.Split(' ')[0]} [J]")) CycleCadBooleanMode();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Extrude / Pull [X]")) CycleDirectPushPullExtrude();
            if (GUILayout.Button($"Rotate / Tilt 3D [V]")) CycleModelTiltAngles();
            if (GUILayout.Button($"Pattern: {ActivePatternModeName} [L]")) CycleCadPatternArray();
            if (GUILayout.Button($"Modify: {CadFeatureOpNames[CadFeatureOpIndex]} [F]")) CycleCadFeatureOp();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Material: {CadMaterialNames[CadMaterialIndex].Split(' ')[0]} [M]")) CycleCadMaterial();
            if (GUILayout.Button(HandInspectActive ? "Right Hand 1:1: ON [I]" : "Hold 1:1 (Right Hand) [I]")) ToggleHandInspectMode();
            if (GUILayout.Button("Export .STL / .OBJ [U]")) ExportUrdf();
            if (GUILayout.Button("Center Desk [R]")) ResetPose();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
#endif

        private void UpdateVrHologramText()
        {
            if (_vrHologramText == null) return;
            if (DirectPushPullActive || _stampedCadBodies.Count > 0) _stepPuppetDone = true;
            string s1 = _stepSynthDone ? "✓" : "1";
            string s2 = _stepPuppetDone ? "✓" : "2";
            string s3 = _stepAuditOrExportDone ? "✓" : "3";
            int totalBodies = 1 + _stampedCadBodies.Count;

            _vrHologramText.text =
                $"KINETIC-CAD XR | INTERACTIVE 3D CAD STUDIO (FUSION 360 FOR QUEST 3)\n" +
                $"[{s1}]Create ({CadPrimitiveShapeIndex + 1}/12): [{ActivePrimitiveShapeName}]  |  [{s2}]Combine: [{ActiveBooleanModeName}] ({totalBodies} Bod{(totalBodies == 1 ? "y" : "ies")})  |  [{s3}]Pattern: [{ActivePatternModeName}]\n" +
                $"Dimensions (10-600mm): {ManualTestWidthMm:F0}W x {CapturedDepthMm:F0}D x {CapturedHeightMm:F0}H mm  |  3D Tilt: Pitch X:{ModelTiltDeg.x:F0}° Yaw Y:{ModelTiltDeg.y:F0}° Roll Z:{ModelTiltDeg.z:F0}°\n" +
                $"Modify: {CadFeatureOpNames[CadFeatureOpIndex]}  |  Material: {CadMaterialNames[CadMaterialIndex]}  |  Mass: {CadPartMassGrams:F1}g (~{EstimatedPrintMinutes}m 3D Print)\n" +
                $"{StatusMessage}";

            if (_urdfInspectorText != null && CurrentSpec != null)
            {
                if (_urdfInspectorTransform != null && _headCamera != null)
                {
                    Vector3 toInsp = _urdfInspectorTransform.position - _headCamera.transform.position;
                    if (toInsp.sqrMagnitude > 0.001f)
                        _urdfInspectorTransform.rotation = Quaternion.LookRotation(toInsp.normalized, Vector3.up);
                }

                string rName = CurrentSpec.robotName ?? "kinetic_cad";
                string whereSaved = Application.isEditor
                    ? "PC Path: E:\\Unity\\KineticCAD_XR\\Exports\\"
                    : "Quest 3 Path: /sdcard/Android/data/com.DefaultCompany.KineticCADXR/files/";
                _urdfInspectorText.text =
                    $"=== 3D PRINT (.STL / .OBJ) & EXPORTED MODEL VIEWER ===\n" +
                    $"Export Files: {rName}_cad.stl  &  {rName}_cad.obj  (Exports Saved: {CareerUrdfExports})\n" +
                    $"  {whereSaved}\n" +
                    $"  In-VR Preview: Exported 3D Model spawns on Right Desk Turntable when you poke [EXPORT .STL / .OBJ]!\n" +
                    $"  Right-Hand Preview: Poke [HOLD 1:1 MODEL] to hold the 1:1 model on your RIGHT HAND!";
            }

            if (_specSheetText != null && CurrentSpec != null)
            {
                if (_specSheetTransform != null && _headCamera != null)
                {
                    Vector3 toSpec = _specSheetTransform.position - _headCamera.transform.position;
                    if (toSpec.sqrMagnitude > 0.001f)
                        _specSheetTransform.rotation = Quaternion.LookRotation(toSpec.normalized, Vector3.up);
                }

                _specSheetText.text =
                    $"=== FUSION 360 6-DoF CAD CONTROLS (12 SHAPES, UP TO 600MM) ===\n" +
                    $"1. CREATE (12 SHAPES): Box, Cylinder, Sphere, Cone, Torus, Tube, Hex Nut, Wedge, L-Bracket, U-Channel, T-Beam, Bearing\n" +
                    $"2. EXTRUDE (UP TO 600MM): Drag +X (Red), +Y (Green), +Z (Blue) handles on 3D Model\n" +
                    $"3. 3D TILT / ROTATE: Drag TILT X° (Pitch), TILT Y° (Yaw), TILT Z° (Roll) handles or poke [ROTATE / TILT]\n" +
                    $"4. COMBINE & EXPORT: Touch model + poke [+ADD / CUT BODY] -> Poke [EXPORT .STL / .OBJ] to view on Desk Gallery!";
            }
        }

        // ------------------------------------------------------------------ Synthesis / audit / export

        public async void TriggerAsyncSynthesis(double widthMm)
        {
            // Always apply the selected topology immediately (<1ms) so hands-first button pokes and caliper locks
            // give instant 90Hz visual + tactile audio response on Quest 3 without waiting on network latency!
            UpdateSampleWorkpieceVisual(widthMm);
            MechanismSpec instantSpec = AppendSnappedModulesToSpec(
                NebiusKinematicSynthesizer.BuildDeterministicMechanism(widthMm, _topologyVariantIndex));
            ApplyMechanismSpec(instantSpec);
            _spawnAnimTimer = 1.0f;

            string[] topoNames = { "Articulated 3R Claw", "SCARA Selective Arm", "Heavy Turret Palletizer", "Precision Wrist Probe" };
            string topoLabel = topoNames[((_topologyVariantIndex % 4) + 4) % 4];
            if (PureCadStudioMode)
                StatusMessage = $"1:1 CAD MODEL READY -> [{ActivePrimitiveShapeName}] {ManualTestWidthMm:F0} x {CapturedDepthMm:F0} x {CapturedHeightMm:F0} mm ({CadPartMassGrams:F1}g)";
            else
                StatusMessage = $"SYNTHESIZED: {topoLabel} ({widthMm:F0}mm jaw span | {instantSpec.joints.Length}-DoF)";
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
                    ApplyMechanismSpec(AppendSnappedModulesToSpec(aiSpec));
                    _spawnAnimTimer = 0.7f;
                }
                if (!PureCadStudioMode)
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
            MechanismSpec spec = AppendSnappedModulesToSpec(
                NebiusKinematicSynthesizer.BuildDeterministicMechanism(widthMm, _topologyVariantIndex));
            ApplyMechanismSpec(spec);
        }

        public void ExportCurrentUrdf()
        {
            if (CurrentSpec == null) return;
            try
            {
                _stepAuditOrExportDone = true;
                string featName = CadFeatureOpNames[CadFeatureOpIndex % CadFeatureOpNames.Length];
                string matName = CadMaterialNames[CadMaterialIndex % CadMaterialNames.Length];
                string shapeName = ActivePrimitiveShapeName;
                string boolName = ActiveBooleanModeName;
                string patName = ActivePatternModeName;

                LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, Application.persistentDataPath);
                string stlPath = UrdfSerializer.SaveStlToFile(
                    CurrentSpec, Application.persistentDataPath, ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm,
                    CadFeatureOpIndex, CadPrimitiveShapeIndex, CadBooleanModeIndex, CadPatternModeIndex, _stampedCadBodies);
                string objPath = UrdfSerializer.SaveObjToFile(
                    CurrentSpec, Application.persistentDataPath, ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm,
                    CadFeatureOpIndex, matName, CadPrimitiveShapeIndex, CadBooleanModeIndex, CadPatternModeIndex, _stampedCadBodies);
                string cbfPath = UrdfSerializer.SaveCbfEnvelopeToFile(
                    CurrentSpec, Application.persistentDataPath, CurrentBarrierHxMm, SweptSampleCount, CarvedVoxelCount, IsHousingCarved,
                    ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm, featName, matName, CadPartMassGrams,
                    shapeName, boolName, patName, _stampedCadBodies);

                if (Application.isEditor)
                {
                    try
                    {
                        DirectoryInfo rootDir = Directory.GetParent(Application.dataPath);
                        if (rootDir != null)
                        {
                            string exportsDir = Path.Combine(rootDir.FullName, "Exports");
                            LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, exportsDir);
                            stlPath = UrdfSerializer.SaveStlToFile(
                                CurrentSpec, exportsDir, ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm,
                                CadFeatureOpIndex, CadPrimitiveShapeIndex, CadBooleanModeIndex, CadPatternModeIndex, _stampedCadBodies);
                            objPath = UrdfSerializer.SaveObjToFile(
                                CurrentSpec, exportsDir, ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm,
                                CadFeatureOpIndex, matName, CadPrimitiveShapeIndex, CadBooleanModeIndex, CadPatternModeIndex, _stampedCadBodies);
                            cbfPath = UrdfSerializer.SaveCbfEnvelopeToFile(
                                CurrentSpec, exportsDir, CurrentBarrierHxMm, SweptSampleCount, CarvedVoxelCount, IsHousingCarved,
                                ManualTestWidthMm, CapturedDepthMm, CapturedHeightMm, featName, matName, CadPartMassGrams,
                                shapeName, boolName, patName, _stampedCadBodies);
                        }
                    }
                    catch { }
                }
                LastExportedStlPath = stlPath;
                LastExportedObjPath = objPath;
                _exportFlashTimer = 0.8f; // Flash links bright emerald green to confirm STL + OBJ + URDF export!
                CareerUrdfExports++;
                PlayerPrefs.SetInt("KineticCAD_TotalExports", CareerUrdfExports);
                PlayerPrefs.Save();
                RefreshExportedModelGallery(stlPath, objPath);
                SpatialTactileAudio.Instance?.PlayExportSuccess();
                StatusMessage = $"EXPORTED & DISPLAYED ON RIGHT TURNTABLE -> {Path.GetFileName(stlPath)} + {Path.GetFileName(objPath)} ({1 + _stampedCadBodies.Count} Bodies)";
                Debug.Log($"[KineticCAD] Exported URDF: {LastExportedUrdfPath} | STL: {stlPath} | OBJ: {objPath} | CBF: {cbfPath}");
                WriteDiagnosticState("ExportUrdf");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusMessage = "STL/OBJ export failed (see log)";
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
                }
                UpdateSampleWorkpieceVisual(ManualTestWidthMm);
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
            if (PureCadStudioMode)
                _mechanismRoot.SetActive(false);

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
            _pinTransforms = new Transform[n];

            for (int i = 0; i < n; i++)
            {
                JointSpec j = spec.joints[i];
                GameObject pivot = new GameObject($"Joint_{i}_{j.name}");
                pivot.transform.SetParent(_mechanismRoot.transform, false);
                _jointPivots[i] = pivot.transform;

                bool isPrismatic = string.Equals(j.jointType, "prismatic", StringComparison.OrdinalIgnoreCase);
                GameObject pin = GameObject.CreatePrimitive(isPrismatic ? PrimitiveType.Cube : PrimitiveType.Cylinder);
                pin.name = $"ScrewAxisPin_{i}";
                // Keep Collider enabled so VR fingertip pokes and Desktop mouse clicks can flip Screw Axis S_i!
                pin.transform.SetParent(pivot.transform, false);
                pin.transform.localPosition = new Vector3((float)j.originX, (float)j.originY, (float)j.originZ);
                Vector3 axisDir = new Vector3((float)j.axisX, (float)j.axisY, (float)j.axisZ).normalized;
                if (axisDir.sqrMagnitude > 0.001f)
                    pin.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axisDir);
                pin.transform.localScale = isPrismatic
                    ? new Vector3(0.016f, 0.042f, 0.016f)
                    : new Vector3(0.020f, 0.024f, 0.020f);
                _pinRenderers[i] = pin.GetComponent<Renderer>();
                _pinTransforms[i] = pin.transform;
                SetColorSafe(_pinRenderers[i], isPrismatic ? new Color(1.0f, 0.80f, 0.15f) : new Color(0.0f, 0.95f, 1.0f));
            }

            // Attach each moving link to the joint whose frame carries it (supports 3R, True SCARA, and N-DoF Snapped Modules!)
            bool coLocatedShoulder = (n >= 2 && System.Math.Abs(spec.joints[1].originX - spec.joints[0].originX) < 0.02);
            int movingLinks = System.Math.Max(0, linkCount - 1);
            int offset = coLocatedShoulder ? System.Math.Max(0, n - movingLinks) : 0;
            for (int k = 1; k < linkCount; k++)
            {
                int pivotIndex = System.Math.Min(n - 1, k - 1 + offset);
                if (pivotIndex < 0) break;
                LinkSpec lk = spec.links[k];

                // For snapped modules (pivotIndex >= 3), render with the user's sculpted primitive shape if available!
                PrimitiveType linkPrim = PrimitiveType.Cube;
                int snappedIdx = pivotIndex - 3;
                if (snappedIdx >= 0 && snappedIdx < _snappedModuleCustomSpecs.Count && _snappedModuleCustomSpecs[snappedIdx] != null)
                {
                    int sh = _snappedModuleCustomSpecs[snappedIdx].primitiveShape;
                    if (sh == 1 || sh == 3 || sh == 4) linkPrim = PrimitiveType.Cylinder;
                    else if (sh == 2) linkPrim = PrimitiveType.Sphere;
                }

                GameObject box = GameObject.CreatePrimitive(linkPrim);
                box.name = lk.name;
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(_jointPivots[pivotIndex], false);
                box.transform.localPosition = new Vector3((float)lk.centerX, (float)lk.centerY, (float)lk.centerZ);
                if (linkPrim == PrimitiveType.Cylinder)
                {
                    box.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    box.transform.localScale = new Vector3((float)lk.heightM, (float)(lk.lengthM * 0.5), (float)lk.widthM);
                }
                else
                {
                    box.transform.localScale = new Vector3((float)lk.lengthM, (float)lk.heightM, (float)lk.widthM);
                }
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

            // Coaxial Magnetic Snap Port Ring at the distal end-effector tip
            GameObject snapRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            snapRing.name = "Distal_CoaxialSnapPortRing";
            Destroy(snapRing.GetComponent<Collider>());
            snapRing.transform.SetParent(_eeMarker, false);
            snapRing.transform.localPosition = new Vector3(-0.35f, 0f, 0f);
            snapRing.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            snapRing.transform.localScale = new Vector3(1.55f, 0.12f, 1.55f);
            SetColorSafe(snapRing.GetComponent<Renderer>(), new Color(1.0f, 0.78f, 0.15f));
            _distalSnapPortRing = snapRing.transform;

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
            if (PureCadStudioMode) return;
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
                    bool isPris = (CurrentSpec?.joints != null && i < CurrentSpec.joints.Length &&
                                   string.Equals(CurrentSpec.joints[i].jointType, "prismatic", StringComparison.OrdinalIgnoreCase));
                    double span = System.Math.Max(0.05, _maxLimits[i] - _minLimits[i]);
                    double margin = System.Math.Min(_theta[i] - _minLimits[i], _maxLimits[i] - _theta[i]);
                    float limitSafety = Mathf.Clamp01((float)(margin / (span * 0.22)));
                    Color basePinCol = isPris ? new Color(1.0f, 0.82f, 0.15f) : new Color(0.0f, 0.95f, 1.0f);
                    Color pinCol = Color.Lerp(new Color(1.0f, 0.35f, 0.15f), basePinCol, limitSafety);
                    SetColorSafe(_pinRenderers[i], pinCol);
                }
            }

            if (_pinPokeCooldown > 0f)
                _pinPokeCooldown = Mathf.Max(0f, _pinPokeCooldown - Time.deltaTime);

            // Direct Screw-Axis Joint Pin Inspector: poke any cyan/gold joint pin with Left Index Finger to flip S_i axis/type!
            if (!IsFlatEditorMode && _pinPokeCooldown <= 0f && _pinTransforms != null)
            {
                if (_cachedTipReader == null) _cachedTipReader = FindAnyObjectByType<HandFingertipReader>();
                if (_cachedTipReader != null && _cachedTipReader.LeftPointingOnly)
                {
                    for (int i = 0; i < _pinTransforms.Length; i++)
                    {
                        if (_pinTransforms[i] != null && Vector3.Distance(_cachedTipReader.LeftWorld, _pinTransforms[i].position) < 0.030f)
                        {
                            _pinPokeCooldown = 0.90f;
                            CycleJointScrewAxis(i);
                            break;
                        }
                    }
                }
            }

            if (_distalSnapPortRing != null)
            {
                float ringPulse = 1.45f + 0.18f * Mathf.Sin(Time.time * 4.5f);
                _distalSnapPortRing.localScale = new Vector3(ringPulse, 0.12f, ringPulse);

                // Direct Hands-On Coaxial Magnetic Port Snapping: bring Left Index Tip within 3.2cm of the distal snap port!
                if (!IsFlatEditorMode && _graspCooldownTimer <= 0f)
                {
                    if (_cachedTipReader == null) _cachedTipReader = FindAnyObjectByType<HandFingertipReader>();
                    if (_cachedTipReader != null && _cachedTipReader.LeftPointingOnly)
                    {
                        float snapDist = Vector3.Distance(_cachedTipReader.LeftWorld, _distalSnapPortRing.position);
                        if (snapDist < 0.032f)
                        {
                            _graspCooldownTimer = 1.4f;
                            SnapAddModularJointAndLink();
                        }
                    }
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
                UpdateSweptVolumeAndCbf();
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
                    UpdateSampleWorkpieceVisual(ManualTestWidthMm);
                    StatusMessage = $"GRASP LOCKED (Lv{CurrentChallengeLevel}) -> Keep arm cyan & deliver CAD part to Drop Zone!";
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
                    CareerDeliveries++;
                    PlayerPrefs.SetInt("KineticCAD_TotalDeliveries", CareerDeliveries);
                    PlayerPrefs.Save();
                    double normHealth = ReferenceManipulability > 1e-6 ? (_minManipulabilityDuringCarry / ReferenceManipulability) : 0.8;
                    LastDeliveryStarRating = normHealth > 0.65 ? "★★★" : (normHealth > 0.35 ? "★★☆" : "★☆☆");
                    int nextLevelIdx = CompletedGraspCount % ChallengeTitles.Length;
                    CurrentChallengeLevel = nextLevelIdx + 1;
                    SpatialTactileAudio.Instance?.PlayExportSuccess();

                    // Genuinely advance the mechanism width and topology to match the new Challenge Level!
                    double[] levelWidths = { 70.0, 145.0, 40.0, 210.0, 105.0 };
                    int[] levelTopologies = { 0, 1, 3, 2, 0 };
                    ManualTestWidthMm = levelWidths[nextLevelIdx];
                    _topologyVariantIndex = levelTopologies[nextLevelIdx];
                    ResetPayloadHousingAndEnvelope();
                    SynthesizeAndSpawnSync(ManualTestWidthMm);

                    UpdateReachEnvelopeAndStationPositions(_lastReachMeters);
                    _sampleWorkpiece.localRotation = Quaternion.identity;
                    UpdateSampleWorkpieceVisual(ManualTestWidthMm);
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
