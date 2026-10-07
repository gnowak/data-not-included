# Data Not Included

A comprehensive data extraction toolchain and REST API for **Oxygen Not Included** (ONI). 

**Data Not Included** uses a Harmony-based C# mod to dump runtime game definitions, element properties, building stats, recipes, critters, plants, and UI sprites directly into structured JSON files and PNG assets, then serves them via a lightweight Node.js Express API.

---

## Architecture Overview

```
data-not-included/
├── extractor/               # C# Harmony mod for Oxygen Not Included (.NET Framework 4.8)
│   ├── DataDumper.cs        # Primary extraction logic hooking MainMenu.OnSpawn
│   ├── ModInfo.cs           # KMod entry point (UserMod2)
│   ├── DataNotIncludedMod.csproj
│   ├── mod_info.yaml        # Mod metadata
│   └── sync.ps1             # Optional script to copy dumps into web assets
├── api/                     # Node.js + Express REST API server
│   ├── server.js            # Express API serving dumped data
│   ├── package.json
│   └── package-lock.json
├── scratch/                 # Reverse-engineering / reflection inspection utility (.NET 8)
│   ├── InspectAssembly.csproj
│   └── Program.cs
├── build_and_deploy.bat     # Automated build & local mod installation script
├── .gitignore
└── README.md
```

---

## Components

### 1. Extractor Mod (`extractor/`)
The mod hooks into `MainMenu.OnSpawn` using Harmony. When Oxygen Not Included launches and reaches the main menu, the mod iterates through internal game databases and serializes game data to:
```
%USERPROFILE%\Documents\Klei\OxygenNotIncluded\DataDump\
```
*(Also automatically detects OneDrive-backed documents folders).*

#### Extracted Data Sets:
- **`elements.json`**: Physical states, transition temperatures, specific heat capacity, thermal conductivity, radiation absorption, light absorption.
- **`buildings.json`**: Construction costs, material categories, dimensions, power/heat generation, gas/liquid intake & exhaust, descriptions.
- **`recipes.json`**: Crafting tables, fabricators, refineries, input ingredients, outputs, and energy requirements.
- **`personalities.json`**: Duplicants, starting traits, base attributes, descriptions, and body models.
- **`critters.json`**: Critter breeds, lifecycle phases, fertility, egg requirements, diet/poop conversions, light emission.
- **`plants.json`**: Domesticated and wild growth rates, irrigation/fertilizer inputs, harvest yields, temperature/pressure ranges.
- **`geysers.json`**: Geyser, vent, and volcano definitions, output rates, temperatures.
- **`foods.json`**: Food items, calories, quality tiers, preservation thresholds, spoil times.
- **`equipment.json`**: Suits, clothing, wear bonuses, thermal resistance, and durability.
- **`space_pois.json`**: Space points of interest, artifact chances, harvestable orbital materials.
- **`images/`**: Exported PNG icons and UI textures for buildings, elements, critters, and items.
- **`dump_log.txt`**: Execution log capturing extraction status and any reflection errors.

### 2. REST API (`api/`)
A Node.js / Express service that loads the generated JSON data files from the `DataDump` directory and exposes them as REST endpoints with CORS enabled.

### 3. Assembly Inspector (`scratch/`)
A lightweight .NET 8 console tool used during development to inspect private and public fields and properties of ONI game assemblies via reflection.

---

## Prerequisites

- **Windows OS**
- **Oxygen Not Included** installed via Steam (default path: `C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded`)
- **.NET SDK** (with .NET Framework 4.8 targeting pack for building the mod)
- **Node.js** (v16+) for the API server

---

## Quick Start

### 1. Build & Deploy the Extractor Mod

Run the automated build script from the repository root:

```cmd
build_and_deploy.bat
```

This script will:
1. Compile `extractor/DataNotIncludedMod.csproj` via `dotnet build`.
2. Locate your Klei mods folder (checks standard Documents and OneDrive paths).
3. Copy `DataNotIncluded.dll` and `mod_info.yaml` to:
   `%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\Local\DataNotIncluded\`

### 2. Generate Data Dump

1. Launch **Oxygen Not Included**.
2. Ensure the **DataNotIncluded** mod is enabled in the in-game **Mods** menu.
3. Restart or return to the **Main Menu**.
4. The mod will execute the dump in the background. Check `%USERPROFILE%\Documents\Klei\OxygenNotIncluded\DataDump\dump_log.txt` to verify completion.

### 3. Start the API Server

```bash
cd api
npm install
npm start
```

By default, the server will run on `http://localhost:3000`. You can configure a custom port using the `PORT` environment variable:

```bash
PORT=8080 npm start
```

---

## API Endpoints

| Endpoint | Method | Description |
| :--- | :--- | :--- |
| `/api/health` | GET | Healthcheck and current `dataDumpPath` |
| `/api/buildings` | GET | List all building definitions |
| `/api/buildings/:id` | GET | Retrieve a specific building by ID (e.g. `GasPump`) |
| `/api/elements` | GET | List all elements and thermal/physical properties |
| `/api/elements/:id` | GET | Retrieve a specific element by ID (case-insensitive) |
| `/api/recipes` | GET | List crafting recipes |
| `/api/personalities` | GET | List Duplicant personalities and default traits |
| `/api/critters` | GET | List critter types, diets, drops, and parameters |
| `/api/plants` | GET | List plant requirements, growth times, and yields |
| `/api/geysers` | GET | List geyser and volcano types |
| `/api/foods` | GET | List food recipes, calories, and quality levels |
| `/api/equipment` | GET | List suits, clothing, and wearable items |
| `/api/space_pois` | GET | List space points of interest and asteroid resources |

---

## Configuration & Paths

If your Steam library or game install is located at a custom path, update the `GameFolder` property in `extractor/DataNotIncludedMod.csproj`:

```xml
<PropertyGroup>
  <GameFolder>C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded</GameFolder>
  <ManagedFolder>$(GameFolder)\OxygenNotIncluded_Data\Managed</ManagedFolder>
</PropertyGroup>
```

---

## License

MIT License. See individual source files for details. Oxygen Not Included is a trademark of Klei Entertainment.
