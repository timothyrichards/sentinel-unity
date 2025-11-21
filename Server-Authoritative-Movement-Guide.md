# Server-Authoritative Movement Validation for Unity + SpacetimeDB

This implementation provides server-side movement validation similar to the Unreal Engine approach mentioned in the SpacetimeDB Discord. It validates that players:
1. Only move on walkable surfaces (NavMesh validation)
2. Don't exceed speed limits (anti-speedhack)
3. Can't teleport or clip through geometry

## Architecture Overview

### Server-Side Components

**NavMesh Grid Storage** (`Server/src/modules/navmesh.rs`)
- Samples Unity's NavMesh in a grid pattern
- Uses spatial hashing (grid coordinates) for fast lookups
- Stores walkable points in SpacetimeDB with configurable cell size and Z-tolerance

**Position Validation** (`Server/src/modules/player.rs`)
- Validates every position update against the NavMesh grid
- Checks if position is within Z-tolerance of a walkable surface
- Searches a 3x3 grid of cells around the player's position for efficiency

**Speed Validation** (`Server/src/modules/player.rs`)
- Tracks timestamp of last position update
- Calculates distance traveled and time delta
- Rejects moves exceeding MAX_SPEED (default: 10 units/second)
- Automatically rolls back to last valid position on rejection

### Client-Side Components

**NavMesh Exporter** (`Assets/Scripts/Editor/NavMeshExporter.cs`)
- Unity Editor tool to sample your baked NavMesh
- Exports grid data to JSON format
- Configurable grid cell size and bounds

**NavMesh Uploader** (`Assets/Scripts/NavMeshUploader.cs`)
- Uploads NavMesh data to SpacetimeDB server
- Batched uploads to avoid overwhelming the connection
- One-time setup per NavMesh

**Movement Reconciliation** (`Assets/Scripts/Player/MovementReconciliation.cs`)
- Client-side prediction with server reconciliation
- Smoothly corrects position when server rejects moves
- Maintains pending move queue for rollback

## Setup Instructions

### Step 1: Bake Your NavMesh

1. In Unity, set up your scene with terrain/geometry
2. Open `Window > AI > Navigation`
3. Configure your NavMesh settings (agent size, walkable slopes, etc.)
4. Click "Bake" to generate the NavMesh

### Step 2: Sample and Export NavMesh

1. Open `Tools > NavMesh Exporter` from the Unity menu
2. Configure settings:
   - **Grid Cell Size**: 1.0 (smaller = more accurate but more data)
   - **Z Tolerance**: 2.0 (vertical distance tolerance for validation)
   - **Bounds**: Set min/max to cover your playable area
3. Click "Sample NavMesh and Export"
4. This creates `Assets/NavMeshData.json`

### Step 3: Upload NavMesh to Server

1. Add the `NavMeshUploader` component to a GameObject in your scene
2. Set the `navMeshDataPath` to your exported JSON file
3. Either:
   - Check "Upload On Start" to auto-upload when connecting
   - Or call `GetComponent<NavMeshUploader>().Upload()` manually

**Note**: Only upload once per server! The data persists in SpacetimeDB.

### Step 4: Add Reconciliation to Player

1. Add the `MovementReconciliation` component to your player prefab
2. The `ThirdPersonController` and `PlayerEntity` scripts are already integrated
3. Configure reconciliation settings:
   - **Reconciliation Speed**: 10.0 (how fast to correct position)
   - **Reconciliation Threshold**: 0.1 (minimum distance to trigger correction)

### Step 5: Deploy Updated Server Module

1. Navigate to your server directory: `cd Server`
2. Rebuild and publish: `spacetime publish`
3. The server will now validate all movement

## How It Works

### Position Update Flow

```
1. Client moves player locally (immediate, no lag)
2. Client sends position to server via PlayerUpdate reducer
3. Server validates:
   a. Is position on NavMesh? (checks grid with spatial hash)
   b. Is speed valid? (distance/time < MAX_SPEED)
4. If valid:
   - Server updates position
   - Broadcasts to all clients
5. If invalid:
   - Server rejects move (returns error)
   - Server resets player to last valid position
   - Client receives correction and reconciles smoothly
```

### Validation Details

**NavMesh Validation**:
- Converts world position to grid coordinates
- Checks 3x3 grid cells (target + 8 neighbors)
- Validates horizontal distance < 1.5 * cell_size
- Validates vertical distance < z_tolerance
- O(1) lookup via spatial hashing

**Speed Validation**:
- Calculates: `speed = distance / time_delta`
- Compares to MAX_SPEED constant (10 units/sec default)
- Uses server timestamp for accuracy (client can't fake time)

**Rollback on Failure**:
- Server maintains `last_valid_position` per player
- On rejection, server immediately updates entity position to last valid
- Client reconciliation smoothly interpolates back

## Configuration

### Server-Side Settings

**Speed Limit** (`Server/src/modules/player.rs:135`)
```rust
const MAX_SPEED: f32 = 10.0; // units per second
```

**NavMesh Config** (set via reducer)
```rust
// Upload config when uploading NavMesh data
navmesh_set_config(cell_size: 1.0, z_tolerance: 2.0)
```

### Client-Side Settings

**Reconciliation** (`MovementReconciliation.cs`)
```csharp
reconciliationSpeed = 10f;      // Speed of position correction
reconciliationThreshold = 0.1f; // Min distance to trigger correction
```

**Movement Speed** (`ThirdPersonController.cs:16`)
```csharp
moveSpeed = 5f; // Must be < MAX_SPEED on server!
```

## Debugging

### Check NavMesh Upload

Call the stats reducer to see how many points were uploaded:
```csharp
SpacetimeManager.Conn.Reducers.NavmeshGetStats();
```

Check server logs for output like:
```
NavMesh Stats - Points: 15420, Cell Size: 1.0, Z Tolerance: 2.0
```

### Monitor Position Rejections

Server logs will show rejected moves:
```
[WARN] Player <identity> attempted to move to invalid position (x, y, z)
[WARN] Player <identity> moving too fast: 15.2 units/sec (max: 10.0)
```

Client logs will show reconciliation:
```
Position mismatch detected! Client: (x,y,z), Server: (x,y,z), Distance: 2.45
Move rejected by server: Invalid speed - moving too fast
```

### Visualize Reconciliation

Add this to `MovementReconciliation.cs` Update():
```csharp
void OnDrawGizmos() {
    if (needsReconciliation) {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(serverAuthorityPosition, 0.5f);
        Gizmos.DrawLine(transform.position, serverAuthorityPosition);
    }
}
```

## Performance Considerations

### NavMesh Grid Size

- **Smaller cells** (0.5): More accurate, more data to upload/store
- **Larger cells** (2.0): Less accurate, less data, faster lookups
- Recommended: 1.0 for most games

### Upload Optimization

- Upload happens once per NavMesh change
- Data persists in SpacetimeDB
- Use batching (default: 100 points per batch)
- Total upload time: ~10-30 seconds for typical maps

### Runtime Performance

- Position validation: O(1) via spatial hash + index lookups
- Checks only 9 grid cells (3x3 around player)
- No expensive distance calculations to all points
- Typical cost: <1ms per validation

## Anti-Cheat Capabilities

This system prevents:
- ✅ **Speedhacking**: Speed validation with server timestamps
- ✅ **Teleporting**: Large position changes rejected
- ✅ **Noclip/Flying**: NavMesh validation ensures walkable surface
- ✅ **Position spoofing**: Server is authoritative, client corrections forced

This does NOT prevent:
- ❌ **Aimbots**: Client-side rendering issue
- ❌ **ESP/Wallhacks**: Client-side rendering issue
- ❌ **Packet manipulation**: Use HTTPS/TLS (SpacetimeDB default)

## Comparison to Full Physics Replication

| Aspect | NavMesh Validation | Full Physics Replication |
|--------|-------------------|--------------------------|
| Setup Complexity | Low (export NavMesh once) | High (replicate all colliders) |
| Performance | Excellent (spatial hash) | Moderate (full simulation) |
| Accuracy | High for MMOs | Perfect collision |
| Server Load | Minimal | Significant |
| Best For | MMOs, RPGs, large worlds | Competitive shooters, physics games |

## Future Enhancements

Potential improvements:
1. **Dynamic NavMesh**: Update grid when terrain changes
2. **Per-Player Speed**: Different movement speeds per character class
3. **Jump Validation**: Validate vertical movement separately
4. **Pathfinding**: Use same grid for server-side AI pathfinding
5. **Analytics**: Track rejection rates to detect potential cheaters

## Credits

Implementation inspired by the Unreal Engine approach shared in the SpacetimeDB Discord by a community member who implemented server-authoritative movement using Unreal's NavMesh sampling with `ProjectPointToNavigation`.

## License

Part of the Sentinel project.
