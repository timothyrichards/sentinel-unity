using UnityEngine;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;

/// <summary>
/// Manages synchronization of lootable items with SpacetimeDB.
/// Handles spawning, despawning, and visibility of lootable objects based on server state.
/// </summary>
public class LootableSync : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private float respawnCheckInterval = 5f;
    [SerializeField] private Transform spawnParent;

    [Header("Lootable Prefabs")]
    [Tooltip("Map lootable type IDs to prefabs. Index 0 = type_id 1, etc.")]
    [SerializeField] private LootablePrefabMapping[] prefabMappings;

    [System.Serializable]
    public class LootablePrefabMapping
    {
        public uint typeId;
        public GameObject prefab;
    }

    // Runtime tracking
    private static readonly Dictionary<uint, LootableItem> spawnedLootables = new();
    private static readonly Dictionary<uint, LootableItemType> itemTypes = new();

    public static LootableSync Instance { get; private set; }

    private float nextRespawnCheck;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        SpacetimeManager.OnConnected += HandleConnected;

        if (SpacetimeManager.Conn != null)
        {
            SubscribeToEvents();
        }
    }

    private void OnDisable()
    {
        SpacetimeManager.OnConnected -= HandleConnected;

        if (SpacetimeManager.Conn != null)
        {
            UnsubscribeFromEvents();
        }
    }

    private void SubscribeToEvents()
    {
        SpacetimeManager.Conn.Db.LootableSpawn.OnInsert += HandleSpawnInserted;
        SpacetimeManager.Conn.Db.LootableSpawn.OnUpdate += HandleSpawnUpdated;
        SpacetimeManager.Conn.Db.LootableSpawn.OnDelete += HandleSpawnDeleted;
        SpacetimeManager.Conn.Db.LootableItemType.OnInsert += HandleItemTypeInserted;
        SpacetimeManager.Conn.Db.LootableItemType.OnUpdate += HandleItemTypeUpdated;
    }

    private void UnsubscribeFromEvents()
    {
        SpacetimeManager.Conn.Db.LootableSpawn.OnInsert -= HandleSpawnInserted;
        SpacetimeManager.Conn.Db.LootableSpawn.OnUpdate -= HandleSpawnUpdated;
        SpacetimeManager.Conn.Db.LootableSpawn.OnDelete -= HandleSpawnDeleted;
        SpacetimeManager.Conn.Db.LootableItemType.OnInsert -= HandleItemTypeInserted;
        SpacetimeManager.Conn.Db.LootableItemType.OnUpdate -= HandleItemTypeUpdated;
    }

    private void HandleConnected()
    {
        SubscribeToEvents();

        // Subscribe to lootable tables
        SpacetimeManager.Instance.AddSubscription("SELECT * FROM lootable_item_type");
        SpacetimeManager.Instance.AddSubscription("SELECT * FROM lootable_spawn");
    }

    private void Update()
    {
        // Periodically trigger respawn check on server
        if (Time.time >= nextRespawnCheck)
        {
            nextRespawnCheck = Time.time + respawnCheckInterval;
            TriggerRespawnCheck();
        }
    }

    private void TriggerRespawnCheck()
    {
        if (SpacetimeManager.Conn != null && SpacetimeManager.Conn.IsActive)
        {
            SpacetimeManager.Conn.Reducers.LootableCheckRespawns();
        }
    }

    #region Item Type Handlers

    private void HandleItemTypeInserted(EventContext ctx, LootableItemType itemType)
    {
        itemTypes[itemType.TypeId] = itemType;
        Debug.Log($"[LootableSync] Registered item type: {itemType.Name} (ID: {itemType.TypeId})");
    }

    private void HandleItemTypeUpdated(EventContext ctx, LootableItemType oldType, LootableItemType newType)
    {
        itemTypes[newType.TypeId] = newType;
    }

    public static LootableItemType GetItemType(uint typeId)
    {
        return itemTypes.TryGetValue(typeId, out var itemType) ? itemType : null;
    }

    #endregion

    #region Spawn Handlers

    private void HandleSpawnInserted(EventContext ctx, LootableSpawn spawn)
    {
        SpawnLootable(spawn);
    }

    private void HandleSpawnUpdated(EventContext ctx, LootableSpawn oldSpawn, LootableSpawn newSpawn)
    {
        Debug.Log($"[LootableSync] Spawn {newSpawn.SpawnId} updated: IsLooted={newSpawn.IsLooted} (was {oldSpawn.IsLooted})");

        // LootableItem handles its own updates via event subscription
        // This is just for spawning new items if needed
        if (!spawnedLootables.ContainsKey(newSpawn.SpawnId) && !newSpawn.IsLooted)
        {
            // Item respawned but we don't have the GameObject - spawn it
            SpawnLootable(newSpawn);
        }
    }

    private void HandleSpawnDeleted(EventContext ctx, LootableSpawn spawn)
    {
        if (spawnedLootables.TryGetValue(spawn.SpawnId, out LootableItem pickup))
        {
            Destroy(pickup.gameObject);
            spawnedLootables.Remove(spawn.SpawnId);
        }
    }

    private void SpawnLootable(LootableSpawn spawn)
    {
        if (spawnedLootables.ContainsKey(spawn.SpawnId))
        {
            Debug.LogWarning($"[LootableSync] Spawn {spawn.SpawnId} already exists");
            return;
        }

        // Find the prefab for this type
        GameObject prefab = GetPrefabForType(spawn.TypeId);
        if (prefab == null)
        {
            Debug.LogWarning($"[LootableSync] No prefab mapped for type_id {spawn.TypeId}");
            return;
        }

        // Instantiate at spawn position (world space)
        Vector3 position = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
        Quaternion rotation = Quaternion.Euler(spawn.Rotation.X, spawn.Rotation.Y, spawn.Rotation.Z);

        GameObject instance = Instantiate(prefab, position, rotation, spawnParent);

        LootableItem pickup = instance.GetComponent<LootableItem>();
        if (pickup == null)
        {
            Debug.LogError($"[LootableSync] Prefab for type {spawn.TypeId} is missing LootableItem component!");
            Destroy(instance);
            return;
        }

        // Set the spawn ID and type ID so LootableItem can sync with server
        pickup.spawnId = spawn.SpawnId;
        pickup.typeId = spawn.TypeId;
        spawnedLootables[spawn.SpawnId] = pickup;

        Debug.Log($"[LootableSync] Spawned lootable {spawn.SpawnId} (type {spawn.TypeId}) at {position}");
    }

    private GameObject GetPrefabForType(uint typeId)
    {
        foreach (var mapping in prefabMappings)
        {
            if (mapping.typeId == typeId)
            {
                return mapping.prefab;
            }
        }
        return null;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Get a spawned lootable by its spawn ID
    /// </summary>
    public static LootableItem GetLootable(uint spawnId)
    {
        return spawnedLootables.TryGetValue(spawnId, out var pickup) ? pickup : null;
    }

    /// <summary>
    /// Get all currently spawned lootables
    /// </summary>
    public static IEnumerable<LootableItem> GetAllLootables()
    {
        return spawnedLootables.Values;
    }

    #endregion
}
