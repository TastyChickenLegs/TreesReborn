using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static TreesReborn.ServerSyncWrapper;

namespace TreesReborn
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class TreesRebornPlugin : BaseUnityPlugin
    {
        internal const string ModName = "TreesReborn";
        internal const string ModVersion = "1.0.8";
        internal const string Author = "TastyChickenLegs";
        private const string ModGUID = Author + "." + ModName;
        private static ConfigEntry<bool> isDebug;
        internal static ConfigEntry<bool> continousLogs;
        internal static ConfigEntry<AlwaysEnabledToggle> isServerSyncOn;
        public static Dictionary<string, string> seedsDic = new Dictionary<string, string>();
        private readonly Harmony harmony = new(ModGUID);
        public static ConfigEntry<float> respawnDelay;
        public static ConfigEntry<float> craftingtablenear;
        public static ConfigEntry<bool> useRandomSapling;
        public static ConfigEntry<float> treeRate;

        public static readonly ManualLogSource TreesRebornLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);

        public static ConfigEntry<bool> modEnabled;

        private static readonly ConfigSync ConfigSync = new(ModGUID)
        { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        public static ConfigEntry<bool> growthSpace;
        private static TreesRebornPlugin context;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                UnityEngine.Debug.Log((pref ? typeof(TreesRebornPlugin).Namespace + " " : "") + str);
        }

        public void Awake()
        {
            context = this;
            isServerSyncOn = this.Config.BindHiddenForceEnabledSyncLocker(ConfigSync, "", "Use Server Sync");
            continousLogs = this.Config.Bind("General", "Show Continous Logs", false);
            isDebug = Config.Bind<bool>("General", "DebugMode", false);
            respawnDelay = Config.Bind<float>("General", "RespawnDelay", 2.5f, "Delay in seconds to spawn sapling");
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            growthSpace = Config.Bind<bool>("General", "GrowthSpaceRestriction", true,
                new ConfigDescription("Turn off the Growth Space Restrictions for Saplings", null,
                new ConfigurationManagerAttributes { DispName = "Disable Not Enough Space" }));
            useRandomSapling = Config.Bind<bool>("General", "useRandomSapling", false,
                new ConfigDescription("Replant Random Sapling", null,
                new ConfigurationManagerAttributes { DispName = "Replant Random Sapling" }));
            treeRate = Config.Bind<float>("General", "treeRate", 1f,
                new ConfigDescription("Chance that trees will be replanted.",
                new AcceptableValueRange<float>(0f, 1f), null,
                new ConfigurationManagerAttributes { ShowRangeAsPercent = true, DispName = "Replant Chance" }));
            craftingtablenear = Config.Bind<float>("General", "checkcraftingtable", 0f,
                new ConfigDescription("Check for Crafting Table Distance.  Use zero to disable.",
                new AcceptableValueRange<float>(0f, 100f), null,
                new ConfigurationManagerAttributes { DispName = "Check for Crafting Table" }));

            if (!modEnabled.Value)
                return;

            Assembly assembly = Assembly.GetExecutingAssembly();
            harmony.PatchAll(assembly);
        }

        private void Start()
        {
            string jsonFile = "tree_dict.json";
            if (Chainloader.PluginInfos.ContainsKey("advize.PlantEverything"))
                jsonFile = "tree_dict_Plant_Everything.json";
            else if (Chainloader.PluginInfos.ContainsKey("com.Defryder.Plant_all_trees"))
                jsonFile = "tree_dict_Plant_all_trees.json";
            else if (Chainloader.PluginInfos.ContainsKey("com.bkeyes93.PlantingPlus"))
                jsonFile = "tree_dict_PlantingPlus.json";

            string modPath = Path.GetDirectoryName(Info.Location);
            string path = Path.Combine(modPath, "configs", jsonFile);
            //string path = Path.Combine(Path.Combine(modPath, "configs/" + jsonFile));
            //Path.Combine(Path.GetDirectoryName(Plugin.Info.Location), "configs", ")

            string json = File.ReadAllText(path);

            SeedData seeds = JsonUtility.FromJson<SeedData>(json);

            foreach (string seed in seeds.seeds)
            {
                string[] split = seed.Split(':');
                seedsDic.Add(split[0], split[1]);
            }

            Dbgl($"Loaded {seedsDic.Count} seeds from {path}");
        }

        [HarmonyPatch(typeof(Destructible), "Destroy")]
        private static class Destroy_Patch
        {
            private static void Prefix(Destructible __instance)
            {
                if (Player.m_localPlayer)
                {
                    Dbgl($"destroyed destructible {__instance.name}");

                    // code to check for crafting table.  If zero then it is disabled. If a table is within configured range then stop

                    CraftingStation nearestcrafting = CraftingStation.FindClosestStationInRange("$piece_workbench", __instance.transform.position, craftingtablenear.Value);
                    if (nearestcrafting != null)
                    {
                        Dbgl($"{nearestcrafting}");
                        return;
                    }

                    List<string> keyList = new List<string>(seedsDic.Values);

                    string name;
                    //random sapling code

                    name = seedsDic.FirstOrDefault(s => __instance.name.StartsWith(s.Key)).Value;

                    if (name != null)
                    {
                        if (useRandomSapling.Value)
                        {
                            name = keyList[Random.Range(0, keyList.Count)];
                        }
                        bool ward = PrivateArea.CheckAccess(__instance.transform.position, 0f, true, true);
                        //Dbgl($"destroyed trunk {__instance.name}, trying to spawn {name}");
                        GameObject prefab = ZNetScene.instance.GetPrefab(name);
                        if (prefab != null)
                        {
                            if (Random.value < treeRate.Value)
                            {
                                //Dbgl($"trying to spawn new tree,");
                                TreesRebornLogger.LogInfo($"destroyed trunk {__instance.name}, trying to spawn {name}");
                                context.StartCoroutine(SpawnTree(prefab, __instance.transform.position));
                            }
                        }
                        else
                        {
                            Dbgl($"prefab is null");
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Plant), "HaveGrowSpace")]
        private static class Grow_Space_Patch
        {
            private static void Postfix(Plant __instance, ref bool __result)
            {
                if (growthSpace.Value)
                {
                    __result = true;
                    return;
                }
            }
        }

        private static IEnumerator SpawnTree(GameObject prefab, Vector3 position)
        {
            Dbgl($"spawning new tree");
            yield return new WaitForSeconds(respawnDelay.Value);
            Instantiate(prefab, position, Quaternion.identity);
            Dbgl($"created new {prefab.name}");
        }
    }
}