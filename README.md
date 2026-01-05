# CityDemo

Procedural city demo built in Unity. The project focuses on grid-based city generation, tensor-field streamlines for roads, and map-driven building placement.

## Unity Version
- 6000.3.2f1 (see `ProjectSettings/ProjectVersion.txt`)

## Highlights
- Grid-based building generation with optional height, density, color, and exclusion maps.
- Road generation via simple grid lines or tensor-field streamlines.
- Road mask baking to drive building avoidance and alignment.
- Instanced rendering option for large building counts.

## Main Scene
- `Assets/OutdoorsScene.unity`

## Key Scripts
- `Assets/Scripts/CityGenerator.cs`  
  Spawns buildings on a grid with map-driven height, density, color, and road avoidance.
- `Assets/Scripts/RoadGridGenerator.cs`  
  Builds an orthogonal road grid from a prefab.
- `Assets/Scripts/TensorFieldSimple.cs`  
  Visualizes tensor fields, traces streamlines, and builds roads + road masks.

## Getting Started
1. Open the project with Unity 6000.3.2f1.
2. Load `Assets/OutdoorsScene.unity`.
3. Select the generator objects in the Hierarchy and configure their Inspector fields.

## Usage (Editor Context Menus)
### CityGenerator
- `Generate City` / `Generate City (Instanced)`  
  Requires `buildingPrefab` (see `Assets/Models/Building.prefab` or `Assets/Models/Cube.prefab`).
- Optional maps: height, density, color, road mask, and exclusion mask.

### RoadGridGenerator
- `Generate Roads`  
  Requires `roadPrefab` and grid spacing parameters.

### TensorFieldSimple
- `Rebuild Streamlines`  
  Generates cached tensor-field streamlines for preview.
- `Build Roads`  
  Instantiates road prefabs along cached streamlines.
- `Bake Road Mask`  
  Writes a mask texture (default `Assets/Generated/RoadMask.png`).

## Generated Assets
Textures like `Assets/Generated/RoadMask.png`, `Assets/Generated/BuildingHeight.png`, and `Assets/Generated/BuildingColor.png` can be used as input maps for the generators.
