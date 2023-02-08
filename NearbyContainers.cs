using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

namespace CraftFromContainers
{
    /// <summary>
    /// Borrowed from https://github.com/aedenthorn/ValheimMods
    /// </summary>
    public class NearbyContainers : BaseUnityPlugin
    {
        public static ConfigEntry<float> m_range;

        public static ConfigEntry<bool> ignoreShipContainers;
        public static ConfigEntry<bool> ignoreWagonContainers;
        public static ConfigEntry<bool> ignoreWoodChests;
        public static ConfigEntry<bool> ignorePrivateChests;
        public static ConfigEntry<bool> ignoreBlackMetalChests;
        public static ConfigEntry<bool> ignoreReinforcedChests;

        public static ConfigEntry<bool> allowChangingAmmoTypes;
        public static ConfigEntry<int> reloadTurretsBelow;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;

        public static List<Container> containerList = new List<Container>();

        public class ConnectionParams
        {
            public GameObject connection = null;
            public Vector3 stationPos;
        }

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(NearbyContainers).Namespace + " " : "") + str);
        }
        public static void Init(ConfigFile config)
        {
            modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = config.Bind<bool>("General", "IsDebug", true, "Show debug messages in log");

            m_range = config.Bind<float>("General", "ContainerRange", 10f, "The maximum range from which to pull items from");
            //ignoreRangeInBuildArea = Config.Bind<bool>("General", "IgnoreRangeInBuildArea", true, "Ignore range for building pieces when in build area.");
            
            ignoreShipContainers = config.Bind<bool>("Container Types", "IgnoreShipContainers", false, "If true, will ignore this type of container.");
            ignoreWagonContainers = config.Bind<bool>("Container Types", "IgnoreWagonContainers", false, "If true, will ignore this type of container.");
            ignoreWoodChests = config.Bind<bool>("Container Types", "IgnoreWoodChests", false, "If true, will ignore this type of container.");
            ignorePrivateChests = config.Bind<bool>("Container Types", "IgnorePrivateChests", false, "If true, will ignore this type of container.");
            ignoreBlackMetalChests = config.Bind<bool>("Container Types", "IgnoreBlackMetalChests", false, "If true, will ignore this type of container.");
            ignoreReinforcedChests = config.Bind<bool>("Container Types", "IgnoreReinforcedChests", false, "If true, will ignore this type of container.");

            allowChangingAmmoTypes = config.Bind<bool>("General", "AllowChangingAmmoTypes", true, "If false, ballista will only reload with the previous ammo type when empty.");
            reloadTurretsBelow = config.Bind<int>("General", "ReloadTurretsBelow", 5, "Ballista will automatically reload when their stored ammo drops below this amount.");

            if (!modEnabled.Value)
                return;
        }

        public static List<Container> GetNearbyContainers(Vector3 center)
        {
            List<Container> containers = new List<Container>();
            foreach (Container container in containerList)
            {
                if (container != null
                    && container.GetComponentInParent<Piece>() != null
                    && Player.m_localPlayer != null
                    && container?.transform != null
                    && container.GetInventory() != null
                    && (m_range.Value <= 0 || Vector3.Distance(center, container.transform.position) < m_range.Value)
                    //&& (!PrivateArea.CheckInPrivateArea(container.transform.position) || PrivateArea.CheckAccess(container.transform.position, 0f, true))
                    && (!container.m_checkGuardStone || PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
                    && Traverse.Create(container).Method("CheckAccess", new object[] { Player.m_localPlayer.GetPlayerID() }).GetValue<bool>() && !container.IsInUse()
                    && AllowContainerType(container))
                {
                    //container.GetComponent<ZNetView>()?.ClaimOwnership();

                    containers.Add(container);
                }
            }
            return containers;
        }

        private static bool AllowContainerType(Container __instance)
        {
            Ship ship = __instance.gameObject.transform.parent?.GetComponent<Ship>();
            return !(ship != null && ignoreShipContainers.Value) && !(__instance.m_wagon && ignoreWagonContainers.Value) && !(__instance.name.StartsWith("piece_chest_wood(") && ignoreWoodChests.Value) && !(__instance.name.StartsWith("piece_chest_private(") && ignorePrivateChests.Value) && !(__instance.name.StartsWith("piece_chest_blackmetal(") && ignoreBlackMetalChests.Value) && !(__instance.name.StartsWith("piece_chest(") && ignoreReinforcedChests.Value);
        }

        
        public static IEnumerator AddContainer(Container container, ZNetView nview)
        {
            yield return null;
            try
            {
                //Dbgl($"Checking {container.name} {nview != null} {nview?.GetZDO() != null} {nview?.GetZDO()?.GetLong("creator".GetStableHashCode(), 0L)}");
                if (container.GetInventory() != null && nview?.GetZDO() != null && (container.name.StartsWith("piece_") || container.name.StartsWith("Container") || nview.GetZDO().GetLong("creator".GetStableHashCode(), 0L) != 0))
                {
                    //Dbgl($"Adding {container.name}");
                    containerList.Add(container);
                }
            }
            catch
            {

            }
            yield break;
        }

        public static void PullResources(Turret turret, int qty, Action<string> loadTurretAction)
        {
            var lastAmmo = turret.GetAmmoItem().m_shared.m_name;

            Dbgl($"looking for {qty} {lastAmmo} for {turret.name}");
            Dbgl("Allowed ammo types: ");

            foreach(var ammo in turret.m_allowedAmmo)
            {
                Dbgl($"{ammo.m_ammo.name} : {ammo.m_ammo.m_itemData.m_shared.m_name}");
            }

            var ammoType = turret.m_allowedAmmo.First(x => x.m_ammo.m_itemData.m_shared.m_name == lastAmmo);

            bool found = PullResourcesCore(turret, qty, ammoType, loadTurretAction);

            if(!found)
            {
                Dbgl($"turret has ammo: {turret.HasAmmo()}");
            }

            if (allowChangingAmmoTypes.Value)
            {
                if (!found && !turret.HasAmmo())
                {
                    foreach (var ammo in turret.m_allowedAmmo)
                    {
                        Dbgl($"next ammo type: {ammo.m_ammo.name}");
                        if (ammo.m_ammo == ammoType.m_ammo)
                        {
                            Dbgl($"{ammo.m_ammo.name} is the same as {ammoType.m_ammo.name}, skipping ");
                            continue;
                        }
                        found = PullResourcesCore(turret, qty, ammo, loadTurretAction);

                        if (found)
                            return;
                    }
                }
            }
        }

        static bool PullResourcesCore(Turret turret, int qty, Turret.AmmoType ammoType, Action<string> loadTurretAction)
        {
            var reqAmmo = ammoType.m_ammo.m_itemData.m_shared.m_name;

            Dbgl($"looking for {qty} {reqAmmo} for {turret.name}");
            List<Container> nearbyContainers = GetNearbyContainers(turret.transform.position);

            string reqName = reqAmmo;
            int totalAmount = 0;
            //Dbgl($"have {totalAmount}/{totalRequirement} {reqName} in player inventory");
            bool foundItem = false;
            foreach (Container c in nearbyContainers)
            {
                lock (c)
                {
                    Dbgl($"checking container {c}");

                    Inventory cInventory = c.GetInventory();
                    int thisAmount = Mathf.Min(cInventory.CountItems(reqName), qty - totalAmount);

                    Dbgl($"Container at {c.transform.position} has {cInventory.CountItems(reqName)}");

                    if (thisAmount == 0)
                        continue;

                    for (int i = 0; i < cInventory.GetAllItems().Count; i++)
                    {
                        ItemDrop.ItemData item = cInventory.GetItem(i);
                        if (item.m_shared.m_name == reqName)
                        {
                            Dbgl($"Got stack of {item.m_stack} {reqName}");
                            int stackAmount = Mathf.Min(item.m_stack, qty - totalAmount);

                            Dbgl($"Sending {stackAmount} {reqName} to turret");

                            var currentAmount = turret.GetAmmo();

                            for (int a = 0; a < stackAmount; a++)
                                loadTurretAction(ammoType.m_ammo.name);

                            //don't deplete the items from the box if we didn't successfully load the turret
                            if (turret.GetAmmo() > currentAmount)
                            {
                                if (stackAmount == item.m_stack)
                                    cInventory.RemoveItem(i);
                                else
                                    item.m_stack -= stackAmount;
                            }

                            foundItem = true;

                            totalAmount += stackAmount;
                            Dbgl($"total amount is now {totalAmount}/{qty} {reqName}");

                            if (totalAmount >= qty)
                                break;
                        }
                    }
                    c.GetType().GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { });
                    cInventory.GetType().GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cInventory, new object[] { });

                    if (totalAmount >= qty)
                    {
                        Dbgl($"pulled enough {reqName}");
                        break;
                    }
                }
            }

            return foundItem;
        }
    }
}
