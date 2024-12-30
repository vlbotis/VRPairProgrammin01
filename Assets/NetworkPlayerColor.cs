using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerColor : NetworkBehaviour
{
    private NetworkVariable<Color> playerColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private MeshRenderer meshRenderer;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        Debug.Log($"[NetworkPlayerColor] Awake on {(IsClient ? "Client" : "Host")}, HasMeshRenderer: {meshRenderer != null}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[NetworkPlayerColor] OnNetworkSpawn - " +
                  $"IsServer: {IsServer}, " +
                  $"IsClient: {IsClient}, " +
                  $"IsHost: {IsHost}, " +
                  $"IsOwner: {IsOwner}, " +
        $"ClientId: {OwnerClientId}, " +
                  $"NetworkObjectId: {NetworkObjectId}");

        if (IsServer)
        {
            Color newColor = IsOwner ? Color.blue : Color.green;
            playerColor.Value = newColor;
            Debug.Log($"[NetworkPlayerColor] Server set color to {newColor} for player {OwnerClientId}");
        }

        // Everyone should subscribe to changes and apply initial color
        playerColor.OnValueChanged += OnColorChanged;
        UpdateColor(playerColor.Value);
    }

    private void OnColorChanged(Color previousValue, Color newValue)
    {
        Debug.Log($"[NetworkPlayerColor] OnColorChanged from {previousValue} to {newValue} on {(IsClient ? "Client" : "Host")} for player {OwnerClientId}");
        UpdateColor(newValue);
    }

    private void UpdateColor(Color color)
    {
        if (meshRenderer == null)
        {
            Debug.LogError($"[NetworkPlayerColor] MeshRenderer is null on {(IsClient ? "Client" : "Host")} for player {OwnerClientId}");
            return;
        }

        if (meshRenderer.material == null)
        {
            Debug.LogError($"[NetworkPlayerColor] Material is null on {(IsClient ? "Client" : "Host")} for player {OwnerClientId}");
            return;
        }

        meshRenderer.material.color = color;
        Debug.Log($"[NetworkPlayerColor] Applied color {color} on {(IsClient ? "Client" : "Host")} for player {OwnerClientId}");
    }

    public override void OnDestroy()
    {
        Debug.Log($"[NetworkPlayerColor] OnDestroy for player {OwnerClientId} on {(IsClient ? "Client" : "Host")}");
        base.OnDestroy();
    }
}