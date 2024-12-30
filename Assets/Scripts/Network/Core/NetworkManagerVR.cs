using Unity.Netcode;
using UnityEngine;
using Unity.Netcode.Transports.UTP;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

public enum ConnectionType
{
    PeerToPeer,
    Relay
}

public class NetworkManagerVR : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private ConnectionType connectionType = ConnectionType.PeerToPeer;
    [SerializeField] private string defaultIP = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;
    [SerializeField] private NetworkObject playerPrefab;

    [Header("References")]
    [SerializeField] private SpawnManager spawnManager;

    private NetworkManager networkManager;
    private UnityTransport transport;
    // Using ConcurrentDictionary would be better for thread safety, but Unity's serialization doesn't support it
    private readonly Dictionary<ulong, bool> clientSpawned = new Dictionary<ulong, bool>();
    private readonly object clientLock = new object();

    private void Awake()
    {
        // Get required components
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        if (networkManager == null || transport == null)
        {
            Debug.LogError("Required network components missing!");
            return;
        }

        // Disable automatic spawning
        networkManager.NetworkConfig.PlayerPrefab = null;

        ValidateComponents();
    }

    private void ValidateComponents()
    {
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        if (networkManager == null || transport == null)
        {
            Debug.LogError("[NetworkManagerVR] Required network components missing!");
            enabled = false;
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[NetworkManagerVR] Player prefab not assigned!");
            enabled = false;
            return;
        }

        // Find or validate SpawnManager
        if (spawnManager == null)
        {
            spawnManager = FindFirstObjectByType<SpawnManager>();
            if (spawnManager == null)
            {
                Debug.LogError("[NetworkManagerVR] SpawnManager not found in scene!");
                enabled = false;
                return;
            }
        }

        // Set up transport defaults
        ConfigureTransport();
    }

    private void ConfigureTransport()
    {
        transport.ConnectionData.Address = defaultIP;
        transport.ConnectionData.Port = defaultPort;
    }

    private void OnEnable()
    {
        SubscribeToEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    private void SubscribeToEvents()
    {
        if (networkManager != null)
        {
            networkManager.OnServerStarted += OnServerStarted;
            networkManager.OnClientConnectedCallback += OnServerAddPlayer;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientConnectedCallback -= OnServerAddPlayer;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        CleanupNetworking();
    }

    private void CleanupNetworking()
    {
        UnsubscribeFromEvents();
        Disconnect();
    }

    public async Task<bool> StartHost()
    {
        try
        {
            if (!ValidateNetworkState()) return false;

            await Task.Yield(); // Allow frame to complete
            networkManager.StartHost();
            Debug.Log("[NetworkManagerVR] Host started successfully");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Failed to start host: {e}");
            return false;
        }
    }

    public async Task<bool> StartClient()
    {
        try
        {
            if (!ValidateNetworkState()) return false;

            await Task.Yield(); // Allow frame to complete
            networkManager.StartClient();
            Debug.Log("[NetworkManagerVR] Client started successfully");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Failed to start client: {e}");
            return false;
        }
    }

    private bool ValidateNetworkState()
    {
        if (networkManager == null || transport == null)
        {
            Debug.LogError("[NetworkManagerVR] Network components not initialized!");
            return false;
        }

        if (networkManager.IsClient || networkManager.IsHost)
        {
            Debug.LogWarning("[NetworkManagerVR] Already connected!");
            return false;
        }

        return true;
    }

    public void Disconnect()
    {
        Debug.Log("[NetworkManagerVR] Initiating network disconnect");

        try
        {
            CleanupPlayers();
            ResetNetworkState();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error during disconnect: {e}");
        }
    }

    private void CleanupPlayers()
    {
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost) return;

        lock (clientLock)
        {
            foreach (var clientId in clientSpawned.Keys.ToList())
            {
                var playerObjects = GameObject.FindGameObjectsWithTag("Player")
                    .Where(p => p != null && p.TryGetComponent<NetworkObject>(out var netObj) && netObj.OwnerClientId == clientId);

                foreach (var player in playerObjects)
                {
                    DespawnPlayer(player, clientId);
                }
            }
            clientSpawned.Clear();
        }
    }

    private void DespawnPlayer(GameObject player, ulong clientId)
    {
        try
        {
            if (player.TryGetComponent<NetworkObject>(out var netObj))
            {
                if (netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    Destroy(player);
                }
                Debug.Log($"[NetworkManagerVR] Cleaned up player for client {clientId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error despawning player {clientId}: {e}");
        }
    }

    private void ResetNetworkState()
    {
        spawnManager?.ResetSpawnPoints();

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }

    public string GetConnectionInfo()
    {
        return transport != null ? $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}" : "Not configured";
    }

    // In NetworkManagerVR
    private void OnServerStarted()
    {
        Debug.Log($"Server started. IsHost: {networkManager.IsHost}");

        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab is null!");
            return;
        }

        // Disable automatic spawn
        NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;

        Debug.Log("Player Prefab is ready for spawning");
    }

    private void ValidatePlayerPrefab()
    {
        if (playerPrefab == null || !playerPrefab.TryGetComponent<NetworkObject>(out _))
        {
            Debug.LogError("[NetworkManagerVR] Invalid player prefab configuration!");
            return;
        }
        Debug.Log("[NetworkManagerVR] Player prefab validated");
    }

    private async void OnServerAddPlayer(ulong clientId)
    {
        if (!ShouldSpawnPlayer(clientId)) return;

        lock (clientLock)
        {
            if (!clientSpawned.ContainsKey(clientId))
            {
                clientSpawned[clientId] = false;
            }
        }

        bool success = await SpawnPlayerAsync(clientId);
        if (!success)
        {
            lock (clientLock)
            {
                clientSpawned.Remove(clientId);
            }
        }
    }

    private bool ShouldSpawnPlayer(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[NetworkManagerVR] Not spawning player - not the server");
            return false;
        }

        lock (clientLock)
        {
            if (clientSpawned.ContainsKey(clientId) && clientSpawned[clientId])
            {
                Debug.Log($"[NetworkManagerVR] Client {clientId} already has a player");
                return false;
            }
        }

        return true;
    }

    private async Task<bool> SpawnPlayerAsync(ulong clientId)
    {
        try
        {
            lock (clientLock)
            {
                if (clientSpawned.ContainsKey(clientId) && clientSpawned[clientId])
                {
                    Debug.Log($"[NetworkManagerVR] Skipping spawn for client {clientId} - already spawned");
                    return false;
                }
            }

            await Task.Yield();

            Vector3 spawnPos = spawnManager.GetNextSpawnPosition();
            Debug.Log($"[NetworkManagerVR] Attempting to spawn player for client {clientId} at {spawnPos}");

            NetworkObject playerInstance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            if (playerInstance == null)
            {
                throw new Exception("Failed to instantiate player prefab");
            }

            // Spawn with ownership and automatically handle visibility
            playerInstance.SpawnAsPlayerObject(clientId);

            lock (clientLock)
            {
                clientSpawned[clientId] = true;
            }

            Debug.Log($"[NetworkManagerVR] Successfully spawned player for client {clientId} at {spawnPos}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error spawning player for client {clientId}: {e}");
            lock (clientLock)
            {
                clientSpawned.Remove(clientId);
            }
            return false;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        lock (clientLock)
        {
            clientSpawned.Remove(clientId);
        }
        Debug.Log($"[NetworkManagerVR] Client {clientId} disconnected and cleaned up");
    }
}