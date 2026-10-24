using UnityEngine;
using UnityEngine.XR;
using Meta.XR.MRUtilityKit;

public class DeskWorkspaceInitializer : MonoBehaviour
{
    [SerializeField] private GameObject deskWorkspaceOrigin;

    private void Start()
    {
        // Only query MRUK when a Quest headset is actively running via XR
        if (!XRSettings.isDeviceActive || MRUK.Instance == null)
        {
            return;
        }

        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
    }

    private void OnSceneLoaded()
    {
        if (deskWorkspaceOrigin == null || !XRSettings.isDeviceActive || MRUK.Instance == null)
            return;

        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        MRUKAnchor tableAnchor = null;
        foreach (var anchor in room.Anchors)
        {
            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
            {
                tableAnchor = anchor;
                break;
            }
        }

        if (tableAnchor == null)
        {
            Debug.LogWarning("No TABLE anchor found in current room.");
            return;
        }

        deskWorkspaceOrigin.transform.SetPositionAndRotation(
            tableAnchor.transform.position,
            tableAnchor.transform.rotation
        );

        Debug.Log($"[DeskWorkspaceInitializer] Snapped to TABLE at {tableAnchor.transform.position}");
    }
}