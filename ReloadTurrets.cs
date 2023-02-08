using BepInEx;
using BepInEx.Logging;
using CraftFromContainers;
using HarmonyLib;
using System.Reflection;
using static UnityEngine.GraphicsBuffer;

namespace FriendlyTurrets
{
    [BepInPlugin("ByteArtificer.ReloadTurretsFromContainers", "Reload Ballista From Nearby Containers", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class ReloadTurrets : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony("ByteArtificer.ReloadTurretsFromContainers");
        public static ManualLogSource _logger;

        private static ReloadTurrets _context = null;


        public void Awake()
        {
            _logger = Logger;
            _context = this;
            NearbyContainers.Init(Config);
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.ShootProjectile))]
        public static class Turret_ShootProjectile_Prefix
        {
            static void Postfix(ref Turret __instance)
            {
                var turret = __instance;
                int ammo = __instance.GetAmmo();
                _logger.LogMessage($"Turret fired, {ammo}/{__instance.m_maxAmmo} ammo remains");
                if (ammo < NearbyContainers.reloadTurretsBelow.Value)
                {
                    _logger.LogMessage("Turret needs reloading");
                    NearbyContainers.PullResources(__instance, __instance.m_maxAmmo - ammo, (ammoType) => turret.GetType().GetMethod(nameof(Turret.RPC_AddAmmo), BindingFlags.NonPublic | BindingFlags.Instance).Invoke(turret, new object[] {0, ammoType}));
                }
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        static class Container_Awake_Patch
        {
            static void Postfix(Container __instance, ZNetView ___m_nview)
            {
                _context.StartCoroutine(NearbyContainers.AddContainer(__instance, ___m_nview));
            }

        }

        [HarmonyPatch(typeof(Container), nameof(Container.OnDestroyed))]
        static class Container_OnDestroyed_Patch
        {
            static void Prefix(Container __instance)
            {
                NearbyContainers.containerList.Remove(__instance);

            }
        }


    }
}