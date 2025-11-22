using UnityEngine;
using SpacetimeDB.Types;

/// <summary>
/// Debug tool for spawning lootables where the camera is pointing.
/// Press X to spawn a random branch or rock on the terrain.
/// </summary>
public class LootableSpawner : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private KeyCode spawnKey = KeyCode.X;
    [SerializeField] private float maxRayDistance = 100f;
    [SerializeField] private LayerMask terrainLayer = ~0;

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
        {
            TrySpawnLootable();
        }
    }

    private void TrySpawnLootable()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("[LootableSpawner] No main camera found");
                return;
            }
        }

        if (SpacetimeManager.Conn == null || !SpacetimeManager.Conn.IsActive)
        {
            Debug.LogWarning("[LootableSpawner] Not connected to server");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, terrainLayer))
        {
            // Random type: 0 = Branch, 1 = Rock
            uint typeId = (uint)Random.Range(0, 2);

            DbVector3 position = new DbVector3
            {
                X = hit.point.x,
                Y = hit.point.y,
                Z = hit.point.z
            };

            DbVector3 rotation = new DbVector3
            {
                X = 0f,
                Y = Random.Range(0f, 360f),
                Z = 0f
            };

            SpacetimeManager.Conn.Reducers.LootableCreateSpawn(typeId, position, rotation);

            string typeName = typeId == 0 ? "Branch" : "Rock";
            Debug.Log($"[LootableSpawner] Spawned {typeName} at {hit.point}");
        }
        else
        {
            Debug.LogWarning("[LootableSpawner] No terrain hit");
        }
    }
}
