using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FastVentMod
{
    // Clones vanilla vents and their kits before the game loads its prefabs, as the DS-8 Server Rack mod does.
    [HarmonyPatch(typeof(Prefab), "LoadAll")]
    static class PrefabFactory
    {
        static bool _done;

        static void Prefix()
        {
            if (_done || WorldManager.Instance == null) return;
            _done = true;
            try { Build(); }
            catch (Exception e) { Debug.LogError("[FastVent] could not create prefabs: " + e); }
        }

        static void Build()
        {
            var holder = new GameObject("~FastVentPrefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var made = new List<Thing>();

            var fastVent = CloneWithKit<ActiveVent>("StructureActiveVent", "ItemActiveVent",
                FastVent.PrefabName, FastVent.KitName, holder.transform, made, out var activeVent);
            if (fastVent != null) FastVent.Configure(fastVent, activeVent);

            var reliefVent = CloneWithKit<PassiveVent>("StructurePassiveVent", "ItemPassiveVent",
                PressureReliefVent.PrefabName, PressureReliefVent.KitName, holder.transform, made, out _);
            if (reliefVent != null) PressureReliefVent.Configure(reliefVent, Source<Pipe>(PressureReliefVent.StorageSource));

            var prefabs = WorldManager.Instance.SourcePrefabs;
            foreach (var thing in made)
                if (!prefabs.Contains(thing)) prefabs.Add(thing);
            Debug.Log("[FastVent] prefabs created: " + string.Join(", ", made.Select(thing => thing.PrefabName)));
        }

        // Clones a structure and its kit; the new kit builds only the new structure. Null if either is missing.
        static T CloneWithKit<T>(string sourceName, string sourceKitName, string name, string kitName,
            Transform parent, List<Thing> made, out T source) where T : Structure
        {
            source = Source<T>(sourceName);
            var sourceKit = Source<Item>(sourceKitName);
            if (source == null || sourceKit == null)
            {
                Debug.LogError($"[FastVent] {sourceName} / {sourceKitName} not found - did the game rename them? No {name}.");
                return null;
            }
            var structure = Clone(source, name, parent);
            var kit = Clone(sourceKit, kitName, parent);
            BindKit(kit, sourceKit, structure);
            made.Add(structure);
            made.Add(kit);
            return structure;
        }

        static T Source<T>(string prefabName) where T : Thing
        {
            foreach (var thing in WorldManager.Instance.SourcePrefabs)
                if (thing != null && thing.PrefabName == prefabName) return thing as T;
            return null;
        }

        static T Clone<T>(T source, string name, Transform parent) where T : Thing
        {
            var clone = Object.Instantiate(source, parent);
            clone.gameObject.name = name;
            clone.PrefabName = name;
            clone.PrefabHash = Animator.StringToHash(name);
            FixOwners(clone);
            return clone;
        }

        // Points the clone's parts at the clone rather than the thing it was copied from.
        static void FixOwners(Thing thing)
        {
            if (thing is SmallGrid grid && grid.OpenEnds != null)
                foreach (var end in grid.OpenEnds)
                    if (end != null) end.Parent = grid;
            if (thing.Slots != null)
                foreach (var slot in thing.Slots)
                    if (slot != null) slot.Parent = thing;
            if (thing.Renderers != null)
                foreach (var renderer in thing.Renderers)
                    if (renderer != null) renderer.Parent = thing;
            if (thing.AudioEvents != null)
                foreach (var audio in thing.AudioEvents)
                    if (audio != null) audio.Parent = thing;
            if (thing.Interactables == null) return;
            foreach (var interactable in thing.Interactables)
            {
                if (interactable == null) continue;
                interactable.Parent = thing;
                if (interactable.AssociatedAudioEvents != null)
                    foreach (var audio in interactable.AssociatedAudioEvents)
                        if (audio != null) audio.Parent = thing;
            }
        }

        // The kit builds only the structure, and the structure's build states take the kit, so deconstructing
        // gives it back. ToolExit (the tool that takes a stage apart) stays as it is.
        static void BindKit(Item kit, Item sourceKit, Structure structure)
        {
            switch (kit)
            {
                case MultiConstructor multi:
                    multi.Constructables = new List<Structure> { structure };
                    break;
                case Constructor single:
                    single.BuildStructure = structure;
                    break;
                default:
                    throw new InvalidOperationException($"{kit.PrefabName} is not a construction kit ({kit.GetType().Name})");
            }
            foreach (var state in structure.BuildStates)
            {
                var tool = state?.Tool;
                if (tool == null) continue;
                if (tool.ToolEntry != null && tool.ToolEntry.PrefabHash == sourceKit.PrefabHash) tool.ToolEntry = kit;
                if (tool.ToolEntry2 != null && tool.ToolEntry2.PrefabHash == sourceKit.PrefabHash) tool.ToolEntry2 = kit;
            }
        }
    }
}
