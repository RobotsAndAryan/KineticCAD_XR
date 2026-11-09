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
        private Vector3[] _buttonRestLocalPositions;
        private Vector3[] _buttonRestLocalScales;
        private Transform _leftHandPaletteRoot;
        private TextMesh _leftPaletteHeaderLabel;
        private GameObject[] _leftPalettePreviewShapes;
        private Renderer[] _leftPalettePreviewRenderers;

        private readonly string[] _buttonActions =
        {
            // Front Desk Fusion 360 Toolbar (indices 0..6)
            "CycleCadPrimitiveShape",
            "StampNewCadBodyToAssembly",
            "CycleCadFeatureOp",
            "CycleCadPatternArray",
            "ToggleHandInspectMode",
            "ExportUrdf",
            "ResetPose",
            // Left-Hand Spatial Fusion 360 CAD Palette (indices 7..12)
            "CycleCadPrimitiveShape",
            "StampNewCadBodyToAssembly",
            "CycleCadBooleanMode",
            "CycleCadPatternArray",
            "CycleDirectPushPullExtrude",
            "CycleCadMaterial"
        };
        private readonly string[] _buttonLabels =
        {
            // Front Desk Fusion 360 Toolbar (indices 0..6)
            "CREATE SHAPE\n(Box / Cyl / Sphere)",
            "+ADD / CUT BODY\n(Combine Shape)",
            "MODIFY: SHELL\n(Hollow / Chamfer)",
            "PATTERN ARRAY\n(Linear / Circular)",
            "HOLD 1:1 MODEL\n(Inspect in Hand)",
            "EXPORT .STL / .OBJ\n(Save 3D Print)",
            "CENTER DESK\n(Reset View)",
            // Left-Hand Spatial Fusion 360 CAD Palette (indices 7..12)
            "CREATE: SHAPE\n(Box / Cyl / Sphere)",
            "+ADD / CUT BODY\n(Place on Model)",
            "COMBINE MODE\n(Join / Cut Hole)",
            "PATTERN\n(1x / Linear / Circ)",
            "EXTRUDE / PULL\n(Resize W / D / H)",
            "MATERIAL\n(PLA / CF / Al / Ti)"
        };
        private float _lastPokeTime = -10f;
        private float _lastMicrogestureTime = -10f;
        private OVRHand.MicrogestureType _prevRightMicrogesture = OVRHand.MicrogestureType.NoGesture;
        private OVRHand.MicrogestureType _prevLeftMicrogesture = OVRHand.MicrogestureType.NoGesture;
        private int _lastClickedButtonIdx = -1;
        private int _hoveredButtonIdx = -1;
        private int _gazeFocusedButtonIdx = -1;
        private float _gazePinchHoldTimer = 0f;
        private bool _gazePinchTriggeredThisHold = false;
        private bool _draggingTargetWithMouse = false;
        private int _draggedFacePullAxis = -1;
        private GameObject _fingertipReticle;
        private Renderer _reticleRenderer;
        private Transform _cachedRigOrigin;
        private float _nextRigLookupTime = -10f;
        private float _nextOvrBindLookupTime = -10f;

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
            _buttonRestLocalPositions = new Vector3[count];
            _buttonRestLocalScales = new Vector3[count];
            _buttonBaseColors = new Color[]
            {
                // Front Desk Bar (indices 0..6)
                new Color(0.0f, 0.75f, 0.95f), // Synthesize Topology (Cyan)
                new Color(0.95f, 0.65f, 0.12f), // +Snap Module 3..6 DoF (Gold-Amber)
                new Color(0.25f, 0.50f, 0.95f), // CAD Feature Shell/Boss/L-Bracket (Royal Blue)
                new Color(0.10f, 0.85f, 0.72f), // Hold 1:1 Print in Hand (Teal)
                new Color(0.95f, 0.20f, 0.65f), // Ghost Carve CBF (Magenta)
                new Color(0.15f, 0.85f, 0.35f), // Export STL + OBJ + URDF (Emerald Green)
                new Color(0.85f, 0.25f, 0.25f), // Calibrate Desk (Red)
                // Left-Hand Spatial Fusion 360 CAD Palette (indices 7..12)
                new Color(0.05f, 0.82f, 1.00f), // Prim Shape: Cube/Cyl/Sphere (Sky Cyan)
                new Color(0.12f, 0.90f, 0.55f), // +Stamp Body (Emerald)
                new Color(0.95f, 0.22f, 0.52f), // Boolean Join/Cut/Intersect (Crimson-Rose)
                new Color(0.98f, 0.75f, 0.15f), // Pattern 1x/Linear/Circular (Amber Gold)
                new Color(0.55f, 0.35f, 0.95f), // Push/Pull Face Extrude (Violet)
                new Color(0.62f, 0.72f, 0.84f)  // Material PLA+/CF/Al/Ti (Titanium Silver)
            };

            // 1. Front Desk Poke-Button Bar (indices 0..6)
            GameObject barRoot = new GameObject("VR_Desk_PokeButtonBar");
            barRoot.transform.SetParent(DeskWorkspaceOrigin, false);
            barRoot.transform.localPosition = new Vector3(0.08f, 0.014f, -0.15f);
            barRoot.transform.localRotation = Quaternion.identity;

            const int deskBarCount = 7;
            for (int i = 0; i < deskBarCount; i++)
            {
                float xOffset = (i - (deskBarCount - 1) * 0.5f) * 0.068f;
                Vector3 restPos = new Vector3(xOffset, 0f, 0f);
                Vector3 restScale = new Vector3(0.060f, 0.022f, 0.042f);

                GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"VRButton_{_buttonActions[i]}";
                btn.transform.SetParent(barRoot.transform, false);
                btn.transform.localPosition = restPos;
                btn.transform.localScale = restScale;

                _buttonTransforms[i] = btn.transform;
                _buttonRenderers[i] = btn.GetComponent<Renderer>();
                _buttonRestLocalPositions[i] = restPos;
                _buttonRestLocalScales[i] = restScale;
                SetColorSafe(_buttonRenderers[i], _buttonBaseColors[i]);

                WorldTextFactory.Create(
                    $"Label_{_buttonActions[i]}", barRoot.transform,
                    new Vector3(xOffset, 0.025f, 0.0f), Quaternion.Euler(45f, 0f, 0f),
                    0.0018f, 32, Color.white, _buttonLabels[i]);
            }

            // 2. Left-Hand Spatial Fusion 360 CAD Palette (indices 7..12)
            // Built in front-facing XY panel coordinates (-Z faces the user's eyes!) so buttons & text NEVER face away!
            GameObject leftPaletteObj = new GameObject("LeftHand_SpatialFusion_Palette");
            leftPaletteObj.transform.SetParent(DeskWorkspaceOrigin, false);
            leftPaletteObj.transform.localPosition = new Vector3(-0.235f, 0.095f, -0.045f);
            leftPaletteObj.transform.localRotation = Quaternion.Euler(0f, -22f, 0f);
            _leftHandPaletteRoot = leftPaletteObj.transform;

            // Backing slate for the Left-Hand Palette (sits at +Z behind the buttons)
            GameObject slate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slate.name = "LeftPalette_BackingSlate";
            Destroy(slate.GetComponent<Collider>());
            slate.transform.SetParent(_leftHandPaletteRoot, false);
            slate.transform.localPosition = new Vector3(0f, 0.002f, 0.011f);
            slate.transform.localScale = new Vector3(0.215f, 0.130f, 0.006f);
            SetColorSafe(slate.GetComponent<Renderer>(), new Color(0.06f, 0.10f, 0.16f));

            _leftPaletteHeaderLabel = WorldTextFactory.Create(
                "LeftPalette_Header", _leftHandPaletteRoot,
                new Vector3(0f, 0.054f, -0.004f), Quaternion.identity,
                0.00135f, 30, new Color(0.35f, 0.95f, 1.0f),
                "FUSION 360 TOOL PALETTE\n[Create | Combine | Extrude | Pattern]");

            // Create spinning 3D holographic primitive preview shapes above the Left-Hand Palette
            _leftPalettePreviewShapes = new GameObject[4];
            _leftPalettePreviewRenderers = new Renderer[4];
            PrimitiveType[] primTypes = { PrimitiveType.Cube, PrimitiveType.Cylinder, PrimitiveType.Sphere, PrimitiveType.Cylinder };
            Vector3[] primScales =
            {
                new Vector3(0.024f, 0.024f, 0.024f),
                new Vector3(0.024f, 0.013f, 0.024f),
                new Vector3(0.026f, 0.026f, 0.026f),
                new Vector3(0.028f, 0.006f, 0.028f)
            };
            for (int p = 0; p < 4; p++)
            {
                GameObject pObj = GameObject.CreatePrimitive(primTypes[p]);
                pObj.name = $"LeftPalette_PreviewPrim_{p}";
                Destroy(pObj.GetComponent<Collider>());
                pObj.transform.SetParent(_leftHandPaletteRoot, false);
                pObj.transform.localPosition = new Vector3(0f, 0.095f, -0.004f);
                pObj.transform.localScale = primScales[p];
                _leftPalettePreviewRenderers[p] = pObj.GetComponent<Renderer>();
                SetColorSafe(_leftPalettePreviewRenderers[p], new Color(0.15f, 0.90f, 1.0f));
                pObj.SetActive(p == 0);
                _leftPalettePreviewShapes[p] = pObj;
            }

            // 6 CAD Tool Buttons arranged in a 3x2 front-facing XY grid (-Z faces straight toward the user's eyes!)
            for (int k = 0; k < 6; k++)
            {
                int idx = deskBarCount + k;
                int col = k % 3;
                int row = k / 3;
                float px = (col - 1) * 0.064f;
                float py = (row == 0) ? 0.018f : -0.028f;
                Vector3 restPos = new Vector3(px, py, 0f);
                Vector3 restScale = new Vector3(0.056f, 0.036f, 0.016f);

                GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"VRLeftPalette_{_buttonActions[idx]}";
                btn.transform.SetParent(_leftHandPaletteRoot, false);
                btn.transform.localPosition = restPos;
                btn.transform.localScale = restScale;

                _buttonTransforms[idx] = btn.transform;
                _buttonRenderers[idx] = btn.GetComponent<Renderer>();
                _buttonRestLocalPositions[idx] = restPos;
                _buttonRestLocalScales[idx] = restScale;
                SetColorSafe(_buttonRenderers[idx], _buttonBaseColors[idx]);

                WorldTextFactory.Create(
                    $"Label_{_buttonActions[idx]}", _leftHandPaletteRoot,
                    new Vector3(px, py, -0.011f), Quaternion.identity,
                    0.00140f, 28, Color.white, _buttonLabels[idx]);
            }
        }

        private void UpdateLeftHandPaletteAndPreview(bool isVrActive)
        {
            if (_leftHandPaletteRoot == null || DeskWorkspaceOrigin == null) return;

            // Animate spinning 3D holographic primitive preview above the Left-Hand Palette
            int activeShape = MechanismController != null ? (MechanismController.CadPrimitiveShapeIndex % 6) : 0;
            int previewSlot = (activeShape == 1 || activeShape == 4) ? 1 : (activeShape == 2 ? 2 : (activeShape == 3 ? 3 : 0));
            int boolMode = MechanismController != null ? MechanismController.CadBooleanModeIndex : 0;
            Color previewCol = (boolMode == 1)
                ? new Color(1.0f, 0.22f, 0.52f)
                : ((boolMode == 2) ? new Color(1.0f, 0.80f, 0.18f) : new Color(0.15f, 0.92f, 1.0f));

            if (_leftPalettePreviewShapes != null)
            {
                for (int p = 0; p < _leftPalettePreviewShapes.Length; p++)
                {
                    if (_leftPalettePreviewShapes[p] == null) continue;
                    bool show = (p == previewSlot);
                    if (_leftPalettePreviewShapes[p].activeSelf != show)
                        _leftPalettePreviewShapes[p].SetActive(show);
                    if (show)
                    {
                        _leftPalettePreviewShapes[p].transform.localRotation = Quaternion.Euler(22f, Time.time * 55f, 15f);
                        SetColorSafe(_leftPalettePreviewRenderers[p], previewCol);
                    }
                }
            }

            if (_leftPaletteHeaderLabel != null && MechanismController != null)
            {
                int bodies = 1 + MechanismController.StampedBodyCount;
                _leftPaletteHeaderLabel.text =
                    $"FUSION 360 PALETTE ({bodies} Bod{(bodies == 1 ? "y" : "ies")})\n[{MechanismController.ActivePrimitiveShapeName} | {MechanismController.ActiveBooleanModeName} | {MechanismController.ActivePatternModeName}]";
            }

            Camera cam = MechanismController != null && MechanismController.HeadCamera != null ? MechanismController.HeadCamera : Camera.main;

            // Attach palette beside the user's Left Hand when held up in VR; otherwise dock on Left Wing of Desk
            if (isVrActive && _typedReader != null && _typedReader.LeftValid && !(_typedReader.LeftPointingOnly && _typedReader.RightPointingOnly))
            {
                Vector3 leftWorld = _typedReader.LeftWorld;
                Vector3 deskLocal = DeskWorkspaceOrigin.InverseTransformPoint(leftWorld);
                if (deskLocal.y > 0.02f && deskLocal.y < 0.55f)
                {
                    Vector3 targetWorld = leftWorld + DeskWorkspaceOrigin.right * 0.105f + Vector3.up * 0.045f;
                    _leftHandPaletteRoot.position = Vector3.Lerp(_leftHandPaletteRoot.position, targetWorld, Time.deltaTime * 14f);
                    if (cam != null)
                    {
                        // Just like _vrHudTransform: LookRotation(fromEyeToPalette) makes local -Z (buttons & text) face 100% toward the user's eyes!
                        Vector3 fromEyeToPalette = _leftHandPaletteRoot.position - cam.transform.position;
                        if (fromEyeToPalette.sqrMagnitude > 0.001f)
                        {
                            Quaternion lookRot = Quaternion.LookRotation(fromEyeToPalette.normalized, Vector3.up);
                            _leftHandPaletteRoot.rotation = Quaternion.Slerp(_leftHandPaletteRoot.rotation, lookRot, Time.deltaTime * 16f);
                        }
                    }
                    return;
                }
            }

            // Default ergonomic Left-Wing Desk Dock (also billboarded toward the user's eyes!)
            Vector3 dockWorldPos = DeskWorkspaceOrigin.TransformPoint(new Vector3(-0.235f, 0.095f, -0.045f));
            _leftHandPaletteRoot.position = Vector3.Lerp(_leftHandPaletteRoot.position, dockWorldPos, Time.deltaTime * 10f);
            if (cam != null)
            {
                Vector3 fromEyeToDock = _leftHandPaletteRoot.position - cam.transform.position;
                if (fromEyeToDock.sqrMagnitude > 0.001f)
                {
                    Quaternion dockLookRot = Quaternion.LookRotation(fromEyeToDock.normalized, Vector3.up);
                    _leftHandPaletteRoot.rotation = Quaternion.Slerp(_leftHandPaletteRoot.rotation, dockLookRot, Time.deltaTime * 12f);
                }
            }
        }

        private void Update()
        {
            bool isVrActive = !Application.isEditor || XRSettings.isDeviceActive;
            UpdateLeftHandPaletteAndPreview(isVrActive);

            if (isVrActive)
            {
                // Only activate Right Index Fingertip (CAD manipulation + button poke) when ONLY the right index finger is straight!
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
                    if (MechanismController != null && DeskWorkspaceOrigin != null)
                    {
                        Vector3? palettePreviewPos = (_leftHandPaletteRoot != null)
                            ? (Vector3?)_leftHandPaletteRoot.TransformPoint(new Vector3(0f, 0.095f, -0.004f))
                            : null;
                        bool handledDirectCad = MechanismController.TryHandleDirectSpatialCadManipulation(
                            rightTipWorld, true, -1, palettePreviewPos, IsEitherHandPinching());

                        Vector3 deskLocal = DeskWorkspaceOrigin.InverseTransformPoint(rightTipWorld);
                        bool overButtonBar = (deskLocal.z < -0.095f && deskLocal.y < 0.085f) || (deskLocal.x < -0.135f && deskLocal.y < 0.14f);
                        if (!handledDirectCad && !overButtonBar && deskLocal.y > -0.04f)
                        {
                            MechanismController.AutoOrbitTarget = false;
                            MechanismController.ManualTargetLocalPos = deskLocal;
                            if (TargetTransform != null)
                                TargetTransform.position = rightTipWorld;
                        }
                    }

                    CheckDeskButtonPokes(rightTipWorld);
                }
                else
                {
                    MechanismController?.TryHandleDirectSpatialCadManipulation(Vector3.zero, false);
                }

                // Also allow Left Index Fingertip to poke the desk buttons ONLY when left hand is Index-Pointing!
                if (hasLeftPoint)
                {
                    CheckDeskButtonPokes(_typedReader.LeftWorld, resetColorsIfNotPoked: !hasRightPoint);
                }

                PollGazeAndMicroPinch(hasRightPoint || hasLeftPoint);
                PollMetaMicrogestures();
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
                int idx = _lastClickedButtonIdx;
                SetColorSafe(_buttonRenderers[idx], _buttonBaseColors[idx]);
                _buttonTransforms[idx].localScale = _buttonRestLocalScales[idx];
                _buttonTransforms[idx].localPosition = _buttonRestLocalPositions[idx];
                _lastClickedButtonIdx = -1;
            }

            Vector2 mousePos = KInput.MousePosition();
            // Ignore clicks inside the top-left OnGUI control box (575 x 315 px)
            bool insideGuiBox = (mousePos.x <= 575f && (Screen.height - mousePos.y) <= 315f);

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
                            _draggedFacePullAxis = -1;
                            SetColorSafe(_buttonRenderers[i], Color.white);
                            Vector3 rs = _buttonRestLocalScales[i];
                            Vector3 rp = _buttonRestLocalPositions[i];
                            _buttonTransforms[i].localScale = (i >= 7)
                                ? new Vector3(rs.x * 1.06f, rs.y * 1.06f, rs.z * 0.65f)
                                : new Vector3(rs.x * 1.08f, rs.y * 0.72f, rs.z * 1.08f);
                            _buttonTransforms[i].localPosition = rp + ((i >= 7) ? new Vector3(0f, 0f, 0.0045f) : new Vector3(0f, -0.005f, 0f));
                            SpatialTactileAudio.Instance?.PlayPokeClick(_buttonTransforms[i].position);
                            InvokeControllerCommand(_buttonActions[i]);
                            return;
                        }
                    }

                    // Check if user clicked one of the 3 Direct Face-Pull Gizmo Handles (+X, +Y, +Z)
                    if (MechanismController != null && MechanismController.FacePullHandles != null)
                    {
                        for (int a = 0; a < MechanismController.FacePullHandles.Length; a++)
                        {
                            if (MechanismController.FacePullHandles[a] != null && hit.transform == MechanismController.FacePullHandles[a])
                            {
                                _draggedFacePullAxis = a;
                                _draggingTargetWithMouse = false;
                                SpatialTactileAudio.Instance?.PlayPokeClick(hit.point);
                                return;
                            }
                        }
                    }

                    // Check if user clicked any joint's Screw-Axis Pin S_i on the robot arm
                    if (MechanismController != null && MechanismController.PinTransforms != null)
                    {
                        for (int p = 0; p < MechanismController.PinTransforms.Length; p++)
                        {
                            if (MechanismController.PinTransforms[p] != null && hit.transform == MechanismController.PinTransforms[p])
                            {
                                _draggingTargetWithMouse = false;
                                _draggedFacePullAxis = -1;
                                MechanismController.CycleJointScrewAxis(p);
                                return;
                            }
                        }
                    }
                }
                _draggingTargetWithMouse = true;
            }

            if (!KInput.GetMouseButton(0))
            {
                _draggingTargetWithMouse = false;
                if (_draggedFacePullAxis >= 0)
                {
                    _draggedFacePullAxis = -1;
                    MechanismController?.TryHandleDirectSpatialCadManipulation(Vector3.zero, false);
                }
            }

            if (_draggedFacePullAxis >= 0 && !insideGuiBox && MechanismController != null && DeskWorkspaceOrigin != null)
            {
                Ray ray = cam.ScreenPointToRay(mousePos);
                Vector3 handleWorld = (MechanismController.FacePullHandles != null && MechanismController.FacePullHandles[_draggedFacePullAxis] != null)
                    ? MechanismController.FacePullHandles[_draggedFacePullAxis].position
                    : DeskWorkspaceOrigin.TransformPoint(new Vector3(-0.12f, 0.03f, 0.04f));
                Vector3 planeNormal = (_draggedFacePullAxis == 1) ? -cam.transform.forward : DeskWorkspaceOrigin.up;
                Plane facePlane = new Plane(planeNormal, handleWorld);
                if (facePlane.Raycast(ray, out float enterF) && enterF > 0f && enterF < 5f)
                {
                    Vector3 worldHit = ray.GetPoint(enterF);
                    MechanismController.TryHandleDirectSpatialCadManipulation(worldHit, true, _draggedFacePullAxis);
                }
                return;
            }

            if (_draggingTargetWithMouse && !insideGuiBox && MechanismController != null && DeskWorkspaceOrigin != null)
            {
                Ray ray = cam.ScreenPointToRay(mousePos);
                Plane dragPlane = new Plane(DeskWorkspaceOrigin.up, DeskWorkspaceOrigin.TransformPoint(new Vector3(0f, 0.045f, 0f)));
                if (dragPlane.Raycast(ray, out float enter) && enter > 0f && enter < 5f)
                {
                    Vector3 worldHit = ray.GetPoint(enter);
                    bool shiftHeld = KInput.GetKey(KeyCode.LeftShift) || KInput.GetKey(KeyCode.RightShift);
                    Vector3? palettePreviewPos = (_leftHandPaletteRoot != null)
                        ? (Vector3?)_leftHandPaletteRoot.TransformPoint(new Vector3(0f, 0.095f, -0.004f))
                        : null;
                    if (MechanismController.TryHandleDirectSpatialCadManipulation(worldHit, true, -1, palettePreviewPos, shiftHeld))
                        return;

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
            if (Time.unscaledTime < _nextOvrBindLookupTime) return;
            _nextOvrBindLookupTime = Time.unscaledTime + 1.0f;

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
            if (_cachedRigOrigin != null) return _cachedRigOrigin;
            if (Time.unscaledTime < _nextRigLookupTime) return null;
            _nextRigLookupTime = Time.unscaledTime + 1.0f;

            GameObject trackingSpace = GameObject.Find("TrackingSpace");
            if (trackingSpace != null)
            {
                _cachedRigOrigin = trackingSpace.transform;
                return _cachedRigOrigin;
            }
            GameObject xrOrigin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("OVRCameraRig") ?? GameObject.Find("OVRCameraRigInteraction");
            _cachedRigOrigin = xrOrigin != null ? xrOrigin.transform : null;
            return _cachedRigOrigin;
        }

        private void CheckDeskButtonPokes(Vector3 fingertipWorld, bool resetColorsIfNotPoked = true)
        {
            if (_buttonTransforms == null) return;
            bool onCooldown = (Time.time - _lastPokeTime) < ButtonCooldownSec;
            float hitRadius = Mathf.Max(ButtonPokeRadiusM, 0.048f);
            float hoverRadius = hitRadius * 1.45f;
            int currentHover = -1;

            for (int i = 0; i < _buttonTransforms.Length; i++)
            {
                Vector3 bPos = _buttonTransforms[i].position;
                Vector3 rs = _buttonRestLocalScales[i];
                Vector3 rp = _buttonRestLocalPositions[i];

                // Generous cylindrical/box hit zone around each button
                float dx = Mathf.Abs(fingertipWorld.x - bPos.x);
                float dy = Mathf.Abs(fingertipWorld.y - bPos.y);
                float dz = Mathf.Abs(fingertipWorld.z - bPos.z);
                float dist = Vector3.Distance(fingertipWorld, bPos);

                bool isTouching = (dist < hitRadius) || (dx < 0.042f && dy < 0.048f && dz < 0.042f);
                bool isHovering = !isTouching && (dist < hoverRadius);
                if (isHovering) currentHover = i;

                if (!onCooldown && isTouching)
                {
                    _lastPokeTime = Time.time;
                    _lastClickedButtonIdx = i;
                    SetColorSafe(_buttonRenderers[i], Color.white);
                    // Physically depress button and play tactile mechanical click
                    _buttonTransforms[i].localScale = (i >= 7)
                        ? new Vector3(rs.x * 1.06f, rs.y * 1.06f, rs.z * 0.65f)
                        : new Vector3(rs.x * 1.08f, rs.y * 0.70f, rs.z * 1.08f);
                    _buttonTransforms[i].localPosition = rp + ((i >= 7) ? new Vector3(0f, 0f, 0.0045f) : new Vector3(0f, -0.0055f, 0f));
                    SpatialTactileAudio.Instance?.PlayPokeClick(bPos);
                    InvokeControllerCommand(_buttonActions[i]);
                    return;
                }
                else if (resetColorsIfNotPoked && (i != _lastClickedButtonIdx || (Time.time - _lastPokeTime) > 0.28f))
                {
                    Color targetCol = isHovering ? Color.Lerp(_buttonBaseColors[i], Color.white, 0.35f) : _buttonBaseColors[i];
                    SetColorSafe(_buttonRenderers[i], targetCol);
                    _buttonTransforms[i].localScale = isHovering
                        ? new Vector3(rs.x * 1.05f, rs.y * 1.08f, rs.z * 1.05f)
                        : rs;
                    _buttonTransforms[i].localPosition = rp;
                }
            }

            if (currentHover >= 0 && currentHover != _hoveredButtonIdx)
            {
                SpatialTactileAudio.Instance?.PlayHoverTick(_buttonTransforms[currentHover].position);
            }
            _hoveredButtonIdx = currentHover;
        }

        /// <summary>
        /// "Eyes & Hands" seated mode: allows looking at any 3D desk button or Left-Hand CAD Palette button
        /// (ResetPose excluded to prevent accidental workspace resets) and holding a thumb + index pinch for 0.26s.
        /// </summary>
        private void PollGazeAndMicroPinch(bool fingerPokeActive)
        {
            if (_buttonTransforms == null || MechanismController == null) return;
            Camera cam = MechanismController.HeadCamera != null ? MechanismController.HeadCamera : Camera.main;
            if (cam == null) return;

            Vector3 eyePos = cam.transform.position;
            Vector3 gazeDir = cam.transform.forward;
            int bestGazeIdx = -1;
            float bestAngle = 6.2f; // Tight 6.2-degree FoV gaze cone for deliberate button selection

            for (int i = 0; i < _buttonTransforms.Length; i++)
            {
                // Exclude Calibrate Desk / ResetPose (index 6) from gaze-pinch so resting hands never reset the desk anchor
                if (_buttonActions[i] == "ResetPose") continue;

                Vector3 toBtn = _buttonTransforms[i].position - eyePos;
                if (toBtn.sqrMagnitude < 0.001f) continue;
                float angle = Vector3.Angle(gazeDir, toBtn.normalized);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestGazeIdx = i;
                }
            }

            bool isPinching = IsEitherHandPinching();
            if (!isPinching || bestGazeIdx < 0 || bestGazeIdx != _gazeFocusedButtonIdx)
            {
                _gazePinchHoldTimer = 0f;
                if (!isPinching) _gazePinchTriggeredThisHold = false;
            }
            else if (!_gazePinchTriggeredThisHold)
            {
                _gazePinchHoldTimer += Time.unscaledDeltaTime;
            }

            float dwellProgress = Mathf.Clamp01(_gazePinchHoldTimer / 0.26f);

            if (!fingerPokeActive)
            {
                for (int i = 0; i < _buttonTransforms.Length; i++)
                {
                    if (i == _lastClickedButtonIdx && (Time.time - _lastPokeTime) <= 0.28f) continue;
                    Vector3 rs = _buttonRestLocalScales[i];
                    Vector3 rp = _buttonRestLocalPositions[i];
                    bool gazeFocus = (i == bestGazeIdx);
                    float highlight = gazeFocus ? (0.30f + 0.55f * dwellProgress) : 0f;
                    SetColorSafe(_buttonRenderers[i], gazeFocus ? Color.Lerp(_buttonBaseColors[i], Color.white, highlight) : _buttonBaseColors[i]);
                    _buttonTransforms[i].localScale = gazeFocus
                        ? ((i >= 7)
                            ? new Vector3(rs.x * (1.04f + 0.04f * dwellProgress), rs.y * (1.04f + 0.04f * dwellProgress), rs.z * (1.0f - 0.22f * dwellProgress))
                            : new Vector3(rs.x * (1.04f + 0.05f * dwellProgress), rs.y * (1.05f - 0.22f * dwellProgress), rs.z * (1.04f + 0.05f * dwellProgress)))
                        : rs;
                    _buttonTransforms[i].localPosition = rp + (gazeFocus
                        ? ((i >= 7) ? new Vector3(0f, 0f, 0.003f * dwellProgress) : new Vector3(0f, -0.003f * dwellProgress, 0f))
                        : Vector3.zero);
                }

                if (bestGazeIdx >= 0 && bestGazeIdx != _gazeFocusedButtonIdx)
                {
                    SpatialTactileAudio.Instance?.PlayHoverTick(_buttonTransforms[bestGazeIdx].position);
                }
            }
            _gazeFocusedButtonIdx = bestGazeIdx;

            if (!_gazePinchTriggeredThisHold && _gazePinchHoldTimer >= 0.26f && bestGazeIdx >= 0 && (Time.time - _lastPokeTime) >= ButtonCooldownSec)
            {
                _gazePinchTriggeredThisHold = true;
                _gazePinchHoldTimer = 0f;
                _lastPokeTime = Time.time;
                _lastClickedButtonIdx = bestGazeIdx;
                Vector3 rs = _buttonRestLocalScales[bestGazeIdx];
                Vector3 rp = _buttonRestLocalPositions[bestGazeIdx];
                SetColorSafe(_buttonRenderers[bestGazeIdx], Color.white);
                _buttonTransforms[bestGazeIdx].localScale = (bestGazeIdx >= 7)
                    ? new Vector3(rs.x * 1.06f, rs.y * 1.06f, rs.z * 0.65f)
                    : new Vector3(rs.x * 1.08f, rs.y * 0.70f, rs.z * 1.08f);
                _buttonTransforms[bestGazeIdx].localPosition = rp + ((bestGazeIdx >= 7) ? new Vector3(0f, 0f, 0.0045f) : new Vector3(0f, -0.0055f, 0f));
                SpatialTactileAudio.Instance?.PlayPokeClick(_buttonTransforms[bestGazeIdx].position);
                InvokeControllerCommand(_buttonActions[bestGazeIdx]);
            }
        }

        private bool IsEitherHandPinching()
        {
            EnsureOvrHandsBound();
            try
            {
                if (_rightOvrHand != null && _rightOvrHand.IsTracked && _rightOvrHand.GetFingerIsPinching(OVRHand.HandFinger.Index))
                    return true;
                if (_leftOvrHand != null && _leftOvrHand.IsTracked && _leftOvrHand.GetFingerIsPinching(OVRHand.HandFinger.Index))
                    return true;
            }
            catch { }

            if (_handSubsystem != null && _handSubsystem.running)
            {
                if (IsXrHandPinching(_handSubsystem.rightHand) || IsXrHandPinching(_handSubsystem.leftHand))
                    return true;
            }
            return false;
        }

        private static bool IsXrHandPinching(XRHand hand)
        {
            if (!hand.isTracked) return false;
            XRHandJoint thumb = hand.GetJoint(XRHandJointID.ThumbTip);
            XRHandJoint index = hand.GetJoint(XRHandJointID.IndexTip);
            if (thumb.TryGetPose(out Pose tp) && index.TryGetPose(out Pose ip))
            {
                return Vector3.Distance(tp.position, ip.position) < 0.021f;
            }
            return false;
        }

        private void PollControllerAndPinchShortcuts()
        {
            // Poll OVRInput first (Meta Quest native controller button mapping)
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
                {
                    InvokeControllerCommand("CycleCadPrimitiveShape");
                    return;
                }
                if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
                {
                    InvokeControllerCommand("StampNewCadBodyToAssembly");
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
                if (btnA && !_prevBtnA) InvokeControllerCommand("CycleCadPrimitiveShape");
                _prevBtnA = btnA;
            }

            if (rightCtrl.isValid && rightCtrl.TryGetFeatureValue(CommonUsages.secondaryButton, out bool btnB))
            {
                if (btnB && !_prevBtnB) InvokeControllerCommand("StampNewCadBodyToAssembly");
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

        /// <summary>
        /// Native Meta Quest 3 OpenXR Microgestures (OVRHand.GetMicrogestureType):
        /// - SwipeLeft / SwipeRight: Left Hand cycles CREATE SHAPE; Right Hand resizes Width by -5mm / +5mm.
        /// - SwipeForward: Adds / Cuts active shape into the 3D CAD Model (+ADD / CUT BODY).
        /// - SwipeBackward: Left Hand cycles PATTERN; Right Hand cycles MODIFY: SHELL / CHAMFER.
        /// - ThumbTap: Left Hand toggles COMBINE MODE (Join / Cut Hole); Right Hand exports .STL / .OBJ.
        /// </summary>
        private void PollMetaMicrogestures()
        {
            if (MechanismController == null) return;
            EnsureOvrHandsBound();

            CheckSingleHandMicrogesture(_rightOvrHand, ref _prevRightMicrogesture);
            CheckSingleHandMicrogesture(_leftOvrHand, ref _prevLeftMicrogesture);
        }

        private void CheckSingleHandMicrogesture(OVRHand hand, ref OVRHand.MicrogestureType prevGesture)
        {
            if (hand == null || !hand.IsTracked)
            {
                prevGesture = OVRHand.MicrogestureType.NoGesture;
                return;
            }

            OVRHand.MicrogestureType mg = OVRHand.MicrogestureType.NoGesture;
            try
            {
                mg = hand.GetMicrogestureType();
            }
            catch
            {
                return;
            }

            if (mg != prevGesture && mg != OVRHand.MicrogestureType.NoGesture && mg != OVRHand.MicrogestureType.Invalid)
            {
                if ((Time.unscaledTime - _lastMicrogestureTime) >= 0.30f)
                {
                    _lastMicrogestureTime = Time.unscaledTime;
                    bool isLeft = (hand == _leftOvrHand);
                    switch (mg)
                    {
                        case OVRHand.MicrogestureType.SwipeLeft:
                            if (isLeft) { PulseButtonVisual(7); MechanismController.CycleCadPrimitiveShape(); }
                            else        { PulseButtonVisual(0); MechanismController.ScrubDimensionMm(-5.0); }
                            break;
                        case OVRHand.MicrogestureType.SwipeRight:
                            if (isLeft) { PulseButtonVisual(7); MechanismController.CycleCadPrimitiveShape(); }
                            else        { PulseButtonVisual(0); MechanismController.ScrubDimensionMm(+5.0); }
                            break;
                        case OVRHand.MicrogestureType.SwipeForward:
                            if (isLeft) { PulseButtonVisual(8); MechanismController.StampNewCadBodyToAssembly(); }
                            else        { PulseButtonVisual(1); MechanismController.StampNewCadBodyToAssembly(); }
                            break;
                        case OVRHand.MicrogestureType.SwipeBackward:
                            if (isLeft) { PulseButtonVisual(10); MechanismController.CycleCadPatternArray(); }
                            else        { PulseButtonVisual(2);  MechanismController.CycleCadFeatureOp(); }
                            break;
                        case OVRHand.MicrogestureType.ThumbTap:
                            if (isLeft) { PulseButtonVisual(9); MechanismController.CycleCadBooleanMode(); }
                            else        { PulseButtonVisual(5); MechanismController.ExportUrdf(); }
                            break;
                    }
                }
            }
            prevGesture = mg;
        }

        private void PulseButtonVisual(int idx)
        {
            if (_buttonTransforms == null || idx < 0 || idx >= _buttonTransforms.Length) return;
            _lastPokeTime = Time.time;
            _lastClickedButtonIdx = idx;
            Vector3 rs = _buttonRestLocalScales[idx];
            Vector3 rp = _buttonRestLocalPositions[idx];
            SetColorSafe(_buttonRenderers[idx], Color.white);
            _buttonTransforms[idx].localScale = (idx >= 7)
                ? new Vector3(rs.x * 1.06f, rs.y * 1.06f, rs.z * 0.65f)
                : new Vector3(rs.x * 1.08f, rs.y * 0.70f, rs.z * 1.08f);
            _buttonTransforms[idx].localPosition = rp + ((idx >= 7) ? new Vector3(0f, 0f, 0.0045f) : new Vector3(0f, -0.0055f, 0f));
            SpatialTactileAudio.Instance?.PlayPokeClick(_buttonTransforms[idx].position);
        }

        private void InvokeControllerCommand(string actionName)
        {
            if (MechanismController == null) return;

            switch (actionName)
            {
                case "SynthesizeFast":             MechanismController.SynthesizeFast();             return;
                case "SnapAddModularJointAndLink": MechanismController.SnapAddModularJointAndLink(); return;
                case "CycleCadPrimitiveShape":     MechanismController.CycleCadPrimitiveShape();     return;
                case "StampNewCadBodyToAssembly":  MechanismController.StampNewCadBodyToAssembly();  return;
                case "CycleCadBooleanMode":        MechanismController.CycleCadBooleanMode();        return;
                case "CycleCadPatternArray":       MechanismController.CycleCadPatternArray();       return;
                case "CycleDirectPushPullExtrude": MechanismController.CycleDirectPushPullExtrude(); return;
                case "CycleCadFeatureOp":          MechanismController.CycleCadFeatureOp();          return;
                case "CycleCadMaterial":           MechanismController.CycleCadMaterial();           return;
                case "ToggleHandInspectMode":      MechanismController.ToggleHandInspectMode();      return;
                case "ToggleEnvelopeRecording":    MechanismController.ToggleEnvelopeRecording();    return;
                case "ExecuteGhostCarve":          MechanismController.ExecuteGhostCarve();          return;
                case "SynthesizeDeep":             MechanismController.SynthesizeDeep();             return;
                case "ExportUrdf":                 MechanismController.ExportUrdf();                 return;
                case "TogglePuppet":               MechanismController.TogglePuppet();               return;
                case "ResetPose":                  MechanismController.ResetPose();                  return;
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
