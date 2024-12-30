using System.Collections.Generic;
using System;
using Unity.Netcode;
using UnityEngine;

public class SpawnManager : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private Transform[] spawnPoints;
    private int nextSpawnPointIndex = 0;
    private bool isInitialized = false;
    private readonly object initLock = new object(); // Thread safety

    private void Awake()
    {
        Debug.Log("[SpawnManager] Initializing");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"[SpawnManager] OnNetworkSpawn - IsServer: {IsServer}");
        if (IsServer)
        {
            InitializeSpawnPoints();
        }
    }

    public void InitializeSpawnPoints()
    {
        // Thread-safe initialization
        lock (initLock)
        {
            if (isInitialized)
            {
                Debug.Log("[SpawnManager] Already initialized, skipping");
                return;
            }

            Debug.Log("[SpawnManager] Initializing spawn points");

            var taggedObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (taggedObjects == null || taggedObjects.Length == 0)
            {
                Debug.LogError("[SpawnManager] No spawn points found! Players will spawn at origin.");
                spawnPoints = new Transform[0];
                isInitialized = true;
                return;
            }

            // Convert to transform array and validate each point
            var validSpawnPoints = new List<Transform>();
            foreach (var obj in taggedObjects)
            {
                if (obj != null && obj.transform != null)
                {
                    validSpawnPoints.Add(obj.transform);
                    Debug.Log($"[SpawnManager] Validated spawn point: {obj.name} at position {obj.transform.position}");
                }
            }

            spawnPoints = validSpawnPoints.ToArray();
            Debug.Log($"[SpawnManager] Initialization complete. Found {spawnPoints.Length} valid spawn points");
            isInitialized = true;
        }
    }

    public Vector3 GetNextSpawnPosition()
    {
        if (!isInitialized || spawnPoints == null)
        {
            lock (initLock)
            {
                if (!isInitialized)
                {
                    InitializeSpawnPoints();
                }
            }
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[SpawnManager] No valid spawn points available! Using origin.");
            return Vector3.zero;
        }

        // Thread-safe index increment
        int currentIndex;
        lock (initLock)
        {
            currentIndex = nextSpawnPointIndex;
            nextSpawnPointIndex = (nextSpawnPointIndex + 1) % spawnPoints.Length;
        }

        Vector3 position = spawnPoints[currentIndex].position;
        Debug.Log($"[SpawnManager] Using spawn position {position} from point {currentIndex}");
        return position;
    }

    public void ResetSpawnPoints()
    {
        lock (initLock)
        {
            Debug.Log("[SpawnManager] Resetting spawn system");
            nextSpawnPointIndex = 0;
            // Don't set isInitialized to false, just reset the index
            // This prevents unnecessary re-initialization
        }
    }

    public bool IsReady()
    {
        return isInitialized && spawnPoints != null && spawnPoints.Length > 0;
    }

    public int GetSpawnPointCount()
    {
        return spawnPoints?.Length ?? 0;
    }
}