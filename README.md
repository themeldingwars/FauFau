# FauFau

*FauFau is a library for manipulating FireFall's various file formats and protocols*

This library was created for educational purposes only and should not be used by anyone.

Library is automatically published to NuGet: https://www.nuget.org/packages/FauFau

## Contents

Most formats derive from Bitter's `BinaryWrapper`, so they read from a file path, a byte array or a stream with `Read(...)`. The game file paths are relative to the client's `system` folder.

### Static Database

| Class            | Files                                | Notes                                                                                           |
|------------------|--------------------------------------|-------------------------------------------------------------------------------------------------|
| `StaticDB`       | `db/clientdb.sd2`                    | Reads and writes the static database of 1297 and later, every cell as an object                 |
| `StaticDBView`   | `db/clientdb.sd2`                    | Read-only, keeps the file in one pooled buffer and reads cells on access                        |
| `StaticDBNames`  |                                      | Maps the FNV hashes of table and column names back to names, e.g. from SDBrowser's `fields.txt` |
| `LocalizedText`  |                                      | Turns the parameter header of `dblocalization::LocalizedText` strings into `{0}` placeholders   |
| `LegacyStaticDB` | `db/clientdb.sdb`, `db/commondb.sdb` | Read-only, the database of the builds before 1297, including the scrambled one of 1265          |

### Replays

| Class     | Files   | Notes                                                                                   |
|-----------|---------|-----------------------------------------------------------------------------------------|
| `Nsr`     | `*.nsr` | Reads and writes replays, also older, nested gzip and cut off ones                      |
| `NsrView` | `*.nsr` | Read-only, keeps the unpacked replay in one pooled buffer                               |
| `NsrInfo` | `*.nsr` | Reads the sections and counts the packets from a stream, without keeping them in memory |

### World

| Class                             | Files                            | Notes                                                                                                 |
|-----------------------------------|----------------------------------|-------------------------------------------------------------------------------------------------------|
| `Zone`                            | `maps/*.zone`                    | Reads and writes zones, the layers and the terrain chunks they reference                              |
| `GtChunkV8`                       | `maps/chunks/*.gtchunk`          | Terrain chunks, decompresses the layers of each LOD and sub chunk                                     |
| `GtLayer`                         |                                  | The layer tree that zones, chunks and environments share, typed by the id of the layer and its parent |
| `GtContainerLayer`, `GtDataLayer` |                                  | Layers that hold child layers, and layers FauFau has no type for                                      |
| `WorldLayerIds`                   |                                  | The ids of the zone and chunk layers                                                                  |
| `EnwfLayer`                       |                                  | The collision layers of chunks and the world chunk import of zones, mesh and Havok data               |
| `ZoneBoundsLayer`                 |                                  | The bounds of a zone                                                                                  |
| `ZoneSkyboxLayer`                 |                                  | The skybox record of a zone                                                                           |
| `ZoneChunkRangeLayer`             |                                  | The chunk coordinates a zone covers                                                                   |
| `ZoneChunkRefLayer`               |                                  | A terrain chunk of a zone, from the ChunkRef and ChunkRef2 layers                                     |
| `ZonePathLayer`                   |                                  | The steps of a path                                                                                   |
| `MeldingPerimeterLayer`           |                                  | A melding perimeter and its control points                                                            |
| `SubZoneRegionLayer`              |                                  | The area of a sub zone as a bitmap                                                                    |
| `SubZoneGridLayer`                |                                  | The sub zone of each cell of a chunk                                                                  |
| `EncounterNameRegistryLayer`      |                                  | The encounter names of the props of a zone or chunk                                                   |
| `Environment10000Layer`           |                                  | Two vectors of the default environment of a zone                                                      |
| `WorldDir`                        | `maps/*.worldDir`                | The index of a zone's world map tiles                                                                 |
| `WorldMap`                        | `maps/worldmapchunks/*.worldMap` | A world map tile with its textures and geometry                                                       |
| `Bnv`                             | `assetdb/*.bnv`                  | Environment assets, the environment layer on its own                                                  |
| `ScZone`                          | `assetdb/*.scZone`               | The props and environment of the 3D scenes in the UI                                                  |

### Assets

| Class                   | Files                                          | Notes                                                                  |
|-------------------------|------------------------------------------------|------------------------------------------------------------------------|
| `AssetDb`               | `assetdb/`                                     | Folder and file names of an asset id                                   |
| `BMesh`, `BMesh32`      | `assetdb/*.bMesh`                              | Meshes, `BMesh32` also writes version 32                               |
| `Czi`                   | `assetdb/*.czi`, `assetdb/*.czip`              | Color zone index maps and patterns, the recolorable areas of a texture |
| `VTexIndex`, `VTexTile` | `vt/static.vtex_idx`, `vt/static.vtex0` to `6` | The virtual texture and its tiles                                      |
| `VGeoIndex`, `VGeoPage` | `vg/static.vgeo_idx`, `vg/static.vgeo`         | The virtual geometry and its pages                                     |
| `LowMipTexturePak`      | `vt/lowmiptextures.pak`                        | The smallest mips of the textures                                      |

### Client Patches

| Class                    | Notes                                                                         |
|--------------------------|-------------------------------------------------------------------------------|
| `Patcher`                | Loads `FirefallClient.exe`, applies patches in memory and saves the result    |
| `RedHandedBypass`        | Skips the RedHanded anti-cheat, so the client starts without it               |
| `UnlimitedFreeCamRadius` | Removes the radius limit of the replay free camera                            |
| `BytePattern`            | Byte patterns with wildcards like `8B ?? 4?`, for finding the patch locations |

### Web

| Class     | Notes                                                                                   |
|-----------|-----------------------------------------------------------------------------------------|
| `Auth`    | The user id, secret and token of the Red 5 login                                        |
| `Red5Sig` | Parses, creates and verifies the `X-Red5-Signature` header of the client's web requests |

### Utilities

| Class             | Notes                                                                                     |
|-------------------|-------------------------------------------------------------------------------------------|
| `Checksum`        | `FFnv32`, the hash of the static database names, and CRC-32                               |
| `MersenneTwister` | The generator the client scrambles data with, plus `Xor` and `Fill` for its first outputs |
| `Compression`     | Raw deflate and gzip between binary streams                                               |
| `FastLz`          | FastLZ decompression                                                                      |
| `Hex`             | Hex encoding into strings and spans                                                       |
| `EntityGuid`      | Packs and parses entity guids                                                             |
| `WarpaintColor`   | Packs the light and dark shade of a warpaint color                                        |
| `Time`            | Unix timestamps and the in-game time                                                      |
