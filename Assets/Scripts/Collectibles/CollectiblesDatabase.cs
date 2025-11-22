using UnityEngine;
using System.Collections.Generic;
using SpacetimeDB.Types;

[CreateAssetMenu(fileName = "CollectiblesDatabase", menuName = "Inventory/Collectibles Database")]
public class CollectiblesDatabase : ScriptableObject
{
    [SerializeField]
    private List<LootableItem> collectiblePrefabs = new();

    // Dictionaries for quick lookup, populated on validation
    private Dictionary<uint, LootableItem> itemMap = new();

    private void OnValidate()
    {
        RebuildLookupMaps();
    }

    private void RebuildLookupMaps()
    {
        itemMap.Clear();

        foreach (var item in collectiblePrefabs)
        {
            if (item == null) continue;

            // Add to item map
            itemMap[item.itemId] = item;
        }
    }

    public void AddCollectible(LootableItem item)
    {
        // Check if item ID already exists
        if (itemMap.ContainsKey(item.itemId))
        {
            Debug.LogError($"Item ID {item.itemId} already exists in the database!");
            return;
        }

        collectiblePrefabs.Add(item);
        RebuildLookupMaps();
    }

    public void RemoveCollectible(LootableItem item)
    {
        collectiblePrefabs.Remove(item);
        RebuildLookupMaps();
    }

    public void RemoveNullEntries()
    {
        collectiblePrefabs.RemoveAll(item => item == null);
        RebuildLookupMaps();
    }

    public List<LootableItem> GetAllCollectibles()
    {
        return collectiblePrefabs;
    }

    public LootableItem GetCollectibleById(uint itemId)
    {
        if (itemMap.Count == 0)
        {
            RebuildLookupMaps();
        }

        return itemMap.TryGetValue(itemId, out var item) ? item : null;
    }

    public bool HasItemId(uint itemId)
    {
        if (itemMap.Count == 0)
        {
            RebuildLookupMaps();
        }

        return itemMap.ContainsKey(itemId);
    }
}
