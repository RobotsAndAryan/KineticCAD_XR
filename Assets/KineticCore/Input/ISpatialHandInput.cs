using UnityEngine;

namespace KineticCore.Input
{
    public interface ISpatialHandInput
    {
        Vector3 LeftFingertipWorld  { get; }
        Vector3 RightFingertipWorld { get; }
        bool    BothHandsValid      { get; }
        void Update();
    }
}   