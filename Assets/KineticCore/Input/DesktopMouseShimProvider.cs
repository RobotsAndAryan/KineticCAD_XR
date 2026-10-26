using UnityEngine;
using UnityEngine.Scripting;

namespace KineticCore.Input
{
    [Preserve]
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
        private MaterialPropertyBlock _mpb;

        public Vector3 LeftFingertipWorld  => _leftWorld;
        public Vector3 RightFingertipWorld => _rightWorld;
        public bool    BothHandsValid      => _initialized;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (deskFrame == null)
                deskFrame = GameObject.Find("DeskWorkspaceOrigin")?.transform ?? transform;

            CreateSpheres();
            _initialized = true;
        }

        private void CreateSpheres()
        {
            if (leftSphere == null)
            {
                leftSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leftSphere.name = "Shim_Left";
                Destroy(leftSphere.GetComponent<Collider>());
                leftSphere.transform.localScale = Vector3.one * 0.02f;
                leftSphere.transform.position = deskFrame.position + deskFrame.right * -0.05f + deskFrame.up * 0.01f;
                SetColorSafe(leftSphere.GetComponent<Renderer>(), Color.blue);
                _leftWorld = leftSphere.transform.position;
            }
            if (rightSphere == null)
            {
                rightSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rightSphere.name = "Shim_Right";
                Destroy(rightSphere.GetComponent<Collider>());
                rightSphere.transform.localScale = Vector3.one * 0.02f;
                rightSphere.transform.position = deskFrame.position + deskFrame.right * 0.05f + deskFrame.up * 0.01f;
                SetColorSafe(rightSphere.GetComponent<Renderer>(), Color.red);
                _rightWorld = rightSphere.transform.position;
            }
        }

        public void Update()
        {
            if (deskFrame == null) return;

            bool shift = KInput.GetKey(KeyCode.LeftShift) || KInput.GetKey(KeyCode.RightShift);
            float speed = moveSpeed * (shift ? shiftMultiplier : 1f);
            Vector3 move = Vector3.zero;
            if (KInput.GetKey(KeyCode.W)) move += deskFrame.forward;
            if (KInput.GetKey(KeyCode.S)) move -= deskFrame.forward;
            if (KInput.GetKey(KeyCode.D)) move += deskFrame.right;
            if (KInput.GetKey(KeyCode.A)) move -= deskFrame.right;
            if (KInput.GetKey(KeyCode.E)) move += deskFrame.up;
            if (KInput.GetKey(KeyCode.Q)) move -= deskFrame.up;

            if (move.sqrMagnitude > 0.01f)
            {
                move.Normalize();
                _rightWorld += move * (speed * Time.deltaTime);
            }
            if (rightSphere != null)
                rightSphere.transform.position = _rightWorld;
        }

        private void SetColorSafe(Renderer r, Color c)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
