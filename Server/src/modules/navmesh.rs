use spacetimedb::{ReducerContext, Table};

/// Represents a single walkable grid cell in the NavMesh
/// Uses spatial hashing for fast position lookups
#[spacetimedb::table(name = navmesh_grid, public)]
pub struct NavMeshGrid {
    #[primary_key]
    #[auto_inc]
    pub id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// Grid X coordinate for spatial hashing
    #[index(btree)]
    pub grid_x: i32,
    /// Grid Z coordinate for spatial hashing
    #[index(btree)]
    pub grid_z: i32,
}

/// Configuration for NavMesh validation
#[spacetimedb::table(name = navmesh_config, public)]
pub struct NavMeshConfig {
    #[primary_key]
    pub id: u32,
    pub cell_size: f32,
    pub z_tolerance: f32,
    pub bounds_min_x: f32,
    pub bounds_min_z: f32,
}

/// Upload NavMesh grid data to the database
/// This should be called once during server initialization
#[spacetimedb::reducer]
pub fn navmesh_upload_point(
    ctx: &ReducerContext,
    x: f32,
    y: f32,
    z: f32,
    grid_x: i32,
    grid_z: i32,
) -> Result<(), String> {
    ctx.db.navmesh_grid().insert(NavMeshGrid {
        id: 0,
        x,
        y,
        z,
        grid_x,
        grid_z,
    });

    Ok(())
}

/// Set or update the NavMesh configuration
#[spacetimedb::reducer]
pub fn navmesh_set_config(
    ctx: &ReducerContext,
    cell_size: f32,
    z_tolerance: f32,
    bounds_min_x: f32,
    bounds_min_z: f32,
) -> Result<(), String> {
    // Check if config already exists
    if let Some(mut config) = ctx.db.navmesh_config().id().find(&0) {
        config.cell_size = cell_size;
        config.z_tolerance = z_tolerance;
        config.bounds_min_x = bounds_min_x;
        config.bounds_min_z = bounds_min_z;
        ctx.db.navmesh_config().id().update(config);
    } else {
        ctx.db.navmesh_config().insert(NavMeshConfig {
            id: 0,
            cell_size,
            z_tolerance,
            bounds_min_x,
            bounds_min_z,
        });
    }

    log::info!(
        "NavMesh config updated: cell_size={}, z_tolerance={}, bounds_min=({}, {})",
        cell_size, z_tolerance, bounds_min_x, bounds_min_z
    );
    Ok(())
}

/// Clear all NavMesh grid data
/// Use with caution - this deletes all walkable points
#[spacetimedb::reducer]
pub fn navmesh_clear_grid(ctx: &ReducerContext) -> Result<(), String> {
    // Delete all grid points
    let points: Vec<_> = ctx.db.navmesh_grid().iter().collect();
    for point in points {
        ctx.db.navmesh_grid().id().delete(&point.id);
    }

    log::info!("NavMesh grid cleared");
    Ok(())
}

/// Validate if a position is on a walkable surface
/// Returns true if the position is within z_tolerance of a valid NavMesh point
pub fn is_position_valid(ctx: &ReducerContext, x: f32, y: f32, z: f32) -> bool {
    // Get config
    let config = match ctx.db.navmesh_config().id().find(&0) {
        Some(cfg) => cfg,
        None => {
            log::warn!("NavMesh config not found, position validation disabled");
            return true; // Allow movement if no config
        }
    };

    // Calculate grid coordinates for this position (matching Unity's export logic)
    let grid_x = ((x - config.bounds_min_x) / config.cell_size).floor() as i32;
    let grid_z = ((z - config.bounds_min_z) / config.cell_size).floor() as i32;

    // Check the target cell and adjacent cells (3x3 grid)
    for dx in -1..=1 {
        for dz in -1..=1 {
            let check_grid_x = grid_x + dx;
            let check_grid_z = grid_z + dz;

            // Find all points in this grid cell
            let points: Vec<_> = ctx
                .db
                .navmesh_grid()
                .iter()
                .filter(|p| p.grid_x == check_grid_x && p.grid_z == check_grid_z)
                .collect();

            // Check if any point is within tolerance
            for point in points {
                let horizontal_dist_sq = (x - point.x).powi(2) + (z - point.z).powi(2);
                let vertical_dist = (y - point.y).abs();

                // Check if position is within horizontal cell and vertical tolerance
                if horizontal_dist_sq <= (config.cell_size * 1.5).powi(2)
                    && vertical_dist <= config.z_tolerance
                {
                    return true;
                }
            }
        }
    }

    false
}

/// Get statistics about the NavMesh grid
#[spacetimedb::reducer]
pub fn navmesh_get_stats(ctx: &ReducerContext) -> Result<(), String> {
    let point_count = ctx.db.navmesh_grid().iter().count();
    let config = ctx.db.navmesh_config().id().find(&0);

    if let Some(cfg) = config {
        log::info!(
            "NavMesh Stats - Points: {}, Cell Size: {}, Z Tolerance: {}",
            point_count,
            cfg.cell_size,
            cfg.z_tolerance
        );
    } else {
        log::info!("NavMesh Stats - Points: {}, No config set", point_count);
    }

    Ok(())
}
