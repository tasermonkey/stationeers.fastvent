using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster;
using UnityEngine;

namespace FastVentMod
{
    // StationeersLaunchPad calls OnLoaded on this, as it does for the DS-8 Server Rack mod's plugin.
    public class FastVentPlugin : MonoBehaviour
    {
        public const string Version = "1.0.0";

        public static readonly Mod MOD = new Mod("FastVent", Version);

        public void OnLoaded(List<GameObject> prefabs, ConfigFile config)
        {
            // Saves with Fast Vents hold a prefab only this mod knows, so every player needs it.
            MOD.Networking.Required = true;
            new Harmony("tasermonkey.fastvent").PatchAll();
            Debug.Log($"[FastVent] {Version} loaded");
        }
    }
}
