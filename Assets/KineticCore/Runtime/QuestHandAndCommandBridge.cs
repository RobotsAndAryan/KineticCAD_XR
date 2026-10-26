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
        private bool _draggingTargetWithMouse = false;

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

            Create3dDeskPokeButtons();
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
                if (_typedReader != null && _typedReader.LeftPointingOnly && _typedReader.LeftWorld.sqrMagnitude > 0.0001f)
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

            // Restore button color & scale after click flash
            if (_lastClickedButtonIdx >= 0 && (Time.time - _lastPokeTime) > 0.25f)
            {
                SetColorSafe(_buttonRenderers[_lastClickedButtonIdx], _buttonBaseColors[_lastClickedButtonIdx]);
                _buttonTransforms[_lastClickedButtonIdx].localScale = new Vector3(0.072f, 0.022f, 0.042f);
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
                            _buttonTransforms[i].localScale = new Vector3(0.078f, 0.030f, 0.048f);
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

            for (int i = 0; i < _buttonTransforms.Length; i++)
            {
                Vector3 bPos = _buttonTransforms[i].position;
                // Generous cylindrical/box hit zone around each button (4.8 cm X, 5.5 cm Y, 4.8 cm Z)
                float dx = Mathf.Abs(fingertipWorld.x - bPos.x);
                float dy = Mathf.Abs(fingertipWorld.y - bPos.y);
                float dz = Mathf.Abs(fingertipWorld.z - bPos.z);
                float dist = Vector3.Distance(fingertipWorld, bPos);

                bool isTouching = (dist < hitRadius) || (dx < 0.045f && dy < 0.050f && dz < 0.045f);

                if (!onCooldown && isTouching)
                {
                    _lastPokeTime = Time.time;
                    _lastClickedButtonIdx = i;
                    SetColorSafe(_buttonRenderers[i], Color.white);
                    _buttonTransforms[i].localScale = new Vector3(0.080f, 0.032f, 0.050f);
                    InvokeControllerCommand(_buttonActions[i]);
                    return;
                }
                else if (resetColorsIfNotPoked && (i != _lastClickedButtonIdx || (Time.time - _lastPokeTime) > 0.28f))
                {
                    SetColorSafe(_buttonRenderers[i], _buttonBaseColors[i]);
                    _buttonTransforms[i].localScale = new Vector3(0.072f, 0.022f, 0.042f);
                }
            }
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
}
