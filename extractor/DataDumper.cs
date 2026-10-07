using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DataNotIncluded
{
    [HarmonyPatch(typeof(MainMenu), "OnSpawn")]
    public static class DataDumper_MainMenu_OnSpawn_Patch
    {
        public static void Postfix()
        {
            Debug.Log("DataNotIncluded: Starting data dump...");

            string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string outDir = Path.Combine(docsPath, "Klei", "OxygenNotIncluded", "DataDump");
            
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            string logFile = Path.Combine(outDir, "dump_log.txt");
            File.WriteAllText(logFile, "Starting dump...\n");
            
            string imgDir = Path.Combine(outDir, "images");
            if (!Directory.Exists(imgDir))
            {
                Directory.CreateDirectory(imgDir);
            }

            void SafeDump(Action<string> dumpAction, string name)
            {
                try
                {
                    dumpAction(outDir);
                    File.AppendAllText(logFile, $"[OK] {name}\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logFile, $"[ERROR] {name}: {ex.Message}\n{ex.StackTrace}\n");
                    Debug.LogWarning($"DataNotIncluded: Failed to dump {name}. {ex.Message}");
                }
            }

            SafeDump(DumpElements, "Elements");
            SafeDump(DumpBuildings, "Buildings");
            SafeDump(DumpRecipes, "Recipes");
            SafeDump(DumpPersonalities, "Personalities");
            SafeDump(DumpCritters, "Critters");
            SafeDump(DumpPlants, "Plants");
            SafeDump(DumpGeysers, "Geysers");
            SafeDump(DumpFoods, "Foods");
            SafeDump(DumpEquipment, "Equipment");
            SafeDump(DumpSpacePOIs, "SpacePOIs");

            File.AppendAllText(logFile, "Dump process finished.\n");
            Debug.Log($"DataNotIncluded: Dump completed at {outDir}");
        }

        // Helper to safely get private fields via reflection, traversing base classes if needed
        private static T GetPrivateField<T>(object obj, string name)
        {
            if (obj == null) return default(T);
            var type = obj.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return (T)field.GetValue(obj);
                }
                type = type.BaseType;
            }
            return default(T);
        }

        // Helper to strip TMPro and HTML rich-text formatting tags (e.g. <link="...">...</link> or <b>...</b>)
        // and fall back to ID if translation string is missing
        private static string CleanName(string name, string fallbackId = null)
        {
            if (string.IsNullOrEmpty(name)) return fallbackId ?? "";
            
            // Strip TMPro/HTML tags
            string cleaned = System.Text.RegularExpressions.Regex.Replace(name, @"<[^>]+>", "").Trim();
            
            // If it's a missing string key, return the fallback ID
            if (cleaned.StartsWith("MISSING.STRINGS.") && !string.IsNullOrEmpty(fallbackId))
            {
                return fallbackId;
            }
            return cleaned;
        }

        // --- Official-content filtering ---
        // All Klei base-game and DLC content is compiled into Assembly-CSharp.dll.
        // Mods add types in their own DLLs. By checking component assemblies we can
        // reliably exclude any prefab that has a component from a non-Klei assembly.
        private static readonly HashSet<string> OfficialAssemblyNames = new HashSet<string>
        {
            "Assembly-CSharp",
            "Assembly-CSharp-firstpass",
        };

        private static bool IsOfficialAssembly(System.Reflection.Assembly asm)
        {
            var name = asm.GetName().Name;
            if (OfficialAssemblyNames.Contains(name)) return true;
            // Unity runtime and BCL assemblies are always fine
            if (name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)) return true;
            if (name == "mscorlib" || name == "netstandard" || name == "Newtonsoft.Json") return true;
            return false;
        }

        /// <summary>
        /// Returns true if this prefab is an official Klei game entity.
        /// To be immune to dynamic components injected by other mods at runtime, we first search for
        /// the entity's config class (e.g. HatchConfig) and check its assembly. As a fallback, we check
        /// the assembly of its core identifying component (e.g. CreatureBrain or Geyser).
        /// </summary>
        private static bool IsOfficialPrefab(UnityEngine.GameObject go)
        {
            if (go == null) return false;

            var kpid = go.GetComponent<KPrefabID>();
            var cleanId = kpid != null ? kpid.PrefabTag.ToString() : go.name;

            // 1. If it is a building, check if the building is official
            var buildingDef = Assets.GetBuildingDef(cleanId);
            if (buildingDef != null)
            {
                return IsOfficialBuilding(buildingDef);
            }

            // 2. Try to find a config class for this specific prefab name (e.g. HatchConfig)
            var configNames = new List<string>
            {
                cleanId + "Config",
                go.name + "Config",
                "Baby" + cleanId.Replace("Baby", "") + "Config",
                "Baby" + go.name.Replace("Baby", "") + "Config"
            };

            foreach (var configName in configNames)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = asm.GetType(configName) ?? asm.GetType("DataNotIncluded." + configName);
                        if (type == null)
                        {
                            type = asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, configName, StringComparison.OrdinalIgnoreCase));
                        }
                        if (type != null)
                        {
                            return IsOfficialAssembly(type.Assembly);
                        }
                    }
                    catch { /* safe to skip reflection errors on some assemblies */ }
                }
            }

            // 3. Fallback: Check if the primary identity-defining component is from an official assembly.
            UnityEngine.Component coreComp = null;
            
            // Check in order of importance
            coreComp = (UnityEngine.Component)go.GetComponent<CreatureBrain>() ??
                       (UnityEngine.Component)go.GetComponent<Geyser>() ??
                       (UnityEngine.Component)go.GetComponent<Edible>() ??
                       (UnityEngine.Component)go.GetComponent<Equippable>() ??
                       (UnityEngine.Component)go.GetComponent<HarvestablePOIConfigurator>() ??
                       (UnityEngine.Component)go.GetComponent<Uprootable>() ??
                       (UnityEngine.Component)go.GetComponent<SeedProducer>() ??
                       (UnityEngine.Component)go.GetComponent<Crop>();

            if (coreComp != null)
            {
                return IsOfficialAssembly(coreComp.GetType().Assembly);
            }

            // 4. Ultimate fallback: check all components
            foreach (var comp in go.GetComponents<UnityEngine.Component>())
            {
                if (comp == null) continue;
                if (!IsOfficialAssembly(comp.GetType().Assembly))
                    return false;
            }

            return true;
        }

        private static bool IsOfficialBuilding(BuildingDef b)
        {
            if (b == null) return false;
            
            var configManager = BuildingConfigManager.Instance;
            if (configManager != null)
            {
                var configTableField = typeof(BuildingConfigManager).GetField("configTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var configTable = configTableField?.GetValue(configManager) as IDictionary;
                if (configTable != null)
                {
                    foreach (DictionaryEntry entry in configTable)
                    {
                        if (object.ReferenceEquals(entry.Value, b))
                        {
                            var asm = entry.Key.GetType().Assembly;
                            return IsOfficialAssembly(asm);
                        }
                    }
                }
            }
            
            // Fallback to name-based reflection search
            return IsOfficialBuilding(b.PrefabID);
        }

        /// <summary>
        /// Returns true if the building's IBuildingConfig class is defined in the official game assembly.
        /// Mod buildings always register a config type in their own DLL.
        /// </summary>
        private static bool IsOfficialBuilding(string prefabId)
        {
            var configName = prefabId + "Config";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsOfficialAssembly(asm)) continue;
                try
                {
                    // If ANY non-official assembly defines this config, it's a mod building
                    if (asm.GetTypes().Any(t => t.Name == configName))
                        return false;
                }
                catch { /* reflection may throw on some assemblies – safe to skip */ }
            }
            return true;
        }

        private static bool IsOfficialRecipe(ComplexRecipe r)
        {
            if (r == null) return false;
            
            // Check fabricators. If fabricators are specified and none of them are official, it's modded.
            if (r.fabricators != null && r.fabricators.Count > 0)
            {
                bool hasOfficialFabricator = false;
                foreach (var fabTag in r.fabricators)
                {
                    var prefab = Assets.GetPrefab(fabTag);
                    if (prefab == null) continue;
                    if (IsOfficialPrefab(prefab.gameObject))
                    {
                        hasOfficialFabricator = true;
                        break;
                    }
                }
                if (!hasOfficialFabricator) return false;
            }

            // Check ingredients
            if (r.ingredients != null)
            {
                foreach (var ing in r.ingredients)
                {
                    var prefab = Assets.GetPrefab(ing.material);
                    if (prefab != null && !IsOfficialPrefab(prefab.gameObject))
                        return false;
                }
            }

            // Check results
            if (r.results != null)
            {
                foreach (var res in r.results)
                {
                    var prefab = Assets.GetPrefab(res.material);
                    if (prefab != null && !IsOfficialPrefab(prefab.gameObject))
                        return false;
                }
            }

            return true;
        }


        private static void ExportImage(object prefab, string name, string imgDir)
        {
            try
            {
                UnityEngine.Sprite sprite = null;
                if (prefab is BuildingDef bdef)
                {
                    sprite = bdef.GetUISprite("ui", false);
                }
                else
                {
                    var spriteTuple = Def.GetUISprite(prefab, "ui", false);
                    if (spriteTuple != null && spriteTuple.first != null)
                    {
                        sprite = spriteTuple.first;
                    }
                }

                if (sprite != null)
                {
                    if (sprite.texture != null)
                    {
                        var texture = sprite.texture;
                        var rect = sprite.textureRect;

                        UnityEngine.RenderTexture tmp = UnityEngine.RenderTexture.GetTemporary(
                            texture.width, texture.height, 0, UnityEngine.RenderTextureFormat.Default, UnityEngine.RenderTextureReadWrite.Linear);
                        UnityEngine.Graphics.Blit(texture, tmp);
                        UnityEngine.RenderTexture previous = UnityEngine.RenderTexture.active;
                        UnityEngine.RenderTexture.active = tmp;
                        UnityEngine.Texture2D myTexture2D = new UnityEngine.Texture2D((int)rect.width, (int)rect.height);
                        myTexture2D.ReadPixels(new UnityEngine.Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                        myTexture2D.Apply();
                        UnityEngine.RenderTexture.active = previous;
                        UnityEngine.RenderTexture.ReleaseTemporary(tmp);

                        byte[] bytes = myTexture2D.EncodeToPNG();
                        File.WriteAllBytes(Path.Combine(imgDir, name + ".png"), bytes);
                        UnityEngine.Object.Destroy(myTexture2D);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"DataNotIncluded: Failed to export image for {name}. {e.Message}");
            }
        }

        private static void DumpRecipes(string outDir)
        {
            var recipes = ComplexRecipeManager.Get().recipes.Where(IsOfficialRecipe).Select(r => new
            {
                id = r.id,
                time = r.time,
                description = r.description,
                fabricators = r.fabricators.Select(f => f.ToString()).ToList(),
                inputs = r.ingredients.Select(i => new { material = i.material.ToString(), amount = i.amount }).ToList(),
                outputs = r.results.Select(o => new { material = o.material.ToString(), amount = o.amount }).ToList(),
                requiredDlcIds = GetPrivateField<string[]>(r, "requiredDlcIds"),
                forbiddenDlcIds = GetPrivateField<string[]>(r, "forbiddenDlcIds")
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "recipes.json"), JsonConvert.SerializeObject(recipes, Formatting.Indented));
        }

        private static void DumpPersonalities(string outDir)
        {
            var personalities = Db.Get().Personalities.resources.Select(p => new
            {
                id = p.Id,
                name = CleanName(p.Name, p.Id),
                description = p.description,
                gender = p.genderStringKey,
                joyTrait = p.joyTrait,
                stressTrait = p.stresstrait,
                congenitalTrait = p.congenitaltrait,
                requiredDlcId = p.requiredDlcId
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "personalities.json"), JsonConvert.SerializeObject(personalities, Formatting.Indented));
        }

        private static void DumpCritters(string outDir)
        {
            var critters = Assets.Prefabs.Where(p => p.GetComponent<CreatureBrain>() != null && IsOfficialPrefab(p.gameObject)).Select(c => {
                // KPrefabID for DLC info
                var kpid = c.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                // Temperature tolerances
                var tv = c.GetComponent<TemperatureVulnerable>();
                float? tempLethalLow = tv != null ? tv.TemperatureLethalLow : (float?)null;
                float? tempWarningLow = tv != null ? tv.TemperatureWarningLow : (float?)null;
                float? tempWarningHigh = tv != null ? tv.TemperatureWarningHigh : (float?)null;
                float? tempLethalHigh = tv != null ? tv.TemperatureLethalHigh : (float?)null;

                // Decor
                var dp = c.GetComponent<DecorProvider>();
                float? decor = dp != null ? dp.baseDecor : (float?)null;
                float? decorRadius = dp != null ? dp.baseRadius : (float?)null;

                // Lifespan (Standard is 100 cycles unless custom aging modifiers/attributes exist)
                float lifespanCycles = 100f; 

                // Crowding requirements
                int? spaceRequired = null;
                var overcrowdingDef = c.GetDef<OvercrowdingMonitor.Def>();
                if (overcrowdingDef != null)
                {
                    spaceRequired = overcrowdingDef.spaceRequiredPerCreature;
                }
                else if (c.GetDef<FishOvercrowdingMonitor.Def>() != null)
                {
                    spaceRequired = 8; // Pacu/fish hardcoded liquid space constant
                }

                // Diet & Daily Metabolism
                var calorieDef = c.GetDef<CreatureCalorieMonitor.Def>();
                List<object> dietList = null;
                if (calorieDef != null && calorieDef.diet != null && calorieDef.diet.infos != null)
                {
                    dietList = new List<object>();
                    foreach (var info in calorieDef.diet.infos)
                    {
                        var consumed = info.consumedTags.Select(t => t.ToString()).ToList();
                        var produced = info.producedElement.ToString();
                        var calPerKg = info.caloriesPerKg;
                        var convRate = info.producedConversionRate;
                        
                        // Daily consumption: standard metabolism burns 120,000 kcal per cycle
                        float dailyCons = calPerKg > 0 ? 120000f / calPerKg : 0f;
                        float dailyProd = dailyCons * convRate;

                        dietList.Add(new
                        {
                            consumedTags = consumed,
                            producedElement = produced,
                            caloriesPerKg = calPerKg,
                            producedConversionRate = convRate,
                            dailyConsumptionKg = dailyCons,
                            dailyExcrementKg = dailyProd
                        });
                    }
                }

                ExportImage(c.gameObject, c.gameObject.name, Path.Combine(outDir, "images"));

                return new
                {
                    id = c.gameObject.name,
                    name = CleanName(c.GetProperName(), c.gameObject.name),
                    prefabId = c.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    tags = kpid?.Tags.Select(t => t.ToString()).ToList(),
                    isRanchable = c.GetComponent<Capturable>() != null || c.GetComponents<UnityEngine.Component>().Any(comp => comp.GetType().Name == "Tameable"),
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    spaceRequired = spaceRequired,
                    temperatures = tv == null ? null : new {
                        lethalLowK = tempLethalLow,
                        warningLowK = tempWarningLow,
                        warningHighK = tempWarningHigh,
                        lethalHighK = tempLethalHigh,
                        lethalLowC = tempLethalLow - 273.15f,
                        warningLowC = tempWarningLow - 273.15f,
                        warningHighC = tempWarningHigh - 273.15f,
                        lethalHighC = tempLethalHigh - 273.15f
                    },
                    decor = dp == null ? null : new {
                        value = decor,
                        radius = decorRadius
                    },
                    lifespanCycles = lifespanCycles,
                    diet = dietList
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "critters.json"), JsonConvert.SerializeObject(critters, Formatting.Indented));
        }

        private static void DumpPlants(string outDir)
        {
            var plants = Assets.Prefabs.Where(p => (p.GetComponent<Crop>() != null || p.GetComponent<SeedProducer>() != null) && IsOfficialPrefab(p.gameObject)).Select(p => {
                // KPrefabID for DLC info
                var kpid = p.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                // Temperatures
                var tv = p.GetComponent<TemperatureVulnerable>();
                float? tempLethalLow = tv != null ? tv.TemperatureLethalLow : (float?)null;
                float? tempWarningLow = tv != null ? tv.TemperatureWarningLow : (float?)null;
                float? tempWarningHigh = tv != null ? tv.TemperatureWarningHigh : (float?)null;
                float? tempLethalHigh = tv != null ? tv.TemperatureLethalHigh : (float?)null;

                // Atmospheric pressures and gases (PressureVulnerable)
                var pv = p.GetComponent<PressureVulnerable>();
                float? pressLethalLow = null;
                float? pressWarningLow = null;
                float? pressWarningHigh = null;
                float? pressLethalHigh = null;
                List<string> safeGases = null;

                if (pv != null)
                {
                    pressLethalLow = GetPrivateField<float>(pv, "pressureLethal_Low");
                    pressWarningLow = GetPrivateField<float>(pv, "pressureWarning_Low");
                    pressWarningHigh = GetPrivateField<float>(pv, "pressureWarning_High");
                    pressLethalHigh = GetPrivateField<float>(pv, "pressureLethal_High");

                    var safeAtmos = GetPrivateField<HashSet<Element>>(pv, "safe_atmospheres");
                    if (safeAtmos != null)
                    {
                        safeGases = safeAtmos.Select(e => e.id.ToString()).ToList();
                    }
                }

                // Light requirements (IlluminationVulnerable)
                var iv = p.GetComponent<IlluminationVulnerable>();
                bool prefersDarkness = false;
                int lightThreshold = 0;
                if (iv != null)
                {
                    prefersDarkness = GetPrivateField<bool>(iv, "prefersDarkness");
                    lightThreshold = iv.LightIntensityThreshold;
                }

                // Fertilization requirements
                var fertDef = p.GetDef<FertilizationMonitor.Def>();
                List<object> fertList = null;
                if (fertDef != null && fertDef.consumedElements != null)
                {
                    fertList = new List<object>();
                    foreach (var info in fertDef.consumedElements)
                    {
                        fertList.Add(new
                        {
                            tag = info.tag.ToString(),
                            massConsumptionRateKgPerSec = info.massConsumptionRate,
                            kgPerCycle = info.massConsumptionRate * 600f
                        });
                    }
                }

                // Irrigation requirements
                var irrDef = p.GetDef<IrrigationMonitor.Def>();
                List<object> irrList = null;
                if (irrDef != null && irrDef.consumedElements != null)
                {
                    irrList = new List<object>();
                    foreach (var info in irrDef.consumedElements)
                    {
                        irrList.Add(new
                        {
                            tag = info.tag.ToString(),
                            massConsumptionRateKgPerSec = info.massConsumptionRate,
                            kgPerCycle = info.massConsumptionRate * 600f
                        });
                    }
                }

                // Growth duration and crop yields
                var crop = p.GetComponent<Crop>();
                float growthCycles = 0f;
                string cropId = null;
                int yieldAmount = 0;
                if (crop != null)
                {
                    growthCycles = crop.cropVal.cropDuration / 600f;
                    cropId = crop.cropVal.cropId;
                    yieldAmount = crop.cropVal.numProduced;
                }

                // Determine accepted planters
                var acceptedPlanters = new List<string>();
                var seedProducer = p.GetComponent<SeedProducer>();
                PlantableSeed plantableSeed = null;
                if (seedProducer != null)
                {
                    var seedInfoField = typeof(SeedProducer).GetField("seedInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var seedInfoVal = seedInfoField?.GetValue(seedProducer);
                    if (seedInfoVal != null)
                    {
                        var seedIdField = seedInfoVal.GetType().GetField("seedId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var seedId = seedIdField?.GetValue(seedInfoVal) as string;
                        if (!string.IsNullOrEmpty(seedId))
                        {
                            var seedPrefab = Assets.GetPrefab(seedId);
                            if (seedPrefab != null)
                            {
                                plantableSeed = seedPrefab.GetComponent<PlantableSeed>();
                            }
                        }
                    }
                }
                if (plantableSeed == null)
                {
                    var fallbackSeedPrefab = Assets.GetPrefab(p.gameObject.name + "Seed");
                    if (fallbackSeedPrefab != null)
                    {
                        plantableSeed = fallbackSeedPrefab.GetComponent<PlantableSeed>();
                    }
                }
                if (plantableSeed != null)
                {
                    var seedKpid = plantableSeed.GetComponent<KPrefabID>();
                    var seedDirectionObj = GetPrivateField<object>(plantableSeed, "direction");
                    var seedDirStr = seedDirectionObj != null ? seedDirectionObj.ToString() : "Top";

                    if (seedKpid != null)
                    {
                        foreach (var bdef in Assets.BuildingDefs)
                        {
                            var buildingPrefab = Assets.GetPrefab(bdef.PrefabID);
                            if (buildingPrefab == null) continue;

                            var receptacle = buildingPrefab.GetComponent<SingleEntityReceptacle>();
                            if (receptacle != null)
                            {
                                var planterDirectionObj = GetPrivateField<object>(receptacle, "direction");
                                var planterDirStr = planterDirectionObj != null ? planterDirectionObj.ToString() : "Top";
                                
                                bool directionMatches = (planterDirStr == "Any") || (seedDirStr == "Any") || (planterDirStr == seedDirStr);
                                if (!directionMatches) continue;

                                var possibleTags = GetPrivateField<List<Tag>>(receptacle, "possibleDepositTagsList");
                                bool tagMatches = false;
                                if (possibleTags != null)
                                {
                                    foreach (var tag in possibleTags)
                                    {
                                        if (tag == GameTags.Seed && seedKpid.HasTag(GameTags.Seed))
                                        {
                                            tagMatches = true;
                                            break;
                                        }
                                        if (tag == GameTags.DecorSeed && seedKpid.HasTag(GameTags.DecorSeed))
                                        {
                                            tagMatches = true;
                                            break;
                                        }
                                        if (seedKpid.HasTag(tag))
                                        {
                                            tagMatches = true;
                                            break;
                                        }
                                    }
                                }

                                if (tagMatches)
                                {
                                    acceptedPlanters.Add(bdef.PrefabID);
                                }
                            }
                        }
                    }
                }

                ExportImage(p.gameObject, p.gameObject.name, Path.Combine(outDir, "images"));

                return new
                {
                    id = p.gameObject.name,
                    name = CleanName(p.GetProperName(), p.gameObject.name),
                    prefabId = p.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    tags = kpid?.Tags.Select(t => t.ToString()).ToList(),
                    isFarmable = p.GetComponent<ReceptacleMonitor>() != null,
                    acceptedPlanters = acceptedPlanters,
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    temperatures = tv == null ? null : new {
                        lethalLowK = tempLethalLow,
                        warningLowK = tempWarningLow,
                        warningHighK = tempWarningHigh,
                        lethalHighK = tempLethalHigh,
                        lethalLowC = tempLethalLow - 273.15f,
                        warningLowC = tempWarningLow - 273.15f,
                        warningHighC = tempWarningHigh - 273.15f,
                        lethalHighC = tempLethalHigh - 273.15f
                    },
                    pressures = pv == null ? null : new {
                        lethalLow = pressLethalLow,
                        warningLow = pressWarningLow,
                        warningHigh = pressWarningHigh,
                        lethalHigh = pressLethalHigh,
                        safeAtmospheres = safeGases
                    },
                    light = iv == null ? null : new {
                        prefersDarkness = prefersDarkness,
                        lightThresholdLux = lightThreshold
                    },
                    fertilizerRequirements = fertList,
                    irrigationRequirements = irrList,
                    growthCycles = growthCycles,
                    yield = crop == null ? null : new {
                        itemId = cropId,
                        amount = yieldAmount
                    }
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "plants.json"), JsonConvert.SerializeObject(plants, Formatting.Indented));
        }

        private static void DumpGeysers(string outDir)
        {
            var geysers = Assets.Prefabs.Where(p => p.GetComponent<Geyser>() != null && IsOfficialPrefab(p.gameObject)).Select(g => {
                // KPrefabID for DLC info
                var kpid = g.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                var configurator = g.GetComponent<GeyserConfigurator>();
                string outputElement = null;
                float outputTempK = 0f;
                float maxPressure = 0f;
                float minRate = 0f, maxRate = 0f;
                float minIterLen = 0f, maxIterLen = 0f;
                float minIterPct = 0f, maxIterPct = 0f;
                float minYearLen = 0f, maxYearLen = 0f;
                float minYearPct = 0f, maxYearPct = 0f;

                if (configurator != null)
                {
                    var presetType = configurator.presetType;
                    var geyserTypesField = typeof(GeyserConfigurator).GetField("geyserTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                    var geyserTypes = geyserTypesField?.GetValue(configurator) as IList;
                    if (geyserTypes != null)
                    {
                        foreach (var gt in geyserTypes)
                        {
                            var idHashField = gt.GetType().GetField("idHash", BindingFlags.Public | BindingFlags.Instance);
                            var idHash = (HashedString)(idHashField?.GetValue(gt) ?? default(HashedString));
                            if (idHash == presetType)
                            {
                                var elField = gt.GetType().GetField("element", BindingFlags.Public | BindingFlags.Instance);
                                var tempField = gt.GetType().GetField("temperature", BindingFlags.Public | BindingFlags.Instance);
                                var minRateField = gt.GetType().GetField("minRatePerCycle", BindingFlags.Public | BindingFlags.Instance);
                                var maxRateField = gt.GetType().GetField("maxRatePerCycle", BindingFlags.Public | BindingFlags.Instance);
                                var pressField = gt.GetType().GetField("maxPressure", BindingFlags.Public | BindingFlags.Instance);
                                
                                var minIterLenField = gt.GetType().GetField("minIterationLength", BindingFlags.Public | BindingFlags.Instance);
                                var maxIterLenField = gt.GetType().GetField("maxIterationLength", BindingFlags.Public | BindingFlags.Instance);
                                var minIterPctField = gt.GetType().GetField("minIterationPercent", BindingFlags.Public | BindingFlags.Instance);
                                var maxIterPctField = gt.GetType().GetField("maxIterationPercent", BindingFlags.Public | BindingFlags.Instance);

                                var minYearLenField = gt.GetType().GetField("minYearLength", BindingFlags.Public | BindingFlags.Instance);
                                var maxYearLenField = gt.GetType().GetField("maxYearLength", BindingFlags.Public | BindingFlags.Instance);
                                var minYearPctField = gt.GetType().GetField("minYearPercent", BindingFlags.Public | BindingFlags.Instance);
                                var maxYearPctField = gt.GetType().GetField("maxYearPercent", BindingFlags.Public | BindingFlags.Instance);

                                outputElement = elField?.GetValue(gt)?.ToString();
                                outputTempK = (float)(tempField?.GetValue(gt) ?? 0f);
                                minRate = (float)(minRateField?.GetValue(gt) ?? 0f);
                                maxRate = (float)(maxRateField?.GetValue(gt) ?? 0f);
                                maxPressure = (float)(pressField?.GetValue(gt) ?? 0f);

                                minIterLen = (float)(minIterLenField?.GetValue(gt) ?? 0f);
                                maxIterLen = (float)(maxIterLenField?.GetValue(gt) ?? 0f);
                                minIterPct = (float)(minIterPctField?.GetValue(gt) ?? 0f);
                                maxIterPct = (float)(maxIterPctField?.GetValue(gt) ?? 0f);

                                minYearLen = (float)(minYearLenField?.GetValue(gt) ?? 0f);
                                maxYearLen = (float)(maxYearLenField?.GetValue(gt) ?? 0f);
                                minYearPct = (float)(minYearPctField?.GetValue(gt) ?? 0f);
                                maxYearPct = (float)(maxYearPctField?.GetValue(gt) ?? 0f);
                                break;
                            }
                        }
                    }
                }

                return new {
                    id = g.gameObject.name,
                    name = CleanName(g.GetProperName(), g.gameObject.name),
                    prefabId = g.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    tags = kpid?.Tags.Select(t => t.ToString()).ToList(),
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    geyserType = configurator?.presetType.ToString(),
                    element = outputElement,
                    temperature = configurator == null ? null : new {
                        kelvin = outputTempK,
                        celsius = outputTempK - 273.15f
                    },
                    maxPressure = maxPressure,
                    flowRateKgPerCycle = new {
                        min = minRate,
                        max = maxRate
                    },
                    eruptionPeriod = new {
                        minDurationSeconds = minIterLen,
                        maxDurationSeconds = maxIterLen,
                        minDutyCycle = minIterPct,
                        maxDutyCycle = maxIterPct
                    },
                    activityPeriod = new {
                        minDurationSeconds = minYearLen,
                        maxDurationSeconds = maxYearLen,
                        minDutyCycle = minYearPct,
                        maxDutyCycle = maxYearPct
                    }
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "geysers.json"), JsonConvert.SerializeObject(geysers, Formatting.Indented));
        }

        private static void DumpFoods(string outDir)
        {
            // Build a set of food IDs that are actually obtainable:
            // 1. Produced as the output of any recipe (e.g. cooked dishes at stoves/fryers)
            // 2. Directly harvestable as a crop yield from a plant
            // 3. Dropped directly by a critter (e.g. raw meat, eggs, fish fillets)
            // 4. Forage plants (wild-spawn edibles like Muckroot, Hexalent Fruit, etc.)
            var obtainableFoodIds = new HashSet<string>();

            // 1. Recipe outputs
            foreach (var recipe in ComplexRecipeManager.Get().recipes)
            {
                foreach (var output in recipe.results)
                {
                    obtainableFoodIds.Add(output.material.ToString());
                }
            }

            // 2. Crop yields from plants
            foreach (var prefab in Assets.Prefabs)
            {
                var crop = prefab.GetComponent<Crop>();
                if (crop != null && !string.IsNullOrEmpty(crop.cropVal.cropId))
                {
                    obtainableFoodIds.Add(crop.cropVal.cropId);
                }
            }

            // 3. Critter drop/butcher outputs (Diet produced elements that are edible)
            foreach (var prefab in Assets.Prefabs)
            {
                if (prefab.GetComponent<CreatureBrain>() == null) continue;
                var butcherable = prefab.GetComponent<Butcherable>();
                if (butcherable != null)
                {
                    var dropsField = typeof(Butcherable).GetField("drops", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var drops = dropsField?.GetValue(butcherable) as string[];
                    if (drops != null)
                    {
                        foreach (var drop in drops)
                        {
                            if (!string.IsNullOrEmpty(drop)) obtainableFoodIds.Add(drop);
                        }
                    }
                }
            }

            // 4. Forage/wild plants — any prefab whose name tag matches a food prefab that has no other source
            //    These are prefabs with both Edible AND a SeedProducer/ForagePlant-like component (they spawn in the world)
            foreach (var prefab in Assets.Prefabs)
            {
                if (prefab.GetComponent<Edible>() == null) continue;
                // If it has no Crop parent and no recipe but does spawn naturally, 
                // check if it has ForagePowaItem or similar marker
                var kpidCheck = prefab.GetComponent<KPrefabID>();
                if (kpidCheck != null && (
                    kpidCheck.HasTag(GameTags.GrowingPlant) ||
                    kpidCheck.HasTag(GameTags.Seed) ||
                    kpidCheck.HasTag(GameTags.CropSeed)
                ))
                {
                    obtainableFoodIds.Add(prefab.gameObject.name);
                }
            }

            var foods = Assets.Prefabs.Where(p =>
            {
                if (p.GetComponent<Edible>() == null) return false;
                if (!IsOfficialPrefab(p.gameObject)) return false;
                // Only include foods that are actually obtainable
                return obtainableFoodIds.Contains(p.gameObject.name);
            }).Select(f => {
                // KPrefabID for DLC info
                var kpid = f.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                var edible = f.GetComponent<Edible>();
                var info = edible?.FoodInfo;
                
                int moraleBonus = 0;
                if (info != null)
                {
                    // standard ONI morale values based on Quality index
                    if (info.Quality == -1) moraleBonus = -1;
                    else if (info.Quality == 0) moraleBonus = 0;
                    else if (info.Quality == 1) moraleBonus = 1;
                    else if (info.Quality == 2) moraleBonus = 4;
                    else if (info.Quality == 3) moraleBonus = 8;
                    else if (info.Quality == 4) moraleBonus = 12;
                    else if (info.Quality == 5) moraleBonus = 16;
                }

                ExportImage(f.gameObject, f.gameObject.name, Path.Combine(outDir, "images"));

                return new {
                    id = f.gameObject.name,
                    name = CleanName(f.GetProperName(), f.gameObject.name),
                    prefabId = f.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    tags = kpid?.Tags.Select(t => t.ToString()).ToList(),
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    caloriesPerUnit = info?.CaloriesPerUnit,
                    quality = info?.Quality,
                    moraleBonus = moraleBonus,
                    spoilTimeCycles = info?.SpoilTime,
                    deepFreezeTemp = info == null ? null : new {
                        kelvin = info.PreserveTemperature,
                        celsius = info.PreserveTemperature - 273.15f
                    },
                    rotTemp = info == null ? null : new {
                        kelvin = info.RotTemperature,
                        celsius = info.RotTemperature - 273.15f
                    },
                    canRot = info?.CanRot
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "foods.json"), JsonConvert.SerializeObject(foods, Formatting.Indented));
        }

        private static void DumpEquipment(string outDir)
        {
            var equipment = Assets.Prefabs.Where(p => p.GetComponent<Equippable>() != null && IsOfficialPrefab(p.gameObject)).Select(e => {
                // KPrefabID for DLC info
                var kpid = e.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                // Tank Capacity
                var suitTank = e.GetComponent<SuitTank>();
                float? tankCapacity = suitTank != null ? suitTank.capacity : (float?)null;
                string tankElement = suitTank != null ? suitTank.element : null;

                // Attribute Modifiers
                var def = e.GetComponent<Equippable>()?.def;
                List<object> attribList = null;
                if (def != null && def.AttributeModifiers != null)
                {
                    attribList = new List<object>();
                    foreach (var mod in def.AttributeModifiers)
                    {
                        attribList.Add(new
                        {
                            attributeId = mod.AttributeId,
                            value = mod.Value,
                            isMultiplier = mod.IsMultiplier
                        });
                    }
                }

                ExportImage(e.gameObject, e.gameObject.name, Path.Combine(outDir, "images"));

                return new
                {
                    id = e.gameObject.name,
                    name = e.GetProperName(),
                    prefabId = e.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    slot = def?.Slot,
                    tank = suitTank == null ? null : new {
                        capacityKg = tankCapacity,
                        element = tankElement
                    },
                    attributes = attribList
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "equipment.json"), JsonConvert.SerializeObject(equipment, Formatting.Indented));
        }

        private static void DumpElements(string outDir)
        {
            var elements = ElementLoader.elements.Where(e => !e.disabled).Select(e => {
                ExportImage(e, e.id.ToString(), Path.Combine(outDir, "images"));
                return new
                {
                id = e.id.ToString(),
                name = CleanName(e.name, e.id.ToString()),
                description = e.description,
                state = e.state.ToString(),
                specificHeatCapacity = e.specificHeatCapacity,
                thermalConductivity = e.thermalConductivity,
                molarMass = e.molarMass,
                highTemp = e.highTemp,
                highTempTransitionTarget = e.highTempTransitionTarget.ToString(),
                lowTemp = e.lowTemp,
                lowTempTransitionTarget = e.lowTempTransitionTarget.ToString(),
                hardness = e.hardness,
                dlcId = e.dlcId
                };
            }).ToList();

File.WriteAllText(Path.Combine(outDir, "elements.json"), JsonConvert.SerializeObject(elements, Formatting.Indented));
        }

        private static void DumpBuildings(string outDir)
        {
            var buildings = Assets.BuildingDefs.Where(b => IsOfficialBuilding(b)).Select(b => {
                ExportImage(b, b.PrefabID, Path.Combine(outDir, "images"));
                var buildingPrefab = Assets.GetPrefab(b.PrefabID);
                var buildingKpid = buildingPrefab != null ? buildingPrefab.GetComponent<KPrefabID>() : null;

                return new
                {
                id = b.PrefabID,
                name = CleanName(b.Name, b.PrefabID),
                tags = buildingKpid?.Tags.Select(t => t.ToString()).ToList(),
                description = b.Desc,
                effect = b.Effect,
                mass = b.Mass,
                materialCategory = b.MaterialCategory,
                energyConsumptionWhenActive = b.EnergyConsumptionWhenActive,
                exhaustKilowattsWhenActive = b.ExhaustKilowattsWhenActive,
                widthInCells = b.WidthInCells,
                heightInCells = b.HeightInCells,
                requiredDlcIds = b.RequiredDlcIds,
                forbiddenDlcIds = b.ForbiddenDlcIds
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "buildings.json"), JsonConvert.SerializeObject(buildings, Formatting.Indented));
        }

        private static void DumpSpacePOIs(string outDir)
        {
            var spacePOIs = Assets.Prefabs.Where(p => p.GetComponent<HarvestablePOIConfigurator>() != null && IsOfficialPrefab(p.gameObject)).Select(p => {
                // KPrefabID for DLC info
                var kpid = p.GetComponent<KPrefabID>();
                var reqDlc = kpid != null ? kpid.requiredDlcIds : null;
                var forbDlc = kpid != null ? kpid.forbiddenDlcIds : null;

                var configurator = p.GetComponent<HarvestablePOIConfigurator>();
                float capMin = 0f, capMax = 0f;
                float rechMin = 0f, rechMax = 0f;
                List<object> resourceList = null;

                if (configurator != null)
                {
                    var presetType = configurator.presetType;
                    var poiTypesField = typeof(HarvestablePOIConfigurator).GetField("_poiTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                    var poiTypes = poiTypesField?.GetValue(configurator) as IList;
                    if (poiTypes != null)
                    {
                        foreach (var pt in poiTypes)
                        {
                            var idHashField = pt.GetType().GetField("idHash", BindingFlags.Public | BindingFlags.Instance);
                            var idHash = (HashedString)(idHashField?.GetValue(pt) ?? default(HashedString));
                            if (idHash == presetType)
                            {
                                var capMinField = pt.GetType().GetField("poiCapacityMin", BindingFlags.Public | BindingFlags.Instance);
                                var capMaxField = pt.GetType().GetField("poiCapacityMax", BindingFlags.Public | BindingFlags.Instance);
                                var rechMinField = pt.GetType().GetField("poiRechargeMin", BindingFlags.Public | BindingFlags.Instance);
                                var rechMaxField = pt.GetType().GetField("poiRechargeMax", BindingFlags.Public | BindingFlags.Instance);
                                var elField = pt.GetType().GetField("harvestableElements", BindingFlags.Public | BindingFlags.Instance);

                                capMin = (float)(capMinField?.GetValue(pt) ?? 0f);
                                capMax = (float)(capMaxField?.GetValue(pt) ?? 0f);
                                rechMin = (float)(rechMinField?.GetValue(pt) ?? 0f);
                                rechMax = (float)(rechMaxField?.GetValue(pt) ?? 0f);

                                var elements = elField?.GetValue(pt) as IDictionary;
                                if (elements != null)
                                {
                                    resourceList = new List<object>();
                                    foreach (DictionaryEntry entry in elements)
                                    {
                                        resourceList.Add(new
                                        {
                                            element = entry.Key?.ToString(),
                                            ratio = entry.Value
                                        });
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                return new
                {
                    id = p.gameObject.name,
                    name = CleanName(p.GetProperName(), p.gameObject.name),
                    prefabId = p.GetComponent<KPrefabID>()?.PrefabTag.ToString(),
                    requiredDlcIds = reqDlc,
                    forbiddenDlcIds = forbDlc,
                    poiType = configurator?.presetType.ToString(),
                    capacityRangeMetricTons = new {
                        min = capMin / 1000f, // raw POI capacity is in kg
                        max = capMax / 1000f
                    },
                    rechargeRangeKgPerCycle = new {
                        min = rechMin,
                        max = rechMax
                    },
                    resources = resourceList
                };
            }).ToList();

            File.WriteAllText(Path.Combine(outDir, "space_pois.json"), JsonConvert.SerializeObject(spacePOIs, Formatting.Indented));
        }
    }
}
