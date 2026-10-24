using UnityEngine;
using KineticCore.Input;

namespace KineticCore.Input
{
    public class OpenXRHandProvider : MonoBehaviour, ISpatialHandInput
    {
        [SerializeField] private HandFingertipReader reader;

        public Vector3 LeftFingertipWorld  => reader.LeftWorld;
        public Vector3 RightFingertipWorld => reader.RightWorld;
        public bool    BothHandsValid      => reader.BothValid;

        private void Awake()
        {
            if (reader == null)
                reader = FindAnyObjectByType<HandFingertipReader>();
        }

        public void Update() { }
    }
}   