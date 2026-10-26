using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR;
using Meta.XR.MRUtilityKit;
using KineticCore.Runtime;

[Preserve]
public class DeskWorkspaceInitializer : MonoBehaviour
{
    [SerializeField] private GameObject deskWorkspaceOrigin;

    private void Awake()
    {
        if (deskWorkspaceOrigin == null)
            deskWorkspaceOrigin = GameObject.Find("DeskWorkspaceOrigin");
    }

    private void Start()
    {
        if (deskWorkspaceOrigin == null)
            deskWorkspaceOrigin = GameObject.Find("DeskWorkspaceOrigin");

        // Only query MRUK when a Quest headset is actively running via XR
        if (!XRSettings.isDeviceActive || MRUK.Instance == null)
        {
            return;
        }

        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
    }

    private void OnSceneLoaded()
    {
        if (deskWorkspaceOrigin == null)
            deskWorkspaceOrigin = GameObject.Find("DeskWorkspaceOrigin");

        if (deskWorkspaceOrigin == null || !XRSettings.isDeviceActive || MRUK.Instance == null)
            return;

        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        Transform head = FindHeadTransform();
        Vector3 headPos = head != null ? head.position : Vector3.zero;
        Vector3 headForwardFlat = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized : Vector3.forward;
        if (headForwardFlat.sqrMagnitude < 0.01f) headForwardFlat = Vector3.forward;

        MRUKAnchor bestTable = null;
        float bestDist = float.MaxValue;

        foreach (var anchor in room.Anchors)
        {
            if (anchor != null && anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
            {
                float d = Vector3.Distance(headPos, anchor.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestTable = anchor;
                }
            }
        }

        if (bestTable == null)
        {
            Debug.LogWarning("[DeskWorkspaceInitializer] No TABLE anchor found in current room.");
            return;
        }

        // Guard against stale/garbage room anchors that are far from the user (> 1.4m away)
        if (head != null && bestDist > 1.4f)
        {
            Debug.LogWarning($"[DeskWorkspaceInitializer] Ignoring distant TABLE anchor ({bestDist:F2}m from HMD); keeping HMD-centered desk.");
            return;
        }

        // Place the workspace at the exact tabletop height (bestTable.transform.position.y),
        // comfortably in front of the user if they are seated at a wide desk, facing the user horizontally.
        Vector3 tableTopCenter = bestTable.transform.position;
        Vector3 pos = tableTopCenter;
        Quaternion rot = deskWorkspaceOrigin.transform.rotation;

        if (head != null && headPos.sqrMagnitude > 0.001f)
        {
            Vector3 desiredInFront = headPos + headForwardFlat * 0.46f;
            desiredInFront.y = tableTopCenter.y;

            // Blend toward the point directly in front of the user on the table surface
            Vector3 horizontalDelta = new Vector3(desiredInFront.x - tableTopCenter.x, 0f, desiredInFront.z - tableTopCenter.z);
            if (horizontalDelta.magnitude < 0.65f)
            {
                pos = desiredInFront;
            }
            else
            {
                pos = new Vector3(tableTopCenter.x, tableTopCenter.y, tableTopCenter.z);
            }

            Vector3 forwardFlat = Vector3.ProjectOnPlane(pos - headPos, Vector3.up).normalized;
            if (forwardFlat.sqrMagnitude < 0.01f)
                forwardFlat = headForwardFlat;
            rot = Quaternion.LookRotation(forwardFlat, Vector3.up);
        }

        deskWorkspaceOrigin.transform.SetPositionAndRotation(pos, rot);

        var controller = deskWorkspaceOrigin.GetComponent<KineticMechanismController>();
        if (controller != null)
        {
            controller.NotifyDeskAnchored(pos, rot, "MRUK Table Locked");
        }

        Debug.Log($"[DeskWorkspaceInitializer] Snapped to TABLE at {pos} (dist={bestDist:F2}m)");
    }

    private static Transform FindHeadTransform()
    {
        GameObject centerEye = GameObject.Find("CenterEyeAnchor");
        if (centerEye != null) return centerEye.transform;
        if (Camera.main != null) return Camera.main.transform;
        return null;
    }
}
