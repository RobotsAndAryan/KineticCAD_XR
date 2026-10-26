using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace KineticCore.Input
{
    [Preserve]
    public class HandFingertipReader : MonoBehaviour
    {
        [SerializeField] private Transform xrOrigin;

        public Vector3 LeftWorld  { get; private set; }
        public Vector3 RightWorld { get; private set; }
        public bool    LeftValid  { get; private set; }
        public bool    RightValid { get; private set; }
        public bool    BothValid  { get; private set; }
        public bool    LeftPointingOnly  { get; private set; }
        public bool    RightPointingOnly { get; private set; }
        public bool    BothPointingOnly  { get; private set; }

        private XRHandSubsystem _subsystem;
        private readonly List<XRHandSubsystem> _subsystemList = new List<XRHandSubsystem>();
        private OVRSkeleton _leftOvrSkeleton;
        private OVRSkeleton _rightOvrSkeleton;
        private OVRHand _leftOvrHand;
        private OVRHand _rightOvrHand;
        private Transform _trackingSpace;

        private void Awake()
        {
            EnsureOrigin();
        }

        private void OnEnable()
        {
            TryBindSubsystem();
        }

        private void OnDisable()
        {
            if (_subsystem != null)
            {
                _subsystem.updatedHands -= OnHandsUpdated;
                _subsystem = null;
            }
        }

        private void Update()
        {
            EnsureOrigin();

            if (_subsystem == null || !_subsystem.running)
                TryBindSubsystem();

            bool leftOk = false, rightOk = false;
            bool leftPoint = false, rightPoint = false;

            // 1. Primary: Meta OVRSkeleton / OVRHand (native OVRCameraRig bone hierarchy, never double-transformed)
            EnsureOvrHandsBound();
            if (TryGetOvrIndexTip(_leftOvrSkeleton, _leftOvrHand, _trackingSpace, out Vector3 ovrL))
            {
                LeftWorld = ovrL;
                leftOk = true;
                leftPoint = IsOvrIndexOnlyPointing(_leftOvrSkeleton);
            }
            if (TryGetOvrIndexTip(_rightOvrSkeleton, _rightOvrHand, _trackingSpace, out Vector3 ovrR))
            {
                RightWorld = ovrR;
                rightOk = true;
                rightPoint = IsOvrIndexOnlyPointing(_rightOvrSkeleton);
            }

            // 2. Secondary: OpenXR XRHandSubsystem (joint poses in TrackingSpace)
            if (_subsystem != null && _subsystem.running)
            {
                if (_subsystem.leftHand.isTracked)
                {
                    if (!leftOk)
                    {
                        var joint = _subsystem.leftHand.GetJoint(XRHandJointID.IndexTip);
                        if (joint.TryGetPose(out Pose p))
                        {
                            LeftWorld = _trackingSpace != null ? _trackingSpace.TransformPoint(p.position) : p.position;
                            leftOk = LeftWorld.sqrMagnitude > 0.0001f;
                        }
                    }
                    if (!leftPoint)
                    {
                        leftPoint = IsOpenXrIndexOnlyPointing(_subsystem.leftHand);
                    }
                }
                if (_subsystem.rightHand.isTracked)
                {
                    if (!rightOk)
                    {
                        var joint = _subsystem.rightHand.GetJoint(XRHandJointID.IndexTip);
                        if (joint.TryGetPose(out Pose p))
                        {
                            RightWorld = _trackingSpace != null ? _trackingSpace.TransformPoint(p.position) : p.position;
                            rightOk = RightWorld.sqrMagnitude > 0.0001f;
                        }
                    }
                    if (!rightPoint)
                    {
                        rightPoint = IsOpenXrIndexOnlyPointing(_subsystem.rightHand);
                    }
                }
            }

            // 3. Fallback: OVRInput LHand/RHand or LTouch/RTouch local controller position + finger offset
            if (!leftOk && TryGetOvrInputHandWorld(true, _trackingSpace, out Vector3 ctrlL, out bool ctrlLPoint))
            {
                LeftWorld = ctrlL;
                leftOk = true;
                leftPoint = ctrlLPoint;
            }
            if (!rightOk && TryGetOvrInputHandWorld(false, _trackingSpace, out Vector3 ctrlR, out bool ctrlRPoint))
            {
                RightWorld = ctrlR;
                rightOk = true;
                rightPoint = ctrlRPoint;
            }

            LeftValid = leftOk;
            RightValid = rightOk;
            BothValid = leftOk && rightOk;
            LeftPointingOnly = leftOk && leftPoint;
            RightPointingOnly = rightOk && rightPoint;
            BothPointingOnly = LeftPointingOnly && RightPointingOnly;
        }

        private void EnsureOrigin()
        {
            if (_trackingSpace == null)
            {
                GameObject ts = GameObject.Find("TrackingSpace") ?? GameObject.Find("OVRCameraRigInteraction") ?? GameObject.Find("OVRCameraRig");
                if (ts != null) _trackingSpace = ts.transform;
            }
            if (xrOrigin == null || xrOrigin.name != "TrackingSpace")
            {
                if (_trackingSpace != null) xrOrigin = _trackingSpace;
            }
        }

        private void EnsureOvrHandsBound()
        {
            if (_leftOvrSkeleton != null && _rightOvrSkeleton != null) return;
            foreach (var skel in FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Exclude))
            {
                if (skel.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft ||
                    skel.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft)
                {
                    _leftOvrSkeleton = skel;
                    _leftOvrHand = skel.GetComponent<OVRHand>();
                }
                else if (skel.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight ||
                         skel.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight)
                {
                    _rightOvrSkeleton = skel;
                    _rightOvrHand = skel.GetComponent<OVRHand>();
                }
            }
        }

        private void TryBindSubsystem()
        {
            _subsystemList.Clear();
            SubsystemManager.GetSubsystems(_subsystemList);
            foreach (var sub in _subsystemList)
            {
                if (sub != null && sub.running)
                {
                    if (_subsystem != null)
                        _subsystem.updatedHands -= OnHandsUpdated;
                    _subsystem = sub;
                    _subsystem.updatedHands += OnHandsUpdated;
                    break;
                }
            }
        }

        private void OnHandsUpdated(XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.BeforeRender) return;
            EnsureOrigin();

            if (!LeftValid && subsystem.leftHand.isTracked)
            {
                var joint = subsystem.leftHand.GetJoint(XRHandJointID.IndexTip);
                if (joint.TryGetPose(out Pose p))
                {
                    LeftWorld = _trackingSpace != null ? _trackingSpace.TransformPoint(p.position) : p.position;
                    LeftValid = LeftWorld.sqrMagnitude > 0.0001f;
                }
            }
            if (LeftValid && !LeftPointingOnly && subsystem.leftHand.isTracked)
            {
                LeftPointingOnly = IsOpenXrIndexOnlyPointing(subsystem.leftHand);
            }

            if (!RightValid && subsystem.rightHand.isTracked)
            {
                var joint = subsystem.rightHand.GetJoint(XRHandJointID.IndexTip);
                if (joint.TryGetPose(out Pose p))
                {
                    RightWorld = _trackingSpace != null ? _trackingSpace.TransformPoint(p.position) : p.position;
                    RightValid = RightWorld.sqrMagnitude > 0.0001f;
                }
            }
            if (RightValid && !RightPointingOnly && subsystem.rightHand.isTracked)
            {
                RightPointingOnly = IsOpenXrIndexOnlyPointing(subsystem.rightHand);
            }

            BothValid = LeftValid && RightValid;
            BothPointingOnly = LeftPointingOnly && RightPointingOnly;
        }

        public static bool TryGetOvrIndexTip(OVRSkeleton skel, OVRHand hand, Transform trackingSpace, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (skel == null || !skel.IsDataValid) return false;
            if (hand != null && !hand.IsTracked) return false;
            if (skel.Bones == null) return false;

            // IMPORTANT: Always keep skel.transform at local (0,0,0) inside LeftHandAnchor/RightHandAnchor
            // because OVRCameraRig ALREADY sets LeftHandAnchor/RightHandAnchor to the hand's RootPose!
            if (skel.transform.parent != null &&
                (skel.transform.parent.name == "LeftHandAnchor" || skel.transform.parent.name == "RightHandAnchor"))
            {
                skel.transform.localPosition = Vector3.zero;
                skel.transform.localRotation = Quaternion.identity;
            }

            foreach (var bone in skel.Bones)
            {
                if (bone != null && bone.Transform != null &&
                    (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip ||
                     bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip ||
                     bone.Id == OVRSkeleton.BoneId.Hand_Index3))
                {
                    worldPos = bone.Transform.position;
                    if (worldPos.sqrMagnitude > 0.0001f)
                        return true;
                }
            }
            return false;
        }

        public static bool IsOvrIndexOnlyPointing(OVRSkeleton skel)
        {
            if (skel == null || !skel.IsDataValid || skel.Bones == null) return false;

            Transform wrist = skel.transform;
            Transform index1 = null, indexTip = null;
            Transform middle1 = null, middleTip = null;
            Transform ring1 = null, ringTip = null;
            Transform pinky1 = null, pinkyTip = null;

            foreach (var bone in skel.Bones)
            {
                if (bone == null || bone.Transform == null) continue;
                OVRSkeleton.BoneId id = bone.Id;

                if (id == OVRSkeleton.BoneId.Hand_WristRoot || id == OVRSkeleton.BoneId.XRHand_Wrist)
                {
                    wrist = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_Index1 || id == OVRSkeleton.BoneId.XRHand_IndexProximal)
                {
                    index1 = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_IndexTip || id == OVRSkeleton.BoneId.XRHand_IndexTip || id == OVRSkeleton.BoneId.Hand_Index3)
                {
                    if (indexTip == null || id != OVRSkeleton.BoneId.Hand_Index3) indexTip = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_Middle1 || id == OVRSkeleton.BoneId.XRHand_MiddleProximal)
                {
                    middle1 = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_MiddleTip || id == OVRSkeleton.BoneId.XRHand_MiddleTip || id == OVRSkeleton.BoneId.Hand_Middle3)
                {
                    if (middleTip == null || id != OVRSkeleton.BoneId.Hand_Middle3) middleTip = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_Ring1 || id == OVRSkeleton.BoneId.XRHand_RingProximal)
                {
                    ring1 = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_RingTip || id == OVRSkeleton.BoneId.XRHand_RingTip || id == OVRSkeleton.BoneId.Hand_Ring3)
                {
                    if (ringTip == null || id != OVRSkeleton.BoneId.Hand_Ring3) ringTip = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_Pinky1 || id == OVRSkeleton.BoneId.XRHand_LittleProximal)
                {
                    pinky1 = bone.Transform;
                }
                else if (id == OVRSkeleton.BoneId.Hand_PinkyTip || id == OVRSkeleton.BoneId.XRHand_LittleTip || id == OVRSkeleton.BoneId.Hand_Pinky3)
                {
                    if (pinkyTip == null || id != OVRSkeleton.BoneId.Hand_Pinky3) pinkyTip = bone.Transform;
                }
            }

            if (indexTip == null) return false;
            Vector3 wPos = wrist.position;
            float idxWristDist = Vector3.Distance(indexTip.position, wPos);
            float idxLen = index1 != null ? Vector3.Distance(indexTip.position, index1.position) : idxWristDist * 0.55f;

            // 1. Index finger must be extended (straight)
            bool indexStraight = idxWristDist > 0.115f && idxLen > 0.052f;
            if (!indexStraight) return false;

            // 2. Middle, Ring, and Pinky fingers must be curled relative to the extended Index finger
            bool middleCurled = middleTip == null ||
                                Vector3.Distance(middleTip.position, wPos) < idxWristDist * 0.84f ||
                                (middle1 != null && Vector3.Distance(middleTip.position, middle1.position) < idxLen * 0.78f);
            bool ringCurled = ringTip == null ||
                              Vector3.Distance(ringTip.position, wPos) < idxWristDist * 0.84f ||
                              (ring1 != null && Vector3.Distance(ringTip.position, ring1.position) < idxLen * 0.78f);
            bool pinkyCurled = pinkyTip == null ||
                               Vector3.Distance(pinkyTip.position, wPos) < idxWristDist * 0.84f;

            return middleCurled && ringCurled && pinkyCurled;
        }

        public static bool IsOpenXrIndexOnlyPointing(XRHand hand)
        {
            if (!hand.isTracked) return false;
            if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose idxTip))
                return false;

            float idxWristDist = Vector3.Distance(idxTip.position, wrist.position);
            float idxLen = hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose idxProx)
                ? Vector3.Distance(idxTip.position, idxProx.position)
                : idxWristDist * 0.55f;

            if (idxWristDist <= 0.115f || idxLen <= 0.052f) return false;

            bool middleCurled = !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose midTip) ||
                                Vector3.Distance(midTip.position, wrist.position) < idxWristDist * 0.84f ||
                                (hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose midProx) &&
                                 Vector3.Distance(midTip.position, midProx.position) < idxLen * 0.78f);

            bool ringCurled = !hand.GetJoint(XRHandJointID.RingTip).TryGetPose(out Pose rngTip) ||
                              Vector3.Distance(rngTip.position, wrist.position) < idxWristDist * 0.84f ||
                              (hand.GetJoint(XRHandJointID.RingProximal).TryGetPose(out Pose rngProx) &&
                               Vector3.Distance(rngTip.position, rngProx.position) < idxLen * 0.78f);

            bool pinkyCurled = !hand.GetJoint(XRHandJointID.LittleTip).TryGetPose(out Pose litTip) ||
                               Vector3.Distance(litTip.position, wrist.position) < idxWristDist * 0.84f;

            return middleCurled && ringCurled && pinkyCurled;
        }

        public static bool TryGetOvrInputHandWorld(bool isLeft, Transform trackingSpace, out Vector3 worldPos, out bool isPointingOnly)
        {
            worldPos = Vector3.zero;
            isPointingOnly = false;
            try
            {
                OVRInput.Controller handCtrl = isLeft ? OVRInput.Controller.LHand : OVRInput.Controller.RHand;
                if (OVRInput.GetControllerPositionValid(handCtrl))
                {
                    Vector3 localPos = OVRInput.GetLocalControllerPosition(handCtrl);
                    Quaternion localRot = OVRInput.GetLocalControllerRotation(handCtrl);
                    Vector3 tipLocal = localPos + localRot * (isLeft ? new Vector3(-0.085f, 0.01f, 0.02f) : new Vector3(0.085f, -0.01f, 0.02f));
                    worldPos = trackingSpace != null ? trackingSpace.TransformPoint(tipLocal) : tipLocal;
                    if (worldPos.sqrMagnitude > 0.0001f) return true;
                }

                OVRInput.Controller touchCtrl = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
                if (OVRInput.GetControllerPositionValid(touchCtrl))
                {
                    Vector3 localPos = OVRInput.GetLocalControllerPosition(touchCtrl);
                    worldPos = trackingSpace != null ? trackingSpace.TransformPoint(localPos) : localPos;
                    // On physical Touch controllers: holding Grip while NOT holding Index Trigger = index pointing gesture
                    float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, touchCtrl);
                    float trig = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, touchCtrl);
                    isPointingOnly = (grip > 0.5f && trig < 0.25f);
                    if (worldPos.sqrMagnitude > 0.0001f) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool TryGetOvrInputHandWorld(bool isLeft, Transform trackingSpace, out Vector3 worldPos)
        {
            return TryGetOvrInputHandWorld(isLeft, trackingSpace, out worldPos, out _);
        }
    }
}
