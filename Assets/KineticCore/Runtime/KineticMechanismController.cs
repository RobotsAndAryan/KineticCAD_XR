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
        private Renderer _targetRenderer;
        private Transform _sampleWorkpiece;
        private Transform _vrHudTransform;
        private TextMesh _vrHologramText;
        private TextMesh _workpieceLabel;
        private Camera _headCamera;
        private MaterialPropertyBlock _mpb;
        private bool _hasCenteredToHmd = false;
        private bool _mrukTableLocked = false;
        private float _vrStartupTimer = 0f;

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
                : "VR: poke desk buttons | A synth | B audit | X URDF | Y reset";

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

            // Ensure Meta Quest 3 Insight Passthrough is actively enabled
            if (!IsFlatEditorMode)
            {
                try
                {
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
            StatusMessage = $"{sourceLabel} -> Desk Anchored";
            WriteDiagnosticState("NotifyDeskAnchored");
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
            StatusMessage = "RESET POSE -> Desk Centered & Home [0.25, 0.45, -0.65] rad";
            WriteDiagnosticState("ResetPose");
        }

        [Preserve]
        public void SynthesizeFast()
        {
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
            SetColorSafe(wp.GetComponent<Renderer>(), new Color(0.18f, 0.55f, 0.72f));

            _workpieceLabel = WorldTextFactory.Create(
                "Workpiece_SizeLabel", DeskWorkspaceOrigin,
                new Vector3(-0.13f, 0.028f, 0.04f), Quaternion.Euler(35f, 0f, 0f),
                0.0016f, 34, new Color(0.55f, 0.92f, 1.0f), $"TARGET OBJECT\n{ManualTestWidthMm:F0} mm");

            UpdateSampleWorkpieceVisual(ManualTestWidthMm);
        }

        private void UpdateSampleWorkpieceVisual(double widthMm)
        {
            if (_sampleWorkpiece == null) return;
            float wMeters = Mathf.Clamp((float)(widthMm * 0.001), 0.02f, 0.25f);
            _sampleWorkpiece.localPosition = new Vector3(-0.13f, 0.006f, 0.04f);
            _sampleWorkpiece.localScale = new Vector3(wMeters, 0.010f, 0.024f);
            if (_workpieceLabel != null)
            {
                _workpieceLabel.text = $"TARGET OBJECT\n{widthMm:F0} mm";
            }
        }

        private void CreateVrHologramHud()
        {
            TextMesh tm = WorldTextFactory.Create(
                "VR_3D_Hologram_HUD", DeskWorkspaceOrigin,
                new Vector3(0.08f, 0.24f, 0.08f), Quaternion.identity,
                0.0020f, 36, new Color(0.15f, 0.95f, 1.0f), "KineticCAD Physical AI Active");
            _vrHologramText = tm;
            _vrHudTransform = tm.transform;
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
            string specTag = LastSpecFromAi ? "[AI Spec ✓]" : "[Parametric]";
            _vrHologramText.text =
                $"KINETIC-CAD XR | NVIDIA NEMOTRON ({ManualTestWidthMm:F0}mm)  {(PuppetActive ? "LIVE PUPPET" : "LOCKED")}\n" +
                $"Model: {ActiveNebiusModel} ({LastApiLatencyMs}ms) {specTag} | 60Hz UDP :9870\n" +
                $"J1={_theta[0] * Mathf.Rad2Deg:F1}°  J2={_theta[1] * Mathf.Rad2Deg:F1}°  J3={_theta[2] * Mathf.Rad2Deg:F1}°\n" +
                $"μ={CurrentManipulability:E2} | {EngineeringAuditReport}\n" +
                $"{StatusMessage}";
        }

        // ------------------------------------------------------------------ Synthesis / audit / export

        public async void TriggerAsyncSynthesis(double widthMm)
        {
            if (IsSynthesisInFlight) { StatusMessage = "Synthesis already running..."; return; }
            IsSynthesisInFlight = true;
            try
            {
                UpdateSampleWorkpieceVisual(widthMm);
                StatusMessage = $"Synthesizing for {widthMm:F0}mm object via Nebius Super...";
                var synth = new NebiusKinematicSynthesizer();
                MechanismSpec spec = await synth.SynthesizeMechanismAsync(widthMm, UserIntentPrompt);
                if (this == null) return;
                ActiveNebiusModel = synth.LastModelUsed;
                LastSpecSource = synth.LastSpecSource;
                LastSpecFromAi = synth.LastSpecFromAi;
                LastApiLatencyMs = synth.LastLatencyMs;
                ApplyMechanismSpec(spec);
                _spawnAnimTimer = 1.0f;
                string tag = LastSpecFromAi ? "AI spec" : "parametric fallback";
                StatusMessage = $"FAST SYNTH: Spawned '{spec.robotName}' ({tag}, {LastApiLatencyMs}ms)";
                WriteDiagnosticState("SynthesizeFast");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusMessage = "Synthesis failed - kept previous mechanism";
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
            MechanismSpec spec = NebiusKinematicSynthesizer.BuildDeterministicMechanism(widthMm);
            ApplyMechanismSpec(spec);
        }

        public void ExportCurrentUrdf()
        {
            if (CurrentSpec == null) return;
            try
            {
                LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, Application.persistentDataPath);
                if (Application.isEditor)
                {
                    try
                    {
                        DirectoryInfo rootDir = Directory.GetParent(Application.dataPath);
                        if (rootDir != null)
                        {
                            string exportsDir = Path.Combine(rootDir.FullName, "Exports");
                            LastExportedUrdfPath = UrdfSerializer.SaveUrdfToFile(CurrentSpec, exportsDir);
                        }
                    }
                    catch { }
                }
                _exportFlashTimer = 0.8f; // Flash robot links bright emerald green to confirm URDF export!
                StatusMessage = $"URDF EXPORTED -> {Path.GetFileName(LastExportedUrdfPath)} (ROS 2 & Isaac Sim Ready)";
                Debug.Log($"[KineticCAD] URDF Exported to: {LastExportedUrdfPath}");
                WriteDiagnosticState("ExportUrdf");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                StatusMessage = "URDF export failed (see log)";
            }
        }

        private void ApplyMechanismSpec(MechanismSpec spec)
        {
            CurrentSpec = spec;
            CurrentChain = spec.ToPoEChain(out _minLimits, out _maxLimits);
            _theta = new double[CurrentChain.Joints.Length];
            for (int i = 0; i < _theta.Length && i < HomeTheta.Length; i++)
                _theta[i] = HomeTheta[i];

            ReferenceManipulability = System.Math.Max(1e-5, DlsInverseKinematics.ComputeManipulabilityIndex(CurrentChain, _theta));
            RebuildVisualHierarchy(spec);
            CurrentManipulability = ReferenceManipulability;
            CurrentEndEffectorPos = CurrentChain.EndEffectorPosition(_theta);
            UpdateVisualLinksFromPoE();
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
            tip.transform.localScale = Vector3.one * 0.024f;
            SetColorSafe(tip.GetComponent<Renderer>(), new Color(0.2f, 1.0f, 0.4f));
            _eeMarker = tip.transform;
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

            for (int i = 0; i < jointTransforms.Length && i < _jointPivots.Length; i++)
            {
                Vec3 pos = jointTransforms[i].GetTranslation();
                jointTransforms[i].ToQuaternion(out double qx, out double qy, out double qz, out double qw);
                _jointPivots[i].localPosition = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                _jointPivots[i].localRotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
                if (_linkRenderers[i] != null)
                    SetColorSafe(_linkRenderers[i], linkColor);
            }
            if (_eeMarker != null)
                _eeMarker.localPosition = new Vector3((float)CurrentEndEffectorPos.X, (float)CurrentEndEffectorPos.Y, (float)CurrentEndEffectorPos.Z);
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
