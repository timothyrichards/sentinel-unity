use crate::modules::admin::require_admin;
use crate::modules::entity::entity;
use crate::modules::inventory::inventory_add_item_internal;
use crate::modules::player::player;
use crate::types::DbVector3;
use spacetimedb::{ReducerContext, Table};

/// Defines a type of lootable item (e.g., "Rock", "Stick")
/// The type_id also serves as the item_id for inventory
#[spacetimedb::table(name = lootable_item_type, public)]
pub struct LootableItemType {
    #[primary_key]
    pub type_id: u32,
    /// Display name of the item
    pub name: String,
    /// Description of the item
    pub description: String,
    /// Weight of a single item
    pub weight: f32,
    /// Quantity given when looted
    pub quantity: u32,
    /// Time in microseconds before the item respawns
    pub respawn_time_us: i64,
    /// Maximum distance from which player can loot this item
    pub loot_distance: f32,
}

/// A specific spawn point for a lootable item in the world
#[derive(Clone)]
#[spacetimedb::table(name = lootable_spawn, public)]
pub struct LootableSpawn {
    #[primary_key]
    #[auto_inc]
    pub spawn_id: u32,
    /// References LootableItemType.type_id
    pub type_id: u32,
    /// World position of this spawn
    pub position: DbVector3,
    /// Rotation in euler angles
    pub rotation: DbVector3,
    /// Whether this item has been looted
    pub is_looted: bool,
    /// Timestamp when the item was looted (for respawn calculation)
    pub looted_at_us: i64,
}

/// Initialize default lootable item types and spawns
pub fn lootable_item_type_init(ctx: &ReducerContext) -> Result<(), String> {
    // Branch - type_id 0 (also used as item_id)
    ctx.db.lootable_item_type().insert(LootableItemType {
        type_id: 0,
        name: "Branch".to_string(),
        description: "A fallen tree branch. Useful for crafting.".to_string(),
        weight: 0.5,
        quantity: 5,
        respawn_time_us: 30_000_000, // 30 seconds
        loot_distance: 1.5,
    });

    // Rock - type_id 1 (also used as item_id)
    ctx.db.lootable_item_type().insert(LootableItemType {
        type_id: 1,
        name: "Rock".to_string(),
        description: "A small stone. Can be used for tools or building.".to_string(),
        weight: 1.0,
        quantity: 5,
        respawn_time_us: 30_000_000, // 30 seconds
        loot_distance: 1.5,
    });

    // Tree - type_id 2 (also used as item_id)
    ctx.db.lootable_item_type().insert(LootableItemType {
        type_id: 2,
        name: "Tree".to_string(),
        description: "A harvestable tree. Provides wood for construction.".to_string(),
        weight: 2.0,
        quantity: 15,
        respawn_time_us: 30_000_000, // 30 seconds
        loot_distance: 2.5,
    });

    log::info!("Initialized default lootable item types");

    // Default spawn: Branch (type_id 0)
    ctx.db.lootable_spawn().insert(LootableSpawn {
        spawn_id: 0, // auto_inc will assign
        type_id: 0,  // Branch
        position: DbVector3 {
            x: -220.332733,
            y: -29.7000008,
            z: 254.894043,
        },
        rotation: DbVector3 {
            x: 0.0,
            y: 86.239,
            z: 0.0,
        },
        is_looted: false,
        looted_at_us: 0,
    });

    // Default spawn: Rock (type_id 1)
    ctx.db.lootable_spawn().insert(LootableSpawn {
        spawn_id: 0, // auto_inc will assign
        type_id: 1,  // Rock
        position: DbVector3 {
            x: -223.271866,
            y: -29.7525482,
            z: 254.474808,
        },
        rotation: DbVector3 {
            x: 0.0,
            y: 0.0,
            z: -90.0,
        },
        is_looted: false,
        looted_at_us: 0,
    });

    log::info!("Initialized default lootable spawns");
    Ok(())
}

/// Creates a new lootable item type definition (admin only)
#[spacetimedb::reducer]
pub fn lootable_create_type(
    ctx: &ReducerContext,
    type_id: u32,
    name: String,
    description: String,
    weight: f32,
    quantity: u32,
    respawn_time_seconds: f32,
    loot_distance: f32,
) -> Result<(), String> {
    require_admin(ctx)?;
    let respawn_time_us = (respawn_time_seconds * 1_000_000.0) as i64;
    let item_type = LootableItemType {
        type_id,
        name,
        description,
        weight,
        quantity,
        respawn_time_us,
        loot_distance,
    };
    ctx.db.lootable_item_type().insert(item_type);
    log::info!("Created lootable item type with type_id: {}", type_id);
    Ok(())
}

/// Creates a spawn point for a lootable item (admin only)
#[spacetimedb::reducer]
pub fn lootable_create_spawn(
    ctx: &ReducerContext,
    type_id: u32,
    position: DbVector3,
    rotation: DbVector3,
) -> Result<(), String> {
    require_admin(ctx)?;

    // Verify the type exists
    let item_type = ctx.db.lootable_item_type().type_id().find(type_id);
    if item_type.is_none() {
        return Err(format!("Lootable item type {} does not exist", type_id));
    }

    let spawn = LootableSpawn {
        spawn_id: 0, // auto_inc
        type_id,
        position,
        rotation,
        is_looted: false,
        looted_at_us: 0,
    };
    ctx.db.lootable_spawn().insert(spawn);
    log::info!("Created lootable spawn for type_id: {}", type_id);
    Ok(())
}

/// Player attempts to loot an item
/// Server validates: item exists, player in range, not on cooldown, and adds to inventory
#[spacetimedb::reducer]
pub fn lootable_loot(ctx: &ReducerContext, spawn_id: u32) -> Result<(), String> {
    log::info!(
        "Player {:?} attempting to loot spawn {}",
        ctx.sender,
        spawn_id
    );

    // Find the player
    let player = ctx
        .db
        .player()
        .identity()
        .find(ctx.sender)
        .ok_or("Player not found")?;

    // Get the player's entity for position
    let player_entity = ctx
        .db
        .entity()
        .entity_id()
        .find(&player.entity_id)
        .ok_or("Player entity not found")?;

    // Find the spawn point
    let spawn = ctx.db.lootable_spawn().spawn_id().find(spawn_id);
    let mut spawn = match spawn {
        Some(s) => s,
        None => return Err(format!("Lootable spawn {} does not exist", spawn_id)),
    };

    // Get the item type (needed for loot_distance and other checks)
    let item_type = ctx
        .db
        .lootable_item_type()
        .type_id()
        .find(spawn.type_id)
        .ok_or("Lootable item type not found")?;

    // Check if player is in range using item's loot_distance
    let dist = player_entity.position.distance(&spawn.position);
    if dist > item_type.loot_distance {
        return Err(format!(
            "Too far away to loot. Distance: {:.1}, Range: {:.1}",
            dist, item_type.loot_distance
        ));
    }

    let current_time = ctx.timestamp.to_micros_since_unix_epoch();

    // Check if already looted (on cooldown)
    if spawn.is_looted {
        // Calculate if respawn time has passed
        let time_since_looted = current_time - spawn.looted_at_us;

        if time_since_looted < item_type.respawn_time_us {
            let remaining_seconds =
                (item_type.respawn_time_us - time_since_looted) as f64 / 1_000_000.0;
            return Err(format!(
                "Item is on cooldown. {:.1} seconds remaining",
                remaining_seconds
            ));
        }

        // Respawn time has passed, allow looting
    }

    // Mark as looted with current timestamp
    spawn.is_looted = true;
    spawn.looted_at_us = current_time;
    ctx.db.lootable_spawn().spawn_id().update(spawn);

    // Add item to player's inventory (type_id is used as item_id)
    inventory_add_item_internal(ctx, ctx.sender, item_type.type_id, item_type.quantity)?;

    log::info!(
        "Player {:?} looted spawn {} ({})",
        ctx.sender,
        spawn_id,
        item_type.name
    );

    Ok(())
}

/// Check and respawn items that have passed their cooldown
/// This can be called periodically by a client or scheduled task
#[spacetimedb::reducer]
pub fn lootable_check_respawns(ctx: &ReducerContext) -> Result<(), String> {
    let current_time = ctx.timestamp.to_micros_since_unix_epoch();

    // Collect spawns that need respawning
    let spawns_to_respawn: Vec<_> = ctx
        .db
        .lootable_spawn()
        .iter()
        .filter(|spawn| {
            if !spawn.is_looted {
                return false;
            }

            // Get respawn time for this type
            if let Some(item_type) = ctx.db.lootable_item_type().type_id().find(spawn.type_id) {
                let time_since_looted = current_time - spawn.looted_at_us;
                time_since_looted >= item_type.respawn_time_us
            } else {
                false
            }
        })
        .collect();

    // Respawn them
    for spawn in spawns_to_respawn {
        let mut updated_spawn = spawn.clone();
        updated_spawn.is_looted = false;
        updated_spawn.looted_at_us = 0;
        ctx.db.lootable_spawn().spawn_id().update(updated_spawn);
        log::info!("Respawned lootable spawn {}", spawn.spawn_id);
    }

    Ok(())
}

/// Delete a specific spawn point
#[spacetimedb::reducer]
pub fn lootable_delete_spawn(ctx: &ReducerContext, spawn_id: u32) -> Result<(), String> {
    let spawn = ctx.db.lootable_spawn().spawn_id().find(spawn_id);
    if spawn.is_none() {
        return Err(format!("Lootable spawn {} does not exist", spawn_id));
    }

    ctx.db.lootable_spawn().spawn_id().delete(spawn_id);
    log::info!("Deleted lootable spawn {}", spawn_id);
    Ok(())
}

/// Delete all spawns of a specific type
#[spacetimedb::reducer]
pub fn lootable_delete_all_spawns_of_type(
    ctx: &ReducerContext,
    type_id: u32,
) -> Result<(), String> {
    let spawns_to_delete: Vec<_> = ctx
        .db
        .lootable_spawn()
        .iter()
        .filter(|s| s.type_id == type_id)
        .map(|s| s.spawn_id)
        .collect();

    for spawn_id in &spawns_to_delete {
        ctx.db.lootable_spawn().spawn_id().delete(*spawn_id);
    }

    log::info!(
        "Deleted {} spawns of type {}",
        spawns_to_delete.len(),
        type_id
    );
    Ok(())
}
