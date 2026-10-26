using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR.Hands;
using KineticCore.Input;
using KineticCore.Kinematics;
using KineticCore.Runtime;

[Preserve]
public class CaliperController : MonoBehaviour
{
    [Header("Direct Bridge to Mechanism Controller (No Reflection)")]
    public KineticMechanismController MechanismController;
    public Transform DeskWorkspaceOrigin;
    public HandFingertipReader FingertipReader;

    [Header("One-Euro Filter & Stability Parameters")]
    public double MinCutoff = 1.0;
    public double Beta = 0.003;
    public double StabilityDriftThresholdMm = 0.5;
    public float StabilityHoldDurationSec = 0.5f;

    // Expose both naming conventions with [Preserve] so neither reflection nor direct calls ever fail
    [Preserve] public double CaliperWidthMm { get; private set; } = 70.0;
    [Preserve] public double WidthMm => CaliperWidthMm;
    [Preserve] public bool IsStable { get; private set; } = false;
    [Preserve] public bool IsMeasuringActive { get; private set; } = false;

    private OneEuroFilter _widthFilter;
    private XRHandSubsystem _handSubsystem;
    private double _anchorWidthMm = 70.0;
    private double _lastSynthesizedWidthMm = -100.0;
    private float _stableTimer = 0f;
    private float _lastAutoSynthTime = -10f;
    private bool _wasStableLastFrame = false;

    // 3D Visual Caliper Markers between the user's real left & right index fingertips
    private GameObject _leftTipMarker;
    private GameObject _rightTipMarker;
    private GameObject _caliperBridgeBar;
    private TextMesh _caliperFloatingLabel;
    private Renderer _leftTipRenderer;
    private Renderer _rightTipRenderer;
    private Renderer _bridgeRenderer;

    private void Awake()
    {
        _widthFilter = new OneEuroFilter(MinCutoff, Beta, 1.0);
    }

    private void Start()
    {
        if (MechanismController == null)
            MechanismController = GetComponent<KineticMechanismController>() ?? FindAnyObjectByType<KineticMechanismController>();

        if (DeskWorkspaceOrigin == null)
        {
            if (MechanismController != null && MechanismController.DeskWorkspaceOrigin != null)
                DeskWorkspaceOrigin = MechanismController.DeskWorkspaceOrigin;
            else
            {
                GameObject found = GameObject.Find("DeskWorkspaceOrigin");
                DeskWorkspaceOrigin = found != null ? found.transform : transform;
            }
        }

        if (FingertipReader == null)
            FingertipReader = FindAnyObjectByType<HandFingertipReader>();

        CreateCaliperVisuals();
    }

    private void CreateCaliperVisuals()
    {
        _leftTipMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _leftTipMarker.name = "LeftFingertip_CaliperMarker";
        Destroy(_leftTipMarker.GetComponent<Collider>());
        _leftTipMarker.transform.localScale = Vector3.one * 0.016f;
        _leftTipRenderer = _leftTipMarker.GetComponent<Renderer>();
        if (MechanismController != null)
            MechanismController.SetColorSafe(_leftTipRenderer, new Color(0.0f, 0.9f, 1.0f));
        _leftTipMarker.SetActive(false);

        _rightTipMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _rightTipMarker.name = "RightFingertip_CaliperMarker";
        Destroy(_rightTipMarker.GetComponent<Collider>());
        _rightTipMarker.transform.localScale = Vector3.one * 0.016f;
        _rightTipRenderer = _rightTipMarker.GetComponent<Renderer>();
        if (MechanismController != null)
            MechanismController.SetColorSafe(_rightTipRenderer, new Color(0.0f, 0.9f, 1.0f));
        _rightTipMarker.SetActive(false);

        _caliperBridgeBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _caliperBridgeBar.name = "Caliper_BridgeBar";
        Destroy(_caliperBridgeBar.GetComponent<Collider>());
        _bridgeRenderer = _caliperBridgeBar.GetComponent<Renderer>();
        if (MechanismController != null)
            MechanismController.SetColorSafe(_bridgeRenderer, new Color(0.0f, 0.9f, 1.0f));
        _caliperBridgeBar.SetActive(false);

        _caliperFloatingLabel = WorldTextFactory.Create(
            "Caliper_FloatingReadout", transform,
            Vector3.zero, Quaternion.identity,
            0.0018f, 36, new Color(0.2f, 1.0f, 0.5f), "70.0 mm");
        _caliperFloatingLabel.gameObject.SetActive(false);
    }

    private void Update()
    {
        bool leftPointing = false, rightPointing = false;
        Vector3 leftWorld = Vector3.zero, rightWorld = Vector3.zero;

        if (FingertipReader != null)
        {
            // Only activate a fingertip when ONLY its index finger is extended straight and Middle/Ring/Pinky are curled!
            leftPointing = FingertipReader.LeftPointingOnly;
            rightPointing = FingertipReader.RightPointingOnly;
            leftWorld = FingertipReader.LeftWorld;
            rightWorld = FingertipReader.RightWorld;
        }
        else if (TryGetBothIndexTipsWorld(out leftWorld, out rightWorld))
        {
            leftPointing = true;
            rightPointing = true;
        }

        // Only show the Left Index Fingertip cyan sphere when the left hand is in the Index-Only Pointing pose
        if (_leftTipMarker != null)
        {
            _leftTipMarker.SetActive(leftPointing);
            if (leftPointing) _leftTipMarker.transform.position = leftWorld;
        }

        if (leftPointing && rightPointing)
        {
            Transform desk = DeskWorkspaceOrigin != null ? DeskWorkspaceOrigin : transform;

            // Transform both fingertips into local desk coordinate frame
            Vector3 leftLocal = desk.InverseTransformPoint(leftWorld);
            Vector3 rightLocal = desk.InverseTransformPoint(rightWorld);

            // Orthogonal projection onto tabletop plane (drop vertical normal axis Y to eliminate tilt hypotenuse bias)
            float dx = rightLocal.x - leftLocal.x;
            float dz = rightLocal.z - leftLocal.z;
            double rawPlanarMeters = System.Math.Sqrt((double)dx * dx + (double)dz * dz);
            double rawWidthMm = rawPlanarMeters * 1000.0;

            // Engage caliper measurement only when BOTH hands are Index-Pointing within caliper range (15mm - 320mm)
            if (rawWidthMm >= 15.0 && rawWidthMm <= 320.0)
            {
                IsMeasuringActive = true;
                CaliperWidthMm = _widthFilter.Filter(rawWidthMm, Time.unscaledTimeAsDouble);

                // Require a deliberate 0.70s steady hold (within ~2.0mm drift) before locking & synthesizing
                double effectiveThresholdMm = System.Math.Max(StabilityDriftThresholdMm, 2.0);
                float effectiveHoldSec = Mathf.Max(StabilityHoldDurationSec, 0.70f);

                double driftMm = System.Math.Abs(CaliperWidthMm - _anchorWidthMm);
                if (driftMm < effectiveThresholdMm)
                {
                    _stableTimer += Time.unscaledDeltaTime;
                    if (_stableTimer >= effectiveHoldSec)
                        IsStable = true;
                }
                else
                {
                    _anchorWidthMm = CaliperWidthMm;
                    _stableTimer = 0f;
                    IsStable = false;
                }

                UpdateCaliperVisualBar(leftWorld, rightWorld, CaliperWidthMm, IsStable);
                PushCaliperToControllerIfStable();
                return;
            }
        }

        IsMeasuringActive = false;
        IsStable = false;
        _stableTimer = 0f;
        _wasStableLastFrame = false;
        HideCaliperVisualBar();
    }

    private void UpdateCaliperVisualBar(Vector3 leftWorld, Vector3 rightWorld, double widthMm, bool stable)
    {
        Color c = stable ? new Color(0.15f, 1.0f, 0.4f) : new Color(0.0f, 0.9f, 1.0f);

        if (_rightTipMarker != null)
        {
            _rightTipMarker.SetActive(true);
            _rightTipMarker.transform.position = rightWorld;
            if (MechanismController != null) MechanismController.SetColorSafe(_rightTipRenderer, c);
        }
        if (_leftTipMarker != null && MechanismController != null)
        {
            MechanismController.SetColorSafe(_leftTipRenderer, c);
        }

        Vector3 mid = (leftWorld + rightWorld) * 0.5f;
        Vector3 delta = rightWorld - leftWorld;
        float dist = delta.magnitude;

        if (_caliperBridgeBar != null && dist > 0.005f)
        {
            _caliperBridgeBar.SetActive(true);
            _caliperBridgeBar.transform.position = mid;
            _caliperBridgeBar.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            _caliperBridgeBar.transform.localScale = new Vector3(0.004f, 0.004f, dist);
            if (MechanismController != null) MechanismController.SetColorSafe(_bridgeRenderer, c);
        }

        if (_caliperFloatingLabel != null)
        {
            _caliperFloatingLabel.gameObject.SetActive(true);
            _caliperFloatingLabel.transform.position = mid + Vector3.up * 0.032f;
            Camera cam = MechanismController != null ? MechanismController.HeadCamera : Camera.main;
            if (cam != null)
            {
                Vector3 toLabel = _caliperFloatingLabel.transform.position - cam.transform.position;
                if (toLabel.sqrMagnitude > 0.001f)
                    _caliperFloatingLabel.transform.rotation = Quaternion.LookRotation(toLabel.normalized, Vector3.up);
            }
            _caliperFloatingLabel.color = c;
            _caliperFloatingLabel.text = stable
                ? $"CALIPER LOCKED: {widthMm:F1} mm"
                : $"CALIPER: {widthMm:F1} mm (Hold Still)";
        }
    }

    private void HideCaliperVisualBar()
    {
        if (_rightTipMarker != null) _rightTipMarker.SetActive(false);
        if (_caliperBridgeBar != null) _caliperBridgeBar.SetActive(false);
        if (_caliperFloatingLabel != null) _caliperFloatingLabel.gameObject.SetActive(false);
    }

    private bool TryGetBothIndexTipsWorld(out Vector3 leftWorld, out Vector3 rightWorld)
    {
        if (FingertipReader != null && FingertipReader.BothPointingOnly)
        {
            leftWorld = FingertipReader.LeftWorld;
            rightWorld = FingertipReader.RightWorld;
            return true;
        }

        if (_handSubsystem == null || !_handSubsystem.running)
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            if (list.Count > 0) _handSubsystem = list[0];
        }

        if (_handSubsystem != null && _handSubsystem.running &&
            _handSubsystem.leftHand.isTracked && _handSubsystem.rightHand.isTracked &&
            HandFingertipReader.IsOpenXrIndexOnlyPointing(_handSubsystem.leftHand) &&
            HandFingertipReader.IsOpenXrIndexOnlyPointing(_handSubsystem.rightHand))
        {
            XRHandJoint lJoint = _handSubsystem.leftHand.GetJoint(XRHandJointID.IndexTip);
            XRHandJoint rJoint = _handSubsystem.rightHand.GetJoint(XRHandJointID.IndexTip);
            if (lJoint.TryGetPose(out Pose lp) && rJoint.TryGetPose(out Pose rp))
            {
                Transform rig = FindRigOriginTransform();
                leftWorld = rig != null ? rig.TransformPoint(lp.position) : lp.position;
                rightWorld = rig != null ? rig.TransformPoint(rp.position) : rp.position;
                return true;
            }
        }

        leftWorld = Vector3.zero;
        rightWorld = Vector3.zero;
        return false;
    }

    private static Transform FindRigOriginTransform()
    {
        GameObject trackingSpace = GameObject.Find("TrackingSpace");
        if (trackingSpace != null) return trackingSpace.transform;
        GameObject xrOrigin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("OVRCameraRig") ?? GameObject.Find("OVRCameraRigInteraction");
        return xrOrigin != null ? xrOrigin.transform : null;
    }

    [Preserve]
    protected void PushCaliperToControllerIfStable()
    {
        if (MechanismController == null)
            MechanismController = FindAnyObjectByType<KineticMechanismController>();

        if (MechanismController != null && CaliperWidthMm >= 15.0 && CaliperWidthMm <= 320.0)
        {
            bool justLocked = IsStable && !_wasStableLastFrame;
            bool significantChange = System.Math.Abs(CaliperWidthMm - _lastSynthesizedWidthMm) > 10.0;
            bool cooldownReady = (Time.unscaledTime - _lastAutoSynthTime) > 3.5f;

            bool shouldTriggerSynth = justLocked && significantChange && cooldownReady;
            if (shouldTriggerSynth)
            {
                _lastSynthesizedWidthMm = CaliperWidthMm;
                _lastAutoSynthTime = Time.unscaledTime;
            }

            MechanismController.SetCaliperWidthMm(CaliperWidthMm, triggerSynthesisNow: shouldTriggerSynth);
        }

        _wasStableLastFrame = IsStable;
    }
}
