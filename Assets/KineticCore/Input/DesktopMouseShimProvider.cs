using UnityEngine;
using KineticCore.Input;

namespace KineticCore.Input
{
    public class DesktopMouseShimProvider : MonoBehaviour, ISpatialHandInput
    {
        [SerializeField] private Transform deskFrame;
        [SerializeField] private float moveSpeed = 0.5f;
        [SerializeField] private float shiftMultiplier = 3f;

        [SerializeField] private GameObject leftSphere;
        [SerializeField] private GameObject rightSphere;

        private Vector3 _leftWorld;
        private Vector3 _rightWorld;
        private bool _initialized;

        public Vector3 LeftFingertipWorld  => _leftWorld;
        public Vector3 RightFingertipWorld => _rightWorld;
        public bool    BothHandsValid      => _initialized;

        private void Awake()
        {
            if (deskFrame == null)
                deskFrame = GameObject.Find("DeskWorkspaceOrigin")?.transform;

            CreateSpheres();
            _initialized = true;
        }

        private void CreateSpheres()
        {
            if (leftSphere == null)
            {
                leftSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leftSphere.name = "Shim_Left";
                leftSphere.transform.localScale = Vector3.one * 0.02f;
                leftSphere.transform.position = deskFrame.position + deskFrame.right * -0.05f + deskFrame.up * 0.01f;
                var mat = new Material(Shader.Find("Standard"));
                mat.color = Color.blue;
                leftSphere.GetComponent<Renderer>().sharedMaterial = mat;
            }
            if (rightSphere == null)
            {
                rightSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rightSphere.name = "Shim_Right";
                rightSphere.transform.localScale = Vector3.one * 0.02f;
                rightSphere.transform.position = deskFrame.position + deskFrame.right * 0.05f + deskFrame.up * 0.01f;
                var mat = new Material(Shader.Find("Standard"));
                mat.color = Color.red;
                rightSphere.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        public void Update()
        {
            if (deskFrame == null) return;

            // LEFT: mouse raycast onto desk plane
            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(UnityEngine.Input.mousePosition);
                Vector3 planePoint = deskFrame.position;
                Vector3 planeNormal = deskFrame.up;
                float denom = Vector3.Dot(ray.direction, planeNormal);
                if (Mathf.Abs(denom) > 1e-6f)
                {
                    float t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
                    if (t > 0f)
                        _leftWorld = ray.origin + ray.direction * t;
                }
            }
            leftSphere.transform.position = _leftWorld;

            // RIGHT: WASD + Shift
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            float speed = moveSpeed * (shift ? shiftMultiplier : 1f);
            Vector3 move = Vector3.zero;
            if (UnityEngine.Input.GetKey(KeyCode.W)) move += deskFrame.forward;
            if (UnityEngine.Input.GetKey(KeyCode.S)) move -= deskFrame.forward;
            if (UnityEngine.Input.GetKey(KeyCode.A)) move -= deskFrame.right;
            if (UnityEngine.Input.GetKey(KeyCode.D)) move += deskFrame.right;

            if (move.sqrMagnitude > 0.01f)
            {
                move.Normalize();
                _rightWorld += move * (speed * Time.deltaTime);
            }
            rightSphere.transform.position = _rightWorld;
        }
    }
}   