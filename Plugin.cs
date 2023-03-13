using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using static TreesReborn.ServerSyncWrapper;

namespace TreesReborn
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class TreesRebornPlugin : BaseUnityPlugin
    {
        internal const string ModName = "TreesReborn";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "TastyChickenLegs";
        private const string ModGUID = Author + "." + ModName;
        private static readonly bool isDebug = true;
        internal static ConfigEntry<bool> continousLogs;
        internal static ConfigEntry<AlwaysEnabledToggle> isServerSyncOn;
        public static Dictionary<string, string> seedsDic = new Dictionary<string, string>();
        private readonly Harmony harmony = new(ModGUID);
        public static ConfigEntry<float> respawnDelay;
        public static readonly ManualLogSource TreesRebornLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);
        public static ConfigEntry<bool> modEnabled;
        private static readonly ConfigSync ConfigSync = new(ModGUID)
            { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public static ConfigEntry<bool> growthSpace;
        private static TreesRebornPlugin context;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug)
               UnityEngine.Debug.Log((pref ? typeof(TreesRebornPlugin).Namespace + " " : "") + str);
        }
        public void Awake()
        {
            context = this;
            isServerSyncOn = this.Config.BindHiddenForceEnabledSyncLocker(ConfigSync, "", "Use Server Sync");
            continousLogs = this.Config.Bind("General", "Show Continous Logs", false);
            respawnDelay = Config.Bind<float>("General", "RespawnDelay", 2.5f, "Delay in seconds to spawn sapling");
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            growthSpace = Config.Bind<bool>("General", "GrowthSpaceRestriction", true,
                new ConfigDescription("Turn off the Growth Space Restrictions for Saplings", null,
                new ConfigurationManagerAttributes { DispName = "Disable Not Enough Space" }));

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

            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"configs", jsonFile);


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
        static class Destroy_Patch
        {
            static void Prefix(Destructible __instance)
            {
                if (Player.m_localPlayer)
                {
                    Dbgl($"destroyed destructible {__instance.name}");

                    string name = seedsDic.FirstOrDefault(s => __instance.name.StartsWith(s.Key)).Value;

                    if (name != null)
                    {
                        Dbgl($"destroyed trunk {__instance.name}, trying to spawn {name}");
                        GameObject prefab = ZNetScene.instance.GetPrefab(name);
                        if (prefab != null)
                        {
                            Dbgl($"trying to spawn new tree");
                            context.StartCoroutine(SpawnTree(prefab, __instance.transform.position));
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

        static class Grow_Space_Patch
        {
            static void Postfix(Plant __instance, ref bool __result)
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
