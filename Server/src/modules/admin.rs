use spacetimedb::{Identity, ReducerContext, Table};

/// Table storing admin identities
#[spacetimedb::table(name = admin, public)]
pub struct Admin {
    #[primary_key]
    pub identity: Identity,
}

/// Check if the given identity is an admin
pub fn is_admin(ctx: &ReducerContext, identity: Identity) -> bool {
    ctx.db.admin().identity().find(identity).is_some()
}

/// Check if the caller is an admin, returning an error if not
pub fn require_admin(ctx: &ReducerContext) -> Result<(), String> {
    if is_admin(ctx, ctx.sender) {
        Ok(())
    } else {
        Err("Unauthorized: admin access required".to_string())
    }
}

/// Add an admin
#[spacetimedb::reducer]
pub fn admin_add(ctx: &ReducerContext, identity: Identity) -> Result<(), String> {
    require_admin(ctx)?;

    if ctx.db.admin().identity().find(identity).is_some() {
        return Err("Identity is already an admin".to_string());
    }

    ctx.db.admin().insert(Admin { identity });
    log::info!("Added admin: {:?}", identity);
    Ok(())
}

/// Remove an admin
#[spacetimedb::reducer]
pub fn admin_remove(ctx: &ReducerContext, identity: Identity) -> Result<(), String> {
    require_admin(ctx)?;

    if ctx.db.admin().identity().find(identity).is_none() {
        return Err("Identity is not an admin".to_string());
    }

    ctx.db.admin().identity().delete(identity);
    log::info!("Removed admin: {:?}", identity);
    Ok(())
}
