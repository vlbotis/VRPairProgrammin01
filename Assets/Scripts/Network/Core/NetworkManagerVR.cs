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
        //networkManager.NetworkConfig.PlayerPrefab = playerPrefab.gameObject;

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

        // Add null check for NetworkConfig and Prefabs
        if (networkManager.NetworkConfig == null || networkManager.NetworkConfig.Prefabs == null)
        {
            Debug.LogError("[NetworkManagerVR] NetworkConfig or Prefabs list is null!");
            enabled = false;
            return;
        }

        // Register the player prefab with NetworkManager
        var prefabsList = networkManager.NetworkConfig.Prefabs.Prefabs;
        bool prefabRegistered = prefabsList.Any(p => p.Prefab == playerPrefab.gameObject);

        if (!prefabRegistered)
        {
            networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab.gameObject });
            Debug.Log("[NetworkManagerVR] Player prefab registered with NetworkManager");
        }


        // ADD THIS LINE HERE - Before any network operations start
        NetworkManager.Singleton.NetworkConfig.PlayerPrefab = null;
        Debug.Log("[NetworkManagerVR] Disabled automatic player spawn");

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
        Debug.Log("[NetworkManagerVR] OnDestroy called");

        // Only cleanup if we're actually in a networked state
        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsHost ||
             NetworkManager.Singleton.IsServer ||
             NetworkManager.Singleton.IsClient))
        {
            CleanupNetworking();
        }
        else
        {
            Debug.Log("[NetworkManagerVR] No active network connection to clean up");
        }
    }

    private void CleanupNetworking()
    {
        Debug.Log("[NetworkManagerVR] Starting network cleanup");

        try
        {
            UnsubscribeFromEvents();
            Disconnect();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error during network cleanup: {e}");
        }
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
        Debug.Log("[NetworkManagerVR] Starting client connection process...");

        try
        {
            if (!ValidateNetworkState())
            {
                Debug.LogError("[NetworkManagerVR] Network state validation failed");
                return false;
            }

            // Setup connection callbacks before starting
            networkManager.OnClientConnectedCallback += (id) =>
                Debug.Log($"[NetworkManagerVR] Client connected with ID: {id}");

            networkManager.OnClientDisconnectCallback += (id) =>
                Debug.Log($"[NetworkManagerVR] Client disconnected with ID: {id}");

            await Task.Yield(); // Allow frame to complete

            Debug.Log("[NetworkManagerVR] Initiating client connection...");
            networkManager.StartClient();

            // Wait briefly to check connection status
            await Task.Delay(1000);

            bool isConnected = networkManager.IsClient;
            Debug.Log($"[NetworkManagerVR] Client connection status: {(isConnected ? "Connected" : "Failed to connect")}");

            return isConnected;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error during client start: {e}");
            return false;
        }
    }

    private bool ValidateNetworkState()
    {
        Debug.Log("[NetworkManagerVR] Starting network validation...");

        // Check if components exist
        if (networkManager == null)
        {
            Debug.LogError("[NetworkManagerVR] NetworkManager is null!");
            return false;
        }

        if (transport == null)
        {
            Debug.LogError("[NetworkManagerVR] Transport is null!");
            return false;
        }

        // Check connection state
        if (networkManager.IsClient)
        {
            Debug.LogWarning("[NetworkManagerVR] Already connected as client!");
            return false;
        }

        if (networkManager.IsHost)
        {
            Debug.LogWarning("[NetworkManagerVR] Already connected as host!");
            return false;
        }

        Debug.Log("[NetworkManagerVR] Network validation passed");
        return true;
    }

    public void Disconnect()
    {
        try
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[NetworkManagerVR] NetworkManager.Singleton is null during disconnect");
                return;
            }

            // Only attempt player cleanup if we're host/server
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                CleanupPlayers();
            }

            // Store network state before shutdown
            bool wasConnected = NetworkManager.Singleton.IsHost ||
                              NetworkManager.Singleton.IsServer ||
                              NetworkManager.Singleton.IsClient;

            // Perform shutdown if we were connected
            if (wasConnected)
            {
                NetworkManager.Singleton.Shutdown();
                Debug.Log("[NetworkManagerVR] Network shutdown completed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error during disconnect: {e}");
        }
    }

    private void CleanupPlayers()
    {
        try
        {
            // Check NetworkManager and client status
            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[NetworkManagerVR] NetworkManager.Singleton is null during cleanup");
                return;
            }

            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
            {
                Debug.Log("[NetworkManagerVR] Cleanup skipped - not server or host");
                return;
            }

            // Check if our collections are initialized
            if (clientSpawned == null)
            {
                Debug.LogWarning("[NetworkManagerVR] clientSpawned dictionary is null");
                return;
            }

            if (clientLock == null)
            {
                Debug.LogWarning("[NetworkManagerVR] clientLock is null");
                return;
            }

            lock (clientLock)
            {
                try
                {
                    // Create a safe copy of keys to iterate
                    var clientIds = clientSpawned.Keys.ToList();

                    foreach (var clientId in clientIds)
                    {
                        var playerObjects = GameObject.FindGameObjectsWithTag("Player")
                            ?.Where(p => p != null &&
                                    p.TryGetComponent<NetworkObject>(out var netObj) &&
                                    netObj.OwnerClientId == clientId)
                            ?.ToList();

                        if (playerObjects != null)
                        {
                            foreach (var player in playerObjects)
                            {
                                if (player != null)
                                {
                                    DespawnPlayer(player, clientId);
                                }
                            }
                        }
                    }

                    clientSpawned.Clear();
                    Debug.Log("[NetworkManagerVR] Players cleanup completed successfully");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetworkManagerVR] Error during player cleanup in lock: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManagerVR] Error during player cleanup: {e.Message}");
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
        Debug.Log($"[NetworkManagerVR] Server started. IsHost: {networkManager.IsHost}");

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

            // This single call handles spawning, ownership, and network visibility
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