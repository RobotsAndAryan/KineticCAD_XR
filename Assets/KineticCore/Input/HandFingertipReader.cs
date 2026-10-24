using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace KineticCore.Input
{
    public class HandFingertipReader : MonoBehaviour
    {
        [SerializeField] private Transform xrOrigin;

        public Vector3 LeftWorld  { get; private set; }
        public Vector3 RightWorld { get; private set; }
        public bool    BothValid  { get; private set; }

        private XRHandSubsystem _subsystem;

        private void OnEnable()
        {
            var list = new System.Collections.Generic.List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            foreach (var sub in list)
            {
                if (sub.running)
                {
                    _subsystem = sub;
                    _subsystem.updatedHands += OnHandsUpdated;
                    break;
                }
            }
        }

        private void OnDisable()
        {
            if (_subsystem != null)
                _subsystem.updatedHands -= OnHandsUpdated;
        }

        private void OnHandsUpdated(XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.BeforeRender) return;

            var originPose = new Pose(xrOrigin.position, xrOrigin.rotation);
            bool leftOk = false, rightOk = false;

            if (subsystem.leftHand != null)
            {
                var joint = subsystem.leftHand.GetJoint(XRHandJointID.IndexTip);
                if (joint.TryGetPose(out Pose p))
                {
                    LeftWorld = p.GetTransformedBy(originPose).position;
                    leftOk = true;
                }
            }

            if (subsystem.rightHand != null)
            {
                var joint = subsystem.rightHand.GetJoint(XRHandJointID.IndexTip);
                if (joint.TryGetPose(out Pose p))
                {
                    RightWorld = p.GetTransformedBy(originPose).position;
                    rightOk = true;
                }
            }

            BothValid = leftOk && rightOk;
        }
    }
}   