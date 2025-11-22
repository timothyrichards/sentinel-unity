// External crate imports
use spacetimedb::SpacetimeType;

#[derive(SpacetimeType, Clone, Debug)]
pub struct DbVector3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

impl Default for DbVector3 {
    fn default() -> Self {
        Self {
            x: 0.0,
            y: 0.0,
            z: 0.0,
        }
    }
}

impl DbVector3 {
    /// Calculate the distance between two points
    pub fn distance(&self, other: &DbVector3) -> f32 {
        ((self.x - other.x).powi(2) + (self.y - other.y).powi(2) + (self.z - other.z).powi(2)).sqrt()
    }
}

#[derive(SpacetimeType, Clone, Debug)]
pub struct DbVector2 {
    pub x: f32,
    pub y: f32,
}

impl Default for DbVector2 {
    fn default() -> Self {
        Self { x: 0.0, y: 0.0 }
    }
}
