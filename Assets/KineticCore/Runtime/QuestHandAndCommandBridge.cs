using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.Scripting;
using KineticCore.Input;

namespace KineticCore.Runtime
{
    /// <summary>
    /// Bridges Quest 3 OpenXR / OVR hands & Touch controllers (and Desktop Mouse Clicks & Dragging in the Editor)
    /// to KineticMechanismController:
    /// 1. Tracks the Right Index Fingertip in 3D space (or Right Touch Controller, or Mouse LMB-drag in the Editor).
    /// 2. Spawns 5 physical 3D Poke-Buttons along the front edge of the desk (clickable by mouse or poked by finger).
    /// 3. Maps Quest Touch buttons / hand pinches to SynthesizeFast, SynthesizeDeep, ExportUrdf, TogglePuppet, ResetPose.
    /// </summary>
    [Preserve]
    public class QuestHandAndCommandBridge : MonoBehaviour
    {
        [Header("Core References")]
        public KineticMechanismController MechanismController;
        public Transform DeskWorkspaceOrigin;
        public Transform TargetTransform;

        [Header("Optional Existing Input Readers")]
        public MonoBehaviour HandFingertipReaderComponent;

        [Header("Desk Poke-Button Settings")]
        public float ButtonPokeRadiusM = 0.052f;
        public float ButtonCooldownSec = 0.55f;

        private XRHandSubsystem _handSubsystem;
        private HandFingertipReader _typedReader;
        private OVRSkeleton _rightOvrSkeleton;
        private OVRHand _rightOvrHand;
        private OVRHand _leftOvrHand;

        private Transform[] _buttonTransforms;
        private Renderer[] _buttonRenderers;
        private Color[] _buttonBaseColors;
        private readonly string[] _buttonActions = { "SynthesizeFast", "SynthesizeDeep", "ExportUrdf", "TogglePuppet", "ResetPose" };
        private readonly string[] _buttonLabels = { "FAST SYNTH\n(Super)", "DEEP AUDIT\n(Ultra)", "EXPORT\nURDF", "TOGGLE\nPUPPET", "RESET\nPOSE" };
        private float _lastPokeTime = -10f;
        private int _lastClickedButtonIdx = -1;
        private int _hoveredButtonIdx = -1;
        private bool _draggingTargetWithMouse = false;
        private GameObject _fingertipReticle;
        private Renderer _reticleRenderer;

        private bool _prevBtnA, _prevBtnB, _prevBtnX, _prevBtnY;
        private MaterialPropertyBlock _mpb;

        private void Start()
        {
            _mpb = new MaterialPropertyBlock();

            if (MechanismController == null)
                MechanismController = GetComponent<KineticMechanismController>();
            if (MechanismController == null)
                MechanismController = FindAnyObjectByType<KineticMechanismController>();

            if (DeskWorkspaceOrigin == null)
            {
                if (MechanismController != null && MechanismController.DeskWorkspaceOrigin != null)
                {
                    DeskWorkspaceOrigin = MechanismController.DeskWorkspaceOrigin;
                }
                else
                {
                    GameObject found = GameObject.Find("DeskWorkspaceOrigin");
                    DeskWorkspaceOrigin = found != null ? found.transform : this.transform;
                }
            }

            if (TargetTransform == null && MechanismController != null)
                TargetTransform = MechanismController.EnsureTargetSphere();
            else if (TargetTransform != null && MechanismController != null && MechanismController.TargetTransform == null)
                MechanismController.TargetTransform = TargetTransform;

            _typedReader = HandFingertipReaderComponent as HandFingertipReader;
            if (_typedReader == null)
                _typedReader = FindAnyObjectByType<HandFingertipReader>();

            SpatialTactileAudio.EnsureInstance(DeskWorkspaceOrigin);
            Create3dDeskPokeButtons();
            CreateFingertipReticle();
        }

        private void CreateFingertipReticle()
        {
            _fingertipReticle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _fingertipReticle.name = "HandsFirst_IndexReticleRing";
            Destroy(_fingertipReticle.GetComponent<Collider>());
            _fingertipReticle.transform.localScale = new Vector3(0.022f, 0.0015f, 0.022f);
            _reticleRenderer = _fingertipReticle.GetComponent<Renderer>();
            SetColorSafe(_reticleRenderer, new Color(0.15f, 0.95f, 1.0f));
            _fingertipReticle.SetActive(false);
        }

        private void Create3dDeskPokeButtons()
        {
            int count = _buttonActions.Length;
            _buttonTransforms = new Transform[count];
            _buttonRenderers = new Renderer[count];
            _buttonBaseColors = new Color[]
            {
                new Color(0.0f, 0.75f, 0.95f), // Fast Synth (Cyan)
                new Color(0.65f, 0.25f, 0.95f), // Deep Audit (Purple)
                new Color(0.15f, 0.85f, 0.35f), // Export URDF (Green)
                new Color(0.95f, 0.65f, 0.10f), // Toggle Puppet (Amber)
                new Color(0.85f, 0.25f, 0.25f)  // Reset Pose (Red)
            };

            GameObject barRoot = new GameObject("VR_Desk_PokeButtonBar");
            barRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            barRoot.transform.localPosition = new Vector3(0.08f, 0.014f, -0.15f);
            barRoot.transform.localRotation = Quaternion.identity;

            for (int i = 0; i < count; i++)
            {
                float xOffset = (i - (count - 1) * 0.5f) * 0.085f;

                GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"VRButton_{_buttonActions[i]}";
                btn.transform.SetParent(barRoot.transform, false);
                btn.transform.localPosition = new Vector3(xOffset, 0f, 0f);
                btn.transform.localScale = new Vector3(0.072f, 0.022f, 0.042f);

                _buttonTransforms[i] = btn.transform;
                _buttonRenderers[i] = btn.GetComponent<Renderer>();
                SetColorSafe(_buttonRenderers[i], _buttonBaseColors[i]);

                WorldTextFactory.Create(
                    $"Label_{_buttonActions[i]}", barRoot.transform,
                    new Vector3(xOffset, 0.025f, 0.0f), Quaternion.Euler(45f, 0f, 0f),
                    0.0022f, 36, Color.white, _buttonLabels[i]);
            }
        }

        private void Update()
        {
            bool isVrActive = !Application.isEditor || XRSettings.isDeviceActive;

            if (isVrActive)
            {
                // Only activate Right Index Fingertip (IK puppet + button poke) when ONLY the right index finger is straight!
                bool hasRightPoint = TryGetRightIndexPointingWorld(out Vector3 rightTipWorld);
                bool hasLeftPoint = (_typedReader != null && _typedReader.LeftPointingOnly && _typedReader.LeftWorld.sqrMagnitude > 0.0001f);

                if (_fingertipReticle != null)
                {
                    if (hasRightPoint || hasLeftPoint)
                    {
                        _fingertipReticle.SetActive(true);
                        Vector3 activeTip = hasRightPoint ? rightTipWorld : _typedReader.LeftWorld;
                        _fingertipReticle.transform.position = activeTip;
                        if (DeskWorkspaceOrigin != null)
                            _fingertipReticle.transform.rotation = DeskWorkspaceOrigin.rotation;
                    }
                    else
                    {
                        _fingertipReticle.SetActive(false);
                    }
                }

                if (hasRightPoint)
                {
                    if (TargetTransform != null)
                    {
                        TargetTransform.position = rightTipWorld;
                    }

                    if (MechanismController != null && DeskWorkspaceOrigin != null)
                    {
                        MechanismController.AutoOrbitTarget = false;
                        MechanismController.ManualTargetLocalPos = DeskWorkspaceOrigin.InverseTransformPoint(rightTipWorld);
                    }

                    CheckDeskButtonPokes(rightTipWorld);
                }

                // Also allow Left Index Fingertip to poke the 5 desk buttons ONLY when left hand is Index-Pointing!
                if (hasLeftPoint)
                {
                    CheckDeskButtonPokes(_typedReader.LeftWorld, resetColorsIfNotPoked: !hasRightPoint);
                }

                PollControllerAndPinchShortcuts();
            }
#if UNITY_EDITOR
            else
            {
                CheckDesktopMouseClickAndDrag();
            }
#endif
        }

        private void CheckDesktopMouseClickAndDrag()
        {
            if (_buttonTransforms == null) return;

            // Restore button color, scale & depression after click flash
            if (_lastClickedButtonIdx >= 0 && (Time.time - _lastPokeTime) > 0.25f)
            {
                SetColorSafe(_buttonRenderers[_lastClickedButtonIdx], _buttonBaseColors[_lastClickedButtonIdx]);
                _buttonTransforms[_lastClickedButtonIdx].localScale = new Vector3(0.072f, 0.022f, 0.042f);
                Vector3 lp = _buttonTransforms[_lastClickedButtonIdx].localPosition;
                _buttonTransforms[_lastClickedButtonIdx].localPosition = new Vector3(lp.x, 0f, lp.z);
                _lastClickedButtonIdx = -1;
            }

            Vector2 mousePos = KInput.MousePosition();
            // Ignore clicks inside the top-left OnGUI control box (485 x 235 px)
            bool insideGuiBox = (mousePos.x <= 485f && (Screen.height - mousePos.y) <= 235f);

            Camera cam = (MechanismController != null && MechanismController.HeadCamera != null)
                ? MechanismController.HeadCamera
                : Camera.main;
            if (cam == null) return;

            if (KInput.GetMouseButtonDown(0) && !insideGuiBox)
            {
                Ray ray = cam.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                {
                    for (int i = 0; i < _buttonTransforms.Length; i++)
                    {
                        if (hit.transform == _buttonTransforms[i])
                        {
                            _lastPokeTime = Time.time;
                            _lastClickedButtonIdx = i;
                            _draggingTargetWithMouse = false;
                            SetColorSafe(_buttonRenderers[i], Color.white);
                            _buttonTransforms[i].localScale = new Vector3(0.078f, 0.016f, 0.048f);
                            Vector3 lp = _buttonTransforms[i].localPosition;
                            _buttonTransforms[i].localPosition = new Vector3(lp.x, -0.005f, lp.z);
                            SpatialTactileAudio.Instance?.PlayPokeClick(_buttonTransforms[i].position);
                            InvokeControllerCommand(_buttonActions[i]);
                            return;
                        }
                    }
                }
                _draggingTargetWithMouse = true;
            }

            if (!KInput.GetMouseButton(0))
            {
                _draggingTargetWithMouse = false;
            }

            if (_draggingTargetWithMouse && !insideGuiBox && MechanismController != null && DeskWorkspaceOrigin != null)
            {
                Ray ray = cam.ScreenPointToRay(mousePos);
                Plane dragPlane = new Plane(DeskWorkspaceOrigin.up, DeskWorkspaceOrigin.TransformPoint(new Vector3(0f, 0.11f, 0f)));
                if (dragPlane.Raycast(ray, out float enter) && enter > 0f && enter < 5f)
                {
                    Vector3 worldHit = ray.GetPoint(enter);
                    Vector3 localHit = DeskWorkspaceOrigin.InverseTransformPoint(worldHit);
                    localHit.x = Mathf.Clamp(localHit.x, -0.18f, 0.38f);
                    localHit.z = Mathf.Clamp(localHit.z, -0.14f, 0.22f);
                    localHit.y = 0.11f;
                    MechanismController.AutoOrbitTarget = false;
                    MechanismController.PuppetActive = true;
                    MechanismController.ManualTargetLocalPos = localHit;
                }
            }
        }

        private bool TryGetRightIndexPointingWorld(out Vector3 worldPos)
        {
            // 1. Check HandFingertipReader first: only active when RightPointingOnly (index straight, Middle/Ring/Pinky curled)
            if (_typedReader != null && _typedReader.RightPointingOnly && _typedReader.RightWorld.sqrMagnitude > 0.0001f)
            {
                worldPos = _typedReader.RightWorld;
                return true;
            }

            // 2. Check Meta OVRHand / OVRSkeleton (Right Hand)
            EnsureOvrHandsBound();
            Transform rigOrigin = FindRigOriginTransform();
            if (HandFingertipReader.TryGetOvrIndexTip(_rightOvrSkeleton, _rightOvrHand, rigOrigin, out Vector3 ovrTip) &&
                HandFingertipReader.IsOvrIndexOnlyPointing(_rightOvrSkeleton))
            {
                worldPos = ovrTip;
                return true;
            }

            // 3. Check OpenXR XRHandSubsystem (joint poses in TrackingSpace)
            if (_handSubsystem == null || !_handSubsystem.running)
            {
                var subsystems = new System.Collections.Generic.List<XRHandSubsystem>();
                SubsystemManager.GetSubsystems(subsystems);
                if (subsystems.Count > 0) _handSubsystem = subsystems[0];
            }

            if (_handSubsystem != null && _handSubsystem.running)
            {
                XRHand rightHand = _handSubsystem.rightHand;
                if (rightHand.isTracked && HandFingertipReader.IsOpenXrIndexOnlyPointing(rightHand))
                {
                    XRHandJoint indexTip = rightHand.GetJoint(XRHandJointID.IndexTip);
                    if (indexTip.TryGetPose(out Pose pose))
                    {
                        worldPos = rigOrigin != null ? rigOrigin.TransformPoint(pose.position) : pose.position;
                        return worldPos.sqrMagnitude > 0.0001f;
                    }
                }
            }

            // 4. Fallback to OVRInput RTouch controller when pointing (holding grip without index trigger)
            if (HandFingertipReader.TryGetOvrInputHandWorld(false, rigOrigin, out Vector3 ovrInputPos, out bool isPointing) && isPointing)
            {
                worldPos = ovrInputPos;
                return true;
            }

            worldPos = Vector3.zero;
            return false;
        }

        private void EnsureOvrHandsBound()
        {
            if (_rightOvrSkeleton != null && _leftOvrHand != null) return;
            foreach (var skel in FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Exclude))
            {
                if (skel.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight ||
                    skel.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight)
                {
                    _rightOvrSkeleton = skel;
                    _rightOvrHand = skel.GetComponent<OVRHand>();
                }
                else if (skel.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft ||
                         skel.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft)
                {
                    _leftOvrHand = skel.GetComponent<OVRHand>();
                }
            }
        }

        private Transform FindRigOriginTransform()
        {
            GameObject trackingSpace = GameObject.Find("TrackingSpace");
            if (trackingSpace != null) return trackingSpace.transform;
            GameObject xrOrigin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("OVRCameraRig") ?? GameObject.Find("OVRCameraRigInteraction");
            return xrOrigin != null ? xrOrigin.transform : null;
        }

        private void CheckDeskButtonPokes(Vector3 fingertipWorld, bool resetColorsIfNotPoked = true)
        {
            if (_buttonTransforms == null) return;
            bool onCooldown = (Time.time - _lastPokeTime) < ButtonCooldownSec;
            float hitRadius = Mathf.Max(ButtonPokeRadiusM, 0.052f);
            float hoverRadius = hitRadius * 1.45f;
            int currentHover = -1;

            for (int i = 0; i < _buttonTransforms.Length; i++)
            {
                Vector3 bPos = _buttonTransforms[i].position;
                // Generous cylindrical/box hit zone around each button (4.8 cm X, 5.5 cm Y, 4.8 cm Z)
                float dx = Mathf.Abs(fingertipWorld.x - bPos.x);
                float dy = Mathf.Abs(fingertipWorld.y - bPos.y);
                float dz = Mathf.Abs(fingertipWorld.z - bPos.z);
                float dist = Vector3.Distance(fingertipWorld, bPos);

                bool isTouching = (dist < hitRadius) || (dx < 0.045f && dy < 0.050f && dz < 0.045f);
                bool isHovering = !isTouching && (dist < hoverRadius);
                if (isHovering) currentHover = i;

                if (!onCooldown && isTouching)
                {
                    _lastPokeTime = Time.time;
                    _lastClickedButtonIdx = i;
                    SetColorSafe(_buttonRenderers[i], Color.white);
                    // Physically depress button into the desk and play tactile mechanical click
                    _buttonTransforms[i].localScale = new Vector3(0.078f, 0.015f, 0.048f);
                    Vector3 lp = _buttonTransforms[i].localPosition;
                    _buttonTransforms[i].localPosition = new Vector3(lp.x, -0.0055f, lp.z);
                    SpatialTactileAudio.Instance?.PlayPokeClick(bPos);
                    InvokeControllerCommand(_buttonActions[i]);
                    return;
                }
                else if (resetColorsIfNotPoked && (i != _lastClickedButtonIdx || (Time.time - _lastPokeTime) > 0.28f))
                {
                    Color targetCol = isHovering ? Color.Lerp(_buttonBaseColors[i], Color.white, 0.35f) : _buttonBaseColors[i];
                    SetColorSafe(_buttonRenderers[i], targetCol);
                    _buttonTransforms[i].localScale = isHovering
                        ? new Vector3(0.075f, 0.024f, 0.045f)
                        : new Vector3(0.072f, 0.022f, 0.042f);
                    Vector3 lp = _buttonTransforms[i].localPosition;
                    _buttonTransforms[i].localPosition = new Vector3(lp.x, 0f, lp.z);
                }
            }

            if (currentHover >= 0 && currentHover != _hoveredButtonIdx)
            {
                SpatialTactileAudio.Instance?.PlayHoverTick(_buttonTransforms[currentHover].position);
            }
            _hoveredButtonIdx = currentHover;
        }

        private void PollControllerAndPinchShortcuts()
        {
            // Poll OVRInput first (Meta Quest native controller button mapping)
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
                {
                    InvokeControllerCommand("SynthesizeFast");
                    return;
                }
                if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
                {
                    InvokeControllerCommand("SynthesizeDeep");
                    return;
                }
                if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
                {
                    InvokeControllerCommand("ExportUrdf");
                    return;
                }
                if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
                {
                    InvokeControllerCommand("ResetPose");
                    return;
                }
            }
            catch { }

            // Also poll UnityEngine.XR.InputDevices (OpenXR controller mapping)
            InputDevice rightCtrl = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            InputDevice leftCtrl = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            if (rightCtrl.isValid && rightCtrl.TryGetFeatureValue(CommonUsages.primaryButton, out bool btnA))
            {
                if (btnA && !_prevBtnA) InvokeControllerCommand("SynthesizeFast");
                _prevBtnA = btnA;
            }

            if (rightCtrl.isValid && rightCtrl.TryGetFeatureValue(CommonUsages.secondaryButton, out bool btnB))
            {
                if (btnB && !_prevBtnB) InvokeControllerCommand("SynthesizeDeep");
                _prevBtnB = btnB;
            }

            if (leftCtrl.isValid && leftCtrl.TryGetFeatureValue(CommonUsages.primaryButton, out bool btnX))
            {
                if (btnX && !_prevBtnX) InvokeControllerCommand("ExportUrdf");
                _prevBtnX = btnX;
            }

            if (leftCtrl.isValid && leftCtrl.TryGetFeatureValue(CommonUsages.secondaryButton, out bool btnY))
            {
                if (btnY && !_prevBtnY) InvokeControllerCommand("ResetPose");
                _prevBtnY = btnY;
            }
        }

        private void InvokeControllerCommand(string actionName)
        {
            if (MechanismController == null) return;

            switch (actionName)
            {
                case "SynthesizeFast": MechanismController.SynthesizeFast(); return;
                case "SynthesizeDeep": MechanismController.SynthesizeDeep(); return;
                case "ExportUrdf":     MechanismController.ExportUrdf();     return;
                case "TogglePuppet":   MechanismController.TogglePuppet();   return;
                case "ResetPose":      MechanismController.ResetPose();      return;
                default: Debug.LogWarning($"[QuestBridge] Unknown command '{actionName}'"); return;
            }
        }

        private void SetColorSafe(Renderer r, Color c)
        {
            if (r == null) return;
            Material solidMat = KineticMechanismController.GetSharedSolidLitMaterial();
            if (solidMat != null && r.sharedMaterial != solidMat)
                r.sharedMaterial = solidMat;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>
    /// Procedural low-latency spatial audio synthesizer for hands-first tactile feedback on Meta Quest 3.
    /// Generates crisp mechanical clicks, caliper ticks, lock chimes, and export confirmations in memory
    /// with zero external audio asset dependencies.
    /// </summary>
    [Preserve]
    public class SpatialTactileAudio : MonoBehaviour
    {
        public static SpatialTactileAudio Instance { get; private set; }

        private AudioSource _source;
        private AudioClip _pokeClickClip;
        private AudioClip _hoverTickClip;
        private AudioClip _caliperTickClip;
        private AudioClip _caliperLockClip;
        private AudioClip _synthChimeClip;
        private AudioClip _exportSuccessClip;

        private const int SampleRate = 44100;

        [Preserve]
        public static SpatialTactileAudio EnsureInstance(Transform parent = null)
        {
            if (Instance != null) return Instance;
            Instance = FindAnyObjectByType<SpatialTactileAudio>();
            if (Instance != null) return Instance;

            GameObject go = new GameObject("KineticCAD_SpatialTactileAudio");
            if (parent != null) go.transform.SetParent(parent, false);
            Instance = go.AddComponent<SpatialTactileAudio>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.35f;
            _source.volume = 0.65f;

            BuildProceduralClips();
        }

        private void BuildProceduralClips()
        {
            _pokeClickClip = CreateClip("PokeClick", 0.035f, (t, norm) =>
            {
                float env = Mathf.Exp(-norm * 14f);
                float tone1 = Mathf.Sin(2f * Mathf.PI * 1150f * t);
                float tone2 = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-norm * 28f);
                return (0.6f * tone1 + 0.4f * tone2) * env;
            });

            _hoverTickClip = CreateClip("HoverTick", 0.016f, (t, norm) =>
            {
                float env = Mathf.Exp(-norm * 22f);
                return Mathf.Sin(2f * Mathf.PI * 1850f * t) * env * 0.35f;
            });

            _caliperTickClip = CreateClip("CaliperTick", 0.018f, (t, norm) =>
            {
                float env = Mathf.Exp(-norm * 20f);
                return Mathf.Sin(2f * Mathf.PI * 2100f * t) * env * 0.40f;
            });

            _caliperLockClip = CreateClip("CaliperLock", 0.18f, (t, norm) =>
            {
                float freq = t < 0.075f ? 1046.5f : 1567.98f;
                float localNorm = t < 0.075f ? (t / 0.075f) : ((t - 0.075f) / 0.105f);
                float env = Mathf.Sin(Mathf.Min(1f, localNorm * 8f) * Mathf.PI * 0.5f) * Mathf.Exp(-localNorm * 4.5f);
                return (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * freq * t)) * env * 0.55f;
            });

            _synthChimeClip = CreateClip("SynthChime", 0.24f, (t, norm) =>
            {
                float freq = t < 0.07f ? 880f : (t < 0.14f ? 1108.73f : 1318.51f);
                float env = Mathf.Exp(-norm * 3.8f);
                return (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * freq * t)) * env * 0.5f;
            });

            _exportSuccessClip = CreateClip("ExportSuccess", 0.28f, (t, norm) =>
            {
                float env = Mathf.Exp(-norm * 4.2f);
                float c6 = Mathf.Sin(2f * Mathf.PI * 1046.50f * t);
                float e6 = Mathf.Sin(2f * Mathf.PI * 1318.51f * t);
                float g6 = Mathf.Sin(2f * Mathf.PI * 1567.98f * t);
                return (c6 + e6 + g6) * 0.25f * env;
            });
        }

        private static AudioClip CreateClip(string clipName, float durationSec, Func<float, float, float> generator)
        {
            int sampleCount = Mathf.Max(64, Mathf.RoundToInt(durationSec * SampleRate));
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float norm = (float)i / sampleCount;
                samples[i] = Mathf.Clamp(generator(t, norm), -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        [Preserve]
        public void PlayPokeClick(Vector3? worldPos = null)
        {
            if (_source == null || _pokeClickClip == null) return;
            if (worldPos.HasValue) transform.position = worldPos.Value;
            _source.PlayOneShot(_pokeClickClip, 0.85f);
        }

        [Preserve]
        public void PlayHoverTick(Vector3? worldPos = null)
        {
            if (_source == null || _hoverTickClip == null) return;
            if (worldPos.HasValue) transform.position = worldPos.Value;
            _source.PlayOneShot(_hoverTickClip, 0.55f);
        }

        [Preserve]
        public void PlayCaliperTick(Vector3? worldPos = null)
        {
            if (_source == null || _caliperTickClip == null) return;
            if (worldPos.HasValue) transform.position = worldPos.Value;
            _source.PlayOneShot(_caliperTickClip, 0.45f);
        }

        [Preserve]
        public void PlayCaliperLock(Vector3? worldPos = null)
        {
            if (_source == null || _caliperLockClip == null) return;
            if (worldPos.HasValue) transform.position = worldPos.Value;
            _source.PlayOneShot(_caliperLockClip, 0.75f);
        }

        [Preserve]
        public void PlaySynthChime()
        {
            if (_source == null || _synthChimeClip == null) return;
            _source.PlayOneShot(_synthChimeClip, 0.70f);
        }

        [Preserve]
        public void PlayExportSuccess()
        {
            if (_source == null || _exportSuccessClip == null) return;
            _source.PlayOneShot(_exportSuccessClip, 0.80f);
        }
    }
}
