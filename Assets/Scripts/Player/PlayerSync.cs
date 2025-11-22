using UnityEngine;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using ThirdPersonCamera;
using DBEntity = SpacetimeDB.Types.Entity;

public class PlayerSync : MonoBehaviour
{
    [Header("Runtime")]
    public static readonly Dictionary<Identity, PlayerEntity> playerObjects = new();

    [Header("References")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private CameraController playerCamera;
    [SerializeField] private HealthDisplay playerHealthDisplay;

    private void OnEnable()
    {
        // Subscribe to SpacetimeDB connection events
        SpacetimeManager.OnConnected += HandleConnected;

        // Subscribe to player and entity table events
        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.Player.OnInsert += HandlePlayerJoined;
            SpacetimeManager.Conn.Db.Player.OnDelete += HandlePlayerLeft;
            SpacetimeManager.Conn.Db.Player.OnUpdate += HandlePlayerUpdated;
            SpacetimeManager.Conn.Db.Entity.OnInsert += HandleEntityInserted;
            SpacetimeManager.Conn.Db.Entity.OnUpdate += HandleEntityUpdated;
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from SpacetimeDB connection events
        SpacetimeManager.OnConnected -= HandleConnected;

        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.Player.OnInsert -= HandlePlayerJoined;
            SpacetimeManager.Conn.Db.Player.OnDelete -= HandlePlayerLeft;
            SpacetimeManager.Conn.Db.Player.OnUpdate -= HandlePlayerUpdated;
            SpacetimeManager.Conn.Db.Entity.OnInsert -= HandleEntityInserted;
            SpacetimeManager.Conn.Db.Entity.OnUpdate -= HandleEntityUpdated;
        }
    }

    private void HandleConnected()
    {
        // Subscribe to table events now that we're connected
        SpacetimeManager.Conn.Db.Player.OnInsert += HandlePlayerJoined;
        SpacetimeManager.Conn.Db.Player.OnDelete += HandlePlayerLeft;
        SpacetimeManager.Conn.Db.Player.OnUpdate += HandlePlayerUpdated;
        SpacetimeManager.Conn.Db.Entity.OnInsert += HandleEntityInserted;
        SpacetimeManager.Conn.Db.Entity.OnUpdate += HandleEntityUpdated;

        // Add subscriptions for online players and their entities
        SpacetimeManager.Instance.AddSubscription("select * from player where online = true");
        SpacetimeManager.Instance.AddSubscription("select * from entity");

        // Send a player connected reducer to the server
        SpacetimeManager.Conn.Reducers.PlayerConnected();
    }

    private void HandlePlayerJoined(EventContext context, SpacetimeDB.Types.Player playerData)
    {
        if (playerObjects.ContainsKey(playerData.Identity))
            return;

        // Get the associated entity to get position/rotation
        DBEntity entity = null;
        foreach (var e in SpacetimeManager.Conn.Db.Entity.Iter())
        {
            if (e.EntityId == playerData.EntityId)
            {
                entity = e;
                break;
            }
        }

        if (entity == null)
        {
            // Entity not synced yet - this can happen if player table update arrives before entity table update
            // We'll spawn the player when the entity arrives via HandleEntityUpdated
            Debug.LogWarning($"Player {playerData.Identity} entity not yet synced (EntityId: {playerData.EntityId}). Will spawn when entity arrives.");
            return;
        }

        SpawnPlayer(playerData, entity);
    }

    private void SpawnPlayer(SpacetimeDB.Types.Player playerData, DBEntity entity)
    {
        if (playerObjects.ContainsKey(playerData.Identity))
            return;

        // Instantiate the player object
        var position = new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
        var rotation = Quaternion.Euler(entity.Rotation.X, entity.Rotation.Y, entity.Rotation.Z);
        var playerObject = Instantiate(playerPrefab, position, rotation);
        var playerEntity = playerObject.GetComponent<PlayerEntity>();

        // Store the player object reference
        playerObjects[playerData.Identity] = playerEntity;

        // Set the owner identity and entity ID
        playerEntity.ownerIdentity = playerData.Identity;
        playerEntity.entityId = playerData.EntityId;

        // Set the inventory
        playerEntity.inventory = InventorySync.GetInventory(playerEntity).Items;

        // Configure the player object based on whether it's the local player
        playerEntity.Configure(playerData, entity, playerCamera, playerHealthDisplay);

        Debug.Log($"Spawned player {playerData.Identity} at {position}");
    }

    private void HandlePlayerLeft(EventContext context, SpacetimeDB.Types.Player player)
    {
        if (playerObjects.TryGetValue(player.Identity, out PlayerEntity playerEntity))
        {
            Destroy(playerEntity.gameObject);
            playerObjects.Remove(player.Identity);
        }
    }

    private void HandlePlayerUpdated(EventContext context, SpacetimeDB.Types.Player oldData, SpacetimeDB.Types.Player newData)
    {
        if (playerObjects.TryGetValue(newData.Identity, out PlayerEntity playerEntity))
        {
            playerEntity.UpdateFromPlayerData(oldData, newData);
        }
    }

    private void HandleEntityInserted(EventContext context, DBEntity entityData)
    {
        // When a new entity is inserted, check if there's a player waiting for it
        foreach (var player in SpacetimeManager.Conn.Db.Player.Iter())
        {
            if (player.EntityId == entityData.EntityId && !playerObjects.ContainsKey(player.Identity))
            {
                // Found a player that was waiting for this entity - spawn them now
                SpawnPlayer(player, entityData);
                return;
            }
        }
    }

    private void HandleEntityUpdated(EventContext context, DBEntity oldData, DBEntity newData)
    {
        // Find which player owns this entity
        foreach (var player in SpacetimeManager.Conn.Db.Player.Iter())
        {
            if (player.EntityId == newData.EntityId)
            {
                if (playerObjects.TryGetValue(player.Identity, out PlayerEntity playerEntity))
                {
                    // Player already spawned - update it
                    playerEntity.UpdateFromEntityData(oldData, newData);
                }
                else
                {
                    // Player not spawned yet - this entity just arrived, try spawning now
                    SpawnPlayer(player, newData);
                }
                return;
            }
        }
    }
}
