use crate::modules::creative_camera::{creative_camera_create, creative_camera_set_enabled};
use crate::modules::entity::{entity, entity_create};
use crate::modules::inventory::inventory_create;
use crate::modules::navmesh::is_position_valid;
use crate::types::{DbVector2, DbVector3};
use spacetimedb::{Identity, ReducerContext, SpacetimeType, Table};

#[derive(SpacetimeType, Clone, Debug)]
pub struct DbAnimationState {
    pub horizontal_movement: f32,
    pub vertical_movement: f32,
    pub combo_count: u32,
    pub is_moving: bool,
    pub is_grounded: bool,
    pub is_jumping: bool,
    pub is_attacking: bool,
}

#[spacetimedb::table(name = player, public)]
pub struct Player {
    #[primary_key]
    pub identity: Identity,
    #[unique]
    #[auto_inc]
    pub player_id: u32,
    pub entity_id: u32,
    #[index(btree)]
    pub online: bool,
    pub look_direction: DbVector2,
    pub animation_state: DbAnimationState,
    /// Last valid position for rollback on invalid moves
    pub last_valid_position: DbVector3,
    /// Timestamp of last position update (for speed validation)
    pub last_update_timestamp: i64,
    /// Maximum allowed movement speed in units per second
    pub movement_speed: f32,
    /// Maximum distance for interacting with lootables
    pub interaction_range: f32,
    /// Safety margin for client-side movement reconciliation (in meters)
    pub reconciliation_safety_margin: f32,
}

pub fn player_create(ctx: &ReducerContext) -> Result<(), String> {
    let entity = entity_create(ctx)?;

    let spawn_position = entity.position;

    ctx.db.player().insert(Player {
        identity: ctx.sender,
        player_id: 0,
        entity_id: entity.entity_id,
        online: true,
        look_direction: DbVector2 { x: 0.0, y: 0.0 },
        animation_state: DbAnimationState {
            horizontal_movement: 0.0,
            vertical_movement: 0.0,
            combo_count: 0,
            is_moving: false,
            is_grounded: false,
            is_jumping: false,
            is_attacking: false,
        },
        last_valid_position: spawn_position,
        last_update_timestamp: ctx.timestamp.to_micros_since_unix_epoch(),
        movement_speed: 6.0,
        interaction_range: 3.0,
        reconciliation_safety_margin: 1.5,
    });

    log::debug!("Player {} created", ctx.sender);

    Ok(())
}

pub fn player_set_online_status(ctx: &ReducerContext, online: bool) -> Result<(), String> {
    if let Some(mut player) = ctx.db.player().identity().find(ctx.sender) {
        if online {
            log::debug!("Player {} is online", ctx.sender);
        } else {
            log::debug!("Player {} is offline", ctx.sender);
        }

        player.online = online;
        ctx.db.player().identity().update(player);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn player_connected(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.player().identity().find(ctx.sender).is_some() {
        player_set_online_status(ctx, true)?;
        creative_camera_set_enabled(ctx, false)?;
    } else {
        player_create(ctx)?;
        creative_camera_create(ctx)?;
        inventory_create(ctx)?;
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn player_update(
    ctx: &ReducerContext,
    position: DbVector3,
    rotation: DbVector3,
    animation_state: DbAnimationState,
) -> Result<(), String> {
    if ctx.db.player().identity().find(ctx.sender).is_some() {
        player_set_position(ctx, position)?;
        player_set_rotation(ctx, rotation)?;
        player_set_animation_state(ctx, animation_state)?;
        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}

#[spacetimedb::reducer]
pub fn player_set_position(ctx: &ReducerContext, position: DbVector3) -> Result<(), String> {
    if let Some(mut player) = ctx.db.player().identity().find(ctx.sender) {
        // Get the entity to access position
        let mut entity = match ctx.db.entity().entity_id().find(&player.entity_id) {
            Some(e) => e,
            None => return Err("Entity not found".to_string()),
        };

        // Validate position is on walkable surface
        if !is_position_valid(ctx, position.x, position.y, position.z) {
            log::warn!(
                "Player {} attempted to move to invalid position ({}, {}, {}). Resetting to last valid position.",
                ctx.sender,
                position.x,
                position.y,
                position.z
            );
            // Reset to last valid position
            entity.position = player.last_valid_position;
            ctx.db.entity().entity_id().update(entity);
            return Err("Invalid position - not on walkable surface".to_string());
        }

        // Speed validation - use player's configured movement speed with tolerance
        const MIN_TIME_DELTA_SECS: f32 = 0.05; // Ignore updates faster than 50ms to avoid false positives
        const SPEED_TOLERANCE: f32 = 1.25; // Allow 25% over max speed for network lag/variations

        let time_delta_micros =
            ctx.timestamp.to_micros_since_unix_epoch() - player.last_update_timestamp;
        let time_delta_secs = time_delta_micros as f32 / 1_000_000.0;

        if time_delta_secs > MIN_TIME_DELTA_SECS {
            let last_pos = &entity.position;
            // Only validate horizontal (XZ) movement - ignore Y for jumping
            let horizontal_distance =
                ((position.x - last_pos.x).powi(2) + (position.z - last_pos.z).powi(2)).sqrt();
            let speed = horizontal_distance / time_delta_secs;
            let max_allowed_speed = player.movement_speed * SPEED_TOLERANCE;

            if speed > max_allowed_speed {
                log::warn!(
                    "Player {} moving too fast: {:.2} units/sec (max: {:.2}, player speed: {:.2}). Horizontal distance: {:.2}, Time: {:.2}s",
                    ctx.sender,
                    speed,
                    max_allowed_speed,
                    player.movement_speed,
                    horizontal_distance,
                    time_delta_secs
                );
                // Reset to last valid position
                entity.position = player.last_valid_position;
                ctx.db.entity().entity_id().update(entity);
                return Err(format!(
                    "Invalid speed - moving too fast ({:.2} > {:.2} units/sec)",
                    speed, max_allowed_speed
                ));
            }
        }

        // Position is valid - update entity and player
        entity.position = position.clone();
        player.last_valid_position = position;
        player.last_update_timestamp = ctx.timestamp.to_micros_since_unix_epoch();

        ctx.db.entity().entity_id().update(entity);
        ctx.db.player().identity().update(player);

        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}

#[spacetimedb::reducer]
pub fn player_set_rotation(ctx: &ReducerContext, rotation: DbVector3) -> Result<(), String> {
    if let Some(player) = ctx.db.player().identity().find(ctx.sender) {
        // Get the entity to access rotation
        let mut entity = match ctx.db.entity().entity_id().find(&player.entity_id) {
            Some(e) => e,
            None => return Err("Entity not found".to_string()),
        };

        entity.rotation = rotation;
        ctx.db.entity().entity_id().update(entity);
        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}

#[spacetimedb::reducer]
pub fn player_set_animation_state(
    ctx: &ReducerContext,
    animation_state: DbAnimationState,
) -> Result<(), String> {
    if let Some(mut player) = ctx.db.player().identity().find(ctx.sender) {
        player.animation_state = animation_state;
        ctx.db.player().identity().update(player);
        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}
