using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using HarmonyLib;
using UnityEngine;

namespace FastVentMod
{
    // The Pressure Relief Vent is a clone of the Passive Vent that lets gas out of its pipe only above 5 MPa, and
    // then only the gas above 5 MPa, so the pipe is held there. Gas into the pipe flows as through a Passive Vent.
    // It holds as much as an in-line Small Tank: a pipe network's volume is the sum of its pieces' volumes.
    public static class PressureReliefVent
    {
        public const string PrefabName = "StructurePressureReliefVent";
        public const string KitName = "ItemKitPressureReliefVent";
        public const string StorageSource = "StructureTankSmallInLine";
        public static readonly PressurekPa Threshold = new PressurekPa(5000);
        public static readonly int Hash = Animator.StringToHash(PrefabName);

        static readonly AccessTools.FieldRef<Pipe, float> Volume = AccessTools.FieldRefAccess<Pipe, float>("volume");

        public static void Configure(PassiveVent reliefVent, Pipe smallTank)
        {
            if (smallTank == null)
            {
                Debug.LogError($"[FastVent] {StorageSource} not found: the Pressure Relief Vent keeps the Passive Vent's volume");
                return;
            }
            var passiveVentVolume = Volume(reliefVent);
            Volume(reliefVent) = Volume(smallTank);
            Debug.Log($"[FastVent] Pressure Relief Vent volume {Volume(reliefVent)} L (Passive Vent's {passiveVentVolume} L)");
        }

        // Stands in for AtmosphereHelper.Mix(pipe, world, state) in PassiveVent.OnAtmosphericTick.
        public static void Mix(Atmosphere pipe, Atmosphere world, AtmosphereHelper.MatterState state, PassiveVent vent)
        {
            if (vent.PrefabHash != Hash || pipe == null || world == null)
            {
                AtmosphereHelper.Mix(pipe, world, state);
                return;
            }
            var pipePressure = pipe.PressureGasses;
            var worldPressure = world.PressureGasses;
            // Into the pipe, or out of it with both sides over the limit (they even out above it): as a Passive Vent.
            if (pipePressure <= worldPressure || worldPressure >= Threshold)
            {
                AtmosphereHelper.Mix(pipe, world, state);
                return;
            }
            if (pipePressure <= Threshold) return;
            // Out of the pipe: the gas above the limit, but never more than evens the two sides out.
            var pipeVolume = pipe.GetVolume(AtmosphereHelper.MatterState.Gas);
            var worldVolume = world.GetVolume(AtmosphereHelper.MatterState.Gas);
            var excess = IdealGas.Quantity(pipePressure - Threshold, pipeVolume, pipe.Temperature);
            var toEven = IdealGas.Quantity(pipePressure - worldPressure,
                pipeVolume * worldVolume / (pipeVolume + worldVolume), pipe.Temperature);
            world.Add(pipe.Remove(RocketMath.Min(excess, toEven), AtmosphereHelper.MatterState.Gas));
        }
    }

    // Every Passive Vent's tick calls PressureReliefVent.Mix, with the vent, where it called AtmosphereHelper.Mix.
    [HarmonyPatch(typeof(PassiveVent), nameof(PassiveVent.OnAtmosphericTick))]
    static class ReliefPatch
    {
        static readonly MethodInfo VanillaMix = AccessTools.Method(typeof(AtmosphereHelper), nameof(AtmosphereHelper.Mix),
            new[] { typeof(Atmosphere), typeof(Atmosphere), typeof(AtmosphereHelper.MatterState) });
        static readonly MethodInfo ReliefMix = AccessTools.Method(typeof(PressureReliefVent), nameof(PressureReliefVent.Mix));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(VanillaMix))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    instruction.operand = ReliefMix;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0)
                Debug.LogError("[FastVent] PassiveVent.OnAtmosphericTick no longer calls AtmosphereHelper.Mix:"
                    + " Pressure Relief Vents will work like Passive Vents");
        }
    }
}
