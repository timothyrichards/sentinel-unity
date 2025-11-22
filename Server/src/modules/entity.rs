use crate::modules::player::player;
use crate::modules::world_spawn::world_spawn;
use crate::types::DbVector3;
use spacetimedb::{ReducerContext, Table};

#[spacetimedb::table(name = entity, public)]
pub struct Entity {
    #[primary_key]
    #[unique]
    #[auto_inc]
    pub entity_id: u32,
    pub position: DbVector3,
    pub rotation: DbVector3,
    pub health: f32,
    pub max_health: f32,
    pub attack_range: f32,
}

pub fn entity_create(ctx: &ReducerContext) -> Result<Entity, String> {
    let (position, rotation) = if let Some(spawn) = ctx.db.world_spawn().id().find(&0) {
        (spawn.position, spawn.rotation)
    } else {
        (
            DbVector3 {
                x: 0.0,
                y: 0.0,
                z: 0.0,
            },
            DbVector3 {
                x: 0.0,
                y: 0.0,
                z: 0.0,
            },
        )
    };

    let entity = ctx.db.entity().insert(Entity {
        entity_id: 0,
        position,
        rotation,
        health: 100.0,
        max_health: 100.0,
        attack_range: 3.0,
    });

    log::debug!("Entity {} created", ctx.sender);

    Ok(entity)
}

/// Apply damage from one entity to another
/// Validates that the attacker is online and in range
#[spacetimedb::reducer]
pub fn entity_apply_damage(
    ctx: &ReducerContext,
    target_entity_id: u32,
    damage: f32,
) -> Result<(), String> {
    // Get attacker's player to verify they're online
    let attacker = ctx.db.player().identity().find(&ctx.sender)
        .ok_or("Attacker not found")?;

    if !attacker.online {
        return Err("Attacker is not online".to_string());
    }

    // Get attacker's entity for position and attack range
    let attacker_entity = ctx.db.entity().entity_id().find(&attacker.entity_id)
        .ok_or("Attacker entity not found")?;

    // Get target entity
    let mut target_entity = ctx.db.entity().entity_id().find(&target_entity_id)
        .ok_or("Target entity not found")?;

    // Check if attacker is in range of target
    let distance = attacker_entity.position.distance(&target_entity.position);

    if distance > attacker_entity.attack_range {
        return Err(format!(
            "Target out of range. Distance: {:.1}, Range: {:.1}",
            distance, attacker_entity.attack_range
        ));
    }

    // Apply damage
    target_entity.health -= damage;
    if target_entity.health < 0.0 {
        target_entity.health = 0.0;
    }
    ctx.db.entity().entity_id().update(target_entity);

    log::info!(
        "Entity {} dealt {:.1} damage to entity {} (distance: {:.1})",
        attacker.entity_id, damage, target_entity_id, distance
    );

    Ok(())
}

/// Reset an entity's health to max (admin functionality)
#[spacetimedb::reducer]
pub fn entity_reset_health(ctx: &ReducerContext, entity_id: u32) -> Result<(), String> {
    let mut entity = ctx.db.entity().entity_id().find(&entity_id)
        .ok_or("Entity not found")?;

    let max_health = entity.max_health;
    entity.health = max_health;
    ctx.db.entity().entity_id().update(entity);

    log::info!("Entity {} health reset to {}", entity_id, max_health);
    Ok(())
}
