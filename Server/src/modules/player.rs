use crate::modules::creative_camera::{creative_camera_create, creative_camera_set_enabled};
use crate::modules::entity::entity_create;
use crate::modules::inventory::inventory_create;
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
}

pub fn player_create(ctx: &ReducerContext) -> Result<(), String> {
    let entity = entity_create(ctx)?;

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
        player.position = position;
        ctx.db.player().identity().update(player);
        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}

#[spacetimedb::reducer]
pub fn player_set_rotation(ctx: &ReducerContext, rotation: DbVector3) -> Result<(), String> {
    if let Some(mut player) = ctx.db.player().identity().find(ctx.sender) {
        player.rotation = rotation;
        ctx.db.player().identity().update(player);
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

#[spacetimedb::reducer]
pub fn player_apply_damage(
    ctx: &ReducerContext,
    target_identity: Identity,
    damage: f32,
) -> Result<(), String> {
    // Validate that the attacker exists and is online
    if let Some(attacker) = ctx.db.player().identity().find(&ctx.sender) {
        if !attacker.online {
            return Err("Attacker is not online".to_string());
        }

        // Apply damage to target
        if let Some(mut target) = ctx.db.player().identity().find(&target_identity) {
            target.health -= damage;
            if target.health < 0.0 {
                target.health = 0.0;
            }
            ctx.db.player().identity().update(target);
            Ok(())
        } else {
            Err("Target player not found".to_string())
        }
    } else {
        Err("Attacker not found".to_string())
    }
}

#[spacetimedb::reducer]
pub fn player_reset_health(ctx: &ReducerContext, target_identity: Identity) -> Result<(), String> {
    if let Some(mut player) = ctx.db.player().identity().find(&target_identity) {
        player.health = player.max_health;
        ctx.db.player().identity().update(player);
        Ok(())
    } else {
        Err("Player not found".to_string())
    }
}
