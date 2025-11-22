use spacetimedb::{Identity, ReducerContext, SpacetimeType, Table};
use crate::modules::admin::require_admin;

#[derive(SpacetimeType, Clone, Debug)]
pub struct ItemRef {
    pub id: u32,
    pub quantity: u32,
}

#[spacetimedb::table(name = item, public)]
pub struct Item {
    #[primary_key]
    pub id: u32,
    pub name: String,
    pub description: String,
    pub weight: f32,
}

#[spacetimedb::table(name = inventory, public)]
pub struct Inventory {
    #[primary_key]
    pub identity: Identity,
    pub size: u32,
    pub items: Vec<ItemRef>,
}

/// Initialize default items
pub fn item_init(ctx: &ReducerContext) -> Result<(), String> {
    // Branch - item_id 0
    ctx.db.item().insert(Item {
        id: 0,
        name: "Branch".to_string(),
        description: "A sturdy wooden branch.".to_string(),
        weight: 0.5,
    });

    // Rock - item_id 1
    ctx.db.item().insert(Item {
        id: 1,
        name: "Rock".to_string(),
        description: "A solid rock.".to_string(),
        weight: 1.0,
    });

    log::info!("Initialized default items");
    Ok(())
}

#[spacetimedb::reducer]
pub fn inventory_create(ctx: &ReducerContext) -> Result<(), String> {
    let inventory = Inventory {
        identity: ctx.sender,
        size: 32,
        items: vec![],
    };
    ctx.db.inventory().insert(inventory);
    Ok(())
}

/// Internal function for adding items (used by server-side logic like looting)
pub fn inventory_add_item_internal(
    ctx: &ReducerContext,
    identity: Identity,
    item_id: u32,
    quantity: u32,
) -> Result<(), String> {
    let inventory = ctx.db.inventory().identity().find(identity);
    if let Some(mut inventory) = inventory {
        if let Some(existing_item) = inventory.items.iter_mut().find(|item| item.id == item_id) {
            existing_item.quantity += quantity;
        } else {
            let item_ref = ItemRef {
                id: item_id,
                quantity,
            };
            inventory.items.push(item_ref);
        }
        ctx.db.inventory().identity().update(inventory);
    }
    Ok(())
}

/// Add items to a player's inventory (admin-only reducer)
#[spacetimedb::reducer]
pub fn inventory_add_item(
    ctx: &ReducerContext,
    identity: Identity,
    item_id: u32,
    quantity: u32,
) -> Result<(), String> {
    require_admin(ctx)?;
    inventory_add_item_internal(ctx, identity, item_id, quantity)
}

pub fn inventory_get_item(ctx: &ReducerContext, item_id: u32) -> Result<ItemRef, String> {
    let inventory = ctx.db.inventory().identity().find(ctx.sender);
    if let Some(inventory) = inventory {
        if let Some(item) = inventory.items.iter().find(|item| item.id == item_id) {
            Ok(item.clone())
        } else {
            Err("Item not found in inventory".to_string())
        }
    } else {
        Err("Inventory not found".to_string())
    }
}

/// Internal function for removing items (used by server-side logic like building)
pub fn inventory_remove_item_internal(
    ctx: &ReducerContext,
    identity: Identity,
    item_id: u32,
    quantity: u32,
) -> Result<(), String> {
    let inventory = ctx.db.inventory().identity().find(identity);
    if let Some(mut inventory) = inventory {
        if let Some(position) = inventory.items.iter().position(|item| item.id == item_id) {
            let existing_item = &mut inventory.items[position];
            existing_item.quantity -= quantity;
            if existing_item.quantity == 0 {
                inventory.items.remove(position);
            }
        }
        ctx.db.inventory().identity().update(inventory);
    }
    Ok(())
}

/// Remove items from a player's inventory (admin-only reducer)
#[spacetimedb::reducer]
pub fn inventory_remove_item(
    ctx: &ReducerContext,
    identity: Identity,
    item_id: u32,
    quantity: u32,
) -> Result<(), String> {
    require_admin(ctx)?;
    inventory_remove_item_internal(ctx, identity, item_id, quantity)
}
