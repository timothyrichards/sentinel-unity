// External crate imports
use spacetimedb::ReducerContext;

// Local module declarations
mod modules;
mod types;

// Local module imports
use modules::building_piece_variant::building_piece_variant_init;
use modules::inventory::item_init;
use modules::lootable::lootable_item_type_init;
use modules::player::{player, player_set_online_status};
use modules::world_spawn::world_spawn_init;

#[spacetimedb::reducer(init)]
pub fn init(ctx: &ReducerContext) -> Result<(), String> {
    world_spawn_init(ctx)?;
    building_piece_variant_init(ctx)?;
    item_init(ctx)?;
    lootable_item_type_init(ctx)?;
    Ok(())
}

#[spacetimedb::reducer(client_connected)]
pub fn connect(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.player().identity().find(ctx.sender).is_none() {
        log::debug!("Unknown client {} just connected.", ctx.sender);
    }
    Ok(())
}

#[spacetimedb::reducer(client_disconnected)]
pub fn disconnect(ctx: &ReducerContext) -> Result<(), String> {
    player_set_online_status(ctx, false)?;
    Ok(())
}
