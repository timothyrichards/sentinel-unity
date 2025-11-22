using UnityEngine;
using UnityEngine.InputSystem;
using SpacetimeDB.Types;

[RequireComponent(typeof(SphereCollider))]
public class LootableItem : MonoBehaviour
{
    [Header("Item Info")]
    public uint itemId;
    public Sprite itemIcon;
    public string itemName;
    public string itemDescription;
    public float itemWeight = 1f;
    public uint quantity = 1;

    [Header("Server Sync")]
    [Tooltip("The spawn ID from the server's lootable_spawn table.")]
    public uint spawnId;
    [Tooltip("The type ID for looking up loot distance. Set automatically by LootableSync.")]
    public uint typeId;

    [Header("Fallback Settings")]
    [Tooltip("Used if server data not available")]
    [SerializeField] private float fallbackLootDistance = 3f;

    [Header("Range Collider")]
    [Tooltip("Sphere collider resized based on server loot distance")]
    [SerializeField] private SphereCollider rangeCollider;

    private PlayerInputActions inputActions;
    private bool playerInRange = false;
    private bool isLooted = false;
    private Renderer[] renderers;
    private Collider[] colliders;
    private float lastLootDistance = -1f;

    private void Awake()
    {
        inputActions = new PlayerInputActions();
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();

        if (rangeCollider == null)
        {
            rangeCollider = GetComponent<SphereCollider>();
        }
    }

    private void OnEnable()
    {
        inputActions.Player.PickUp.performed += OnPickUp;
        inputActions.Enable();

        // Subscribe to server updates
        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.LootableSpawn.OnUpdate += HandleSpawnUpdated;
        }
        SpacetimeManager.OnConnected += HandleConnected;
    }

    private void OnDisable()
    {
        inputActions.Player.PickUp.performed -= OnPickUp;
        inputActions.Disable();

        if (SpacetimeManager.Conn != null)
        {
            SpacetimeManager.Conn.Db.LootableSpawn.OnUpdate -= HandleSpawnUpdated;
        }
        SpacetimeManager.OnConnected -= HandleConnected;
    }

    private void Update()
    {
        if (isLooted) return;

        CheckPlayerProximity();
    }

    private void CheckPlayerProximity()
    {
        if (PlayerEntity.LocalPlayer == null)
        {
            SetInRange(false);
            return;
        }

        float lootDistance = GetLootDistance();
        float distance = Vector3.Distance(transform.position, PlayerEntity.LocalPlayer.transform.position);

        SetInRange(distance <= lootDistance);
    }

    private float GetLootDistance()
    {
        if (SpacetimeManager.Conn == null)
            return fallbackLootDistance;

        var itemType = SpacetimeManager.Conn.Db.LootableItemType.TypeId.Find(typeId);
        float distance = itemType?.LootDistance ?? fallbackLootDistance;

        // Update range collider if distance changed
        if (!Mathf.Approximately(distance, lastLootDistance))
        {
            rangeCollider.radius = distance;
            lastLootDistance = distance;
        }

        return distance;
    }

    private void SetInRange(bool inRange)
    {
        if (playerInRange == inRange)
            return;

        playerInRange = inRange;

        if (inRange)
            ShowPickupPrompt();
        else
            HidePickupPrompt();
    }

    private void HandleConnected()
    {
        SpacetimeManager.Conn.Db.LootableSpawn.OnUpdate += HandleSpawnUpdated;
    }

    private void HandleSpawnUpdated(EventContext ctx, LootableSpawn oldSpawn, LootableSpawn newSpawn)
    {
        // Only handle updates for our spawn ID
        if (newSpawn.SpawnId != spawnId)
            return;

        Debug.Log($"[LootableItem] Spawn {spawnId} updated: IsLooted={newSpawn.IsLooted}");

        if (newSpawn.IsLooted && !isLooted)
        {
            OnLooted();
        }
        else if (!newSpawn.IsLooted && isLooted)
        {
            OnRespawned();
        }
    }

    private void OnPickUp(InputAction.CallbackContext context)
    {
        if (isLooted) return;

        if (context.ReadValue<float>() > 0.5f && playerInRange)
        {
            Debug.Log($"Picking up {itemName}");
            PickupItem();
        }
    }

    private void PickupItem()
    {
        if (isLooted) return;

        // Always use server-side loot validation (validates range, cooldown)
        SpacetimeManager.Conn.Reducers.LootableLoot(spawnId);
    }

    private void OnLooted()
    {
        isLooted = true;
        playerInRange = false;
        HidePickupPrompt();
        SetVisible(false);
        Debug.Log($"[LootableItem] {itemName} looted and hidden");
    }

    private void OnRespawned()
    {
        isLooted = false;
        SetVisible(true);
        Debug.Log($"[LootableItem] {itemName} respawned and visible");
    }

    private void SetVisible(bool visible)
    {
        foreach (var renderer in renderers)
        {
            renderer.enabled = visible;
        }
        foreach (var col in colliders)
        {
            col.enabled = visible;
        }
    }

    private void ShowPickupPrompt()
    {
        InventoryUI.Instance.ShowPickupPrompt(itemName);
    }

    private void HidePickupPrompt()
    {
        InventoryUI.Instance.HidePickupPrompt();
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize loot distance in editor
        float distance = Application.isPlaying ? GetLootDistance() : fallbackLootDistance;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, distance);
    }
}
