use crate::types::DbVector3;
use spacetimedb::{ReducerContext, Table};

#[spacetimedb::table(name = world_spawn, public)]
pub struct WorldSpawn {
    #[primary_key]
    pub id: u32,
    pub position: DbVector3,
    pub rotation: DbVector3,
}

pub fn world_spawn_init(ctx: &ReducerContext) -> Result<(), String> {
    world_spawn_set(
        ctx,
        0,
        DbVector3 {
            x: 0.0,
            y: 2.0,
            z: 0.0,
        },
        DbVector3::default(),
    )?;
    Ok(())
}

pub fn world_spawn_set(
    ctx: &ReducerContext,
    id: u32,
    position: DbVector3,
    rotation: DbVector3,
) -> Result<(), String> {
    if let Some(mut spawn) = ctx.db.world_spawn().id().find(&id) {
        spawn.position = position;
        spawn.rotation = rotation;
        ctx.db.world_spawn().id().update(spawn);
    } else {
        ctx.db.world_spawn().insert(WorldSpawn {
            id: 0,
            position: DbVector3 {
                x: 0.0,
                y: 52.0,
                z: -234.0,
            },
            rotation: DbVector3::default(),
        });
    }
    Ok(())
}
