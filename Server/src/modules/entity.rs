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
    });

    log::debug!("Entity {} created", ctx.sender);

    Ok(entity)
}
