using UnityEngine;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

public class InventorySync : MonoBehaviour
{
    public static event Action<Identity, List<ItemRef>> OnInventoryChanged;

    private void OnEnable()
    {
        // Subscribe to SpacetimeDB connection events
        SpacetimeManager.OnConnected += HandleConnected;

        // Subscribe to player table events
        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.Inventory.OnInsert += HandleInventoryInserted;
            SpacetimeManager.Conn.Db.Inventory.OnUpdate += HandleInventoryUpdated;
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from SpacetimeDB connection events
        SpacetimeManager.OnConnected -= HandleConnected;

        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.Inventory.OnInsert -= HandleInventoryInserted;
            SpacetimeManager.Conn.Db.Inventory.OnUpdate -= HandleInventoryUpdated;
        }
    }

    private void HandleConnected()
    {
        // Subscribe to table events now that we're connected
        SpacetimeManager.Conn.Db.Inventory.OnInsert += HandleInventoryInserted;
        SpacetimeManager.Conn.Db.Inventory.OnUpdate += HandleInventoryUpdated;

        // Add subscription for online players
        SpacetimeManager.Instance.AddSubscription("select * from inventory");
    }

    private void InvokeInventoryChanged(PlayerEntity playerEntity)
    {
        var inventory = GetInventory(playerEntity);
        OnInventoryChanged?.Invoke(playerEntity.ownerIdentity, inventory.Items);
    }

    private void HandleInventoryInserted(EventContext context, Inventory inventory)
    {
        if (PlayerSync.playerObjects.TryGetValue(inventory.Identity, out PlayerEntity playerEntity))
        {
            playerEntity.inventory = inventory.Items;

            InvokeInventoryChanged(playerEntity);
        }
    }

    private void HandleInventoryUpdated(EventContext context, Inventory oldData, Inventory newData)
    {
        if (PlayerSync.playerObjects.TryGetValue(newData.Identity, out PlayerEntity playerEntity))
        {
            playerEntity.inventory = newData.Items;

            InvokeInventoryChanged(playerEntity);
        }
    }

    public static Inventory GetInventory(PlayerEntity playerEntity)
    {
        var inventory = SpacetimeManager.Conn.Db.Inventory.Identity.Find(playerEntity.ownerIdentity);

        return inventory;
    }

    public static ItemRef GetItem(PlayerEntity playerEntity, uint itemId)
    {
        var inventory = GetInventory(playerEntity);

        return inventory.Items.Find(i => i.Id == itemId);
    }
}