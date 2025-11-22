using UnityEngine;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Handles client-side prediction and server reconciliation for player movement
/// When the server rejects a move, this smoothly corrects the client position
/// </summary>
public class MovementReconciliation : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float reconciliationSpeed = 10f;
    [Tooltip("Expected maximum ping in milliseconds (used to calculate position divergence tolerance)")]
    [SerializeField] private float maxExpectedPingMs = 300f; // Increased from 200ms
    [Tooltip("Fallback safety margin if server value unavailable (in meters)")]
    [SerializeField] private float fallbackSafetyMargin = 1.5f;

    private PlayerEntity playerEntity;
    private CharacterController characterController;
    private Vector3 serverAuthorityPosition;
    private bool needsReconciliation = false;
    private Queue<PendingMove> pendingMoves = new Queue<PendingMove>();
    private int nextMoveId = 0;
    private float currentMovementSpeed = 6f; // Updated from server

    /// <summary>
    /// Returns true if currently reconciling (client should not send position updates)
    /// </summary>
    public bool IsReconciling => needsReconciliation;

    /// <summary>
    /// Update the player's movement speed (called when server data changes)
    /// </summary>
    public void SetMovementSpeed(float speed)
    {
        currentMovementSpeed = speed;
    }

    /// <summary>
    /// Calculate reconciliation threshold based on player speed and network conditions
    /// Formula: (speed * RTT) + safety_margin
    /// Represents maximum expected position divergence due to network lag
    /// </summary>
    private float GetReconciliationThreshold()
    {
        float rttSeconds = maxExpectedPingMs / 1000f;
        float maxDivergence = currentMovementSpeed * rttSeconds;
        return maxDivergence + GetSafetyMargin();
    }

    /// <summary>
    /// Get safety margin from server, falling back to local value if unavailable
    /// </summary>
    private float GetSafetyMargin()
    {
        if (SpacetimeManager.Conn == null || SpacetimeManager.LocalIdentity == null)
            return fallbackSafetyMargin;

        var player = SpacetimeManager.Conn.Db.Player.Identity.Find(SpacetimeManager.LocalIdentity);
        return player?.ReconciliationSafetyMargin ?? fallbackSafetyMargin;
    }

    /// <summary>
    /// Completion threshold is smaller to avoid oscillation
    /// Set to 25% of trigger threshold
    /// </summary>
    private float GetCompletionThreshold()
    {
        return GetReconciliationThreshold() * 0.25f;
    }

    private struct PendingMove
    {
        public int moveId;
        public Vector3 position;
        public float timestamp;
    }

    private void Awake()
    {
        playerEntity = GetComponent<PlayerEntity>();
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!playerEntity.IsLocalPlayer()) return;

        // Perform reconciliation if needed
        if (needsReconciliation)
        {
            PerformReconciliation();
        }

        // Clean up old pending moves (older than 2 seconds)
        float currentTime = Time.time;
        while (pendingMoves.Count > 0 && currentTime - pendingMoves.Peek().timestamp > 2f)
        {
            pendingMoves.Dequeue();
        }
    }

    /// <summary>
    /// Call this when sending a position update to the server
    /// </summary>
    public void RecordMove(Vector3 position)
    {
        pendingMoves.Enqueue(new PendingMove
        {
            moveId = nextMoveId++,
            position = position,
            timestamp = Time.time
        });

        // Limit queue size
        if (pendingMoves.Count > 60) // Max 1 second of moves at 60fps
        {
            pendingMoves.Dequeue();
        }
    }

    /// <summary>
    /// Call this when the server confirms a position update
    /// </summary>
    public void OnServerPositionUpdate(Vector3 serverPosition)
    {
        serverAuthorityPosition = serverPosition;

        // Only reconcile if server rejected (large rollback snap)
        // Don't reconcile on normal network lag (small differences)
        float distance = Vector3.Distance(transform.position, serverPosition);
        const float ROLLBACK_THRESHOLD = 3.0f; // Only reconcile if server snapped us back 3+ meters

        if (distance > ROLLBACK_THRESHOLD)
        {
            Debug.LogWarning($"Server rejected movement! Client: {transform.position}, Server: {serverPosition}, Distance: {distance:F2}m - Starting reconciliation");
            needsReconciliation = true;
        }
    }

    /// <summary>
    /// Call this when the server rejects a move (invalid position/speed)
    /// </summary>
    public void OnMoveRejected(string reason)
    {
        Debug.LogWarning($"Move rejected by server: {reason}");

        // Clear pending moves since they're all invalid
        pendingMoves.Clear();

        // Force immediate reconciliation
        needsReconciliation = true;

        // The server should have sent back the corrected position via OnUpdate
        // If not, we'll snap to the last known server position
    }

    private void PerformReconciliation()
    {
        float distance = Vector3.Distance(transform.position, serverAuthorityPosition);
        float completionThreshold = GetCompletionThreshold();

        if (distance < completionThreshold)
        {
            // Close enough, reconciliation complete
            needsReconciliation = false;
            Debug.Log($"Reconciliation complete. Final distance: {distance:F2}m (threshold: {completionThreshold:F2}m)");
            return;
        }

        // For large distances (teleport-level), snap immediately
        if (distance > 5f)
        {
            Debug.Log($"Large position mismatch ({distance:F2}m), snapping to server position");
            SnapToPosition(serverAuthorityPosition);
            needsReconciliation = false;
            return;
        }

        // For smaller distances, smoothly interpolate
        Vector3 targetPosition = Vector3.Lerp(
            transform.position,
            serverAuthorityPosition,
            reconciliationSpeed * Time.deltaTime
        );

        // Move the character controller
        characterController.enabled = false;
        transform.position = targetPosition;
        characterController.enabled = true;
    }

    private void SnapToPosition(Vector3 position)
    {
        characterController.enabled = false;
        transform.position = position;
        characterController.enabled = true;
    }

    /// <summary>
    /// Get the current number of pending moves awaiting server confirmation
    /// </summary>
    public int GetPendingMoveCount()
    {
        return pendingMoves.Count;
    }
}
