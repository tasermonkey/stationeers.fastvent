using System;
using System.Linq;
using System.Text;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using HarmonyLib;
using Objects.Electrical;
using UnityEngine;

namespace FastVentMod
{
    // The Fast Vent is a clone of the Active Vent whose flow limit per tick is the Active Vent's times its Setting.
    // Setting is the vanilla vent's own (unused) one: the Logic Writer and IC10 can write it, the game clamps it
    // to MinSetting..MaxSetting, saves it and sends it to clients.
    // Like the large Powered Vent, what its own cell doesn't take of the limit goes to the cells open to it.
    public static class FastVent
    {
        public const string PrefabName = "StructureFastVent";
        public const string KitName = "ItemKitFastVent";
        public const float DefaultMultiplier = 5f;
        public const float MinMultiplier = 1f;
        public const float MaxMultiplier = 20f;
        public static readonly int Hash = Animator.StringToHash(PrefabName);

        // The Active Vent prefab's flow limit, in kPa per tick.
        public static float BasePressurePerTick;

        // How many shells of open neighbouring cells it reaches: the multigrid Powered Vent's.
        public static int GridMixingDepth = 4;

        public static readonly AccessTools.FieldRef<DeviceAtmospherics, float> PressurePerTick =
            AccessTools.FieldRefAccess<DeviceAtmospherics, float>("pressurePerTick");
        static readonly AccessTools.FieldRef<DeviceAtmospherics, float> OutputSetting =
            AccessTools.FieldRefAccess<DeviceAtmospherics, float>("outputSetting");
        static readonly AccessTools.FieldRef<PoweredVentMultiGrid, int> PoweredVentDepth =
            AccessTools.FieldRefAccess<PoweredVentMultiGrid, int>("gridMixingDepth");
        static readonly Func<DeviceOutput, bool> IsOperable = AccessTools.MethodDelegate<Func<DeviceOutput, bool>>(
            AccessTools.PropertyGetter(typeof(DeviceOutput), "IsOperable"));

        // A new Fast Vent starts at the default multiplier.
        public static void Configure(ActiveVent fastVent, ActiveVent activeVent)
        {
            BasePressurePerTick = PressurePerTick(activeVent);
            fastVent.MinSetting = MinMultiplier;
            fastVent.MaxSetting = MaxMultiplier;
            OutputSetting(fastVent) = DefaultMultiplier;

            var poweredVent = WorldManager.Instance.SourcePrefabs.OfType<PoweredVentMultiGrid>().FirstOrDefault();
            if (poweredVent != null) GridMixingDepth = Math.Min(PoweredVentDepth(poweredVent), MultiGridAtmospherics.MAX_DEPTH);
            Debug.Log($"[FastVent] Active Vent flow limit {BasePressurePerTick} kPa per tick; reaches {GridMixingDepth} cells"
                + (poweredVent != null ? $" (as {poweredVent.PrefabName})" : " (no multigrid Powered Vent found)"));
        }

        // The vanilla tick's own test for pumping at all.
        public static bool IsWorking(ActiveVent vent) => vent.OnOff && vent.Powered && IsOperable(vent);

        // After the vanilla tick has pumped the vent's own cell, the rest of this tick's flow limit goes to the cells
        // open to it, as PoweredVentMultiGrid.ExchangeWithWorld does it. The pipe side still keeps to the vent's
        // PressureInternal, which the Powered Vent doesn't have.
        public static void Scavenge(ActiveVent vent, PressurekPa cellPressureBefore)
        {
            var cell = vent.GetWorkingAtmosphere();
            var pipe = vent.ConnectedPipeNetwork?.Atmosphere;
            if (cell == null || pipe == null) return;
            var limit = new PressurekPa(PressurePerTick(vent));
            var left = limit - new PressurekPa(Math.Abs((cell.PressureGasses - cellPressureBefore).ToDouble()));
            if (left <= MultiGridAtmospherics.DISCARD_REMAINING_THRESHOLD) return;

            var target = vent.ExternalPressure;
            switch (vent.VentDirection)
            {
                case VentDirection.Inward:
                {
                    // As a pressure in this cell, how much more the pipe can take before it reaches PressureInternal.
                    var pipeRoom = AsCellPressure(vent.InternalPressure - pipe.PressureGasses, pipe, cell);
                    var allowed = RocketMath.Min(left, pipeRoom);
                    if (allowed <= MultiGridAtmospherics.DISCARD_REMAINING_THRESHOLD) break;
                    var required = RocketMath.Min(
                        MultiGridAtmospherics.InwardsPressureRequired(cell, allowed, GridMixingDepth, target), allowed);
                    var toMove = allowed;
                    if (cell.PressureGasses < required)
                        MultiGridAtmospherics.VentFromNeighborsToPipe(pipe, cell, ref toMove, GridMixingDepth, target);
                    else
                        toMove -= MultiGridAtmospherics.PumpGasToPipe(cell, pipe, required, target, force: true);
                    left -= allowed - toMove;
                    break;
                }
                case VentDirection.Outward:
                {
                    // As a pressure in this cell, how much gas the pipe has above PressureInternal.
                    var pipeGas = AsCellPressure(pipe.PressureGassesAndLiquids - vent.InternalPressure, pipe, cell);
                    var allowed = RocketMath.Min(left, pipeGas);
                    if (allowed <= MultiGridAtmospherics.DISCARD_REMAINING_THRESHOLD) break;
                    var toMove = MultiGridAtmospherics.OutwardsPressureRequired(cell, allowed, GridMixingDepth, target);
                    left -= MultiGridAtmospherics.PumpGasToWorld(cell, pipe, MixedTemperature(pipe, cell), toMove, target, force: true);
                    break;
                }
            }
            vent.FlowIndicatorStatus = FlowIndicator(vent.VentDirection, left, limit);
        }

        // A pressure difference in the pipe, as the pressure the same gas would make in the cell; never below 0.
        static PressurekPa AsCellPressure(PressurekPa pipePressure, Atmosphere pipe, Atmosphere cell)
        {
            if (pipePressure <= PressurekPa.Zero) return PressurekPa.Zero;
            return pipePressure * (pipe.Volume / cell.Volume).ToDouble();
        }

        static TemperatureKelvin MixedTemperature(Atmosphere a, Atmosphere b)
        {
            var moles = a.TotalMoles.ToDouble() + b.TotalMoles.ToDouble();
            if (moles <= 0) return b.Temperature;
            return new TemperatureKelvin((a.Temperature.ToDouble() * a.TotalMoles.ToDouble()
                + b.Temperature.ToDouble() * b.TotalMoles.ToDouble()) / moles);
        }

        static FlowIndicatorState FlowIndicator(VentDirection direction, PressurekPa left, PressurekPa limit)
        {
            if (left <= MultiGridAtmospherics.DISCARD_REMAINING_THRESHOLD + PressurekPa.One) return FlowIndicatorState.Max;
            var nearlyAll = left < limit / 2.0;
            return direction == VentDirection.Inward
                ? (nearlyAll ? FlowIndicatorState.InwardsLimited : FlowIndicatorState.InwardsVeryLimited)
                : (nearlyAll ? FlowIndicatorState.OutwardsLimited : FlowIndicatorState.OutwardsVeryLimited);
        }
    }

    [HarmonyPatch(typeof(ActiveVent), nameof(ActiveVent.OnAtmosphericTick))]
    static class FlowPatch
    {
        // Before each tick, a Fast Vent's flow limit follows its Setting, however it was set; and the pressure of
        // its cell is noted, to see how much of the limit the vanilla tick used.
        static void Prefix(ActiveVent __instance, out PressurekPa? __state)
        {
            __state = null;
            if (__instance.PrefabHash != FastVent.Hash) return;
            FastVent.PressurePerTick(__instance) = FastVent.BasePressurePerTick * __instance.OutputSetting;
            if (FastVent.IsWorking(__instance)) __state = __instance.GetWorkingAtmosphere()?.PressureGasses;
        }

        static void Postfix(ActiveVent __instance, PressurekPa? __state)
        {
            if (__state.HasValue && FastVent.IsWorking(__instance)) FastVent.Scavenge(__instance, __state.Value);
        }
    }

    // The hover tooltip's extended text shows the multiplier with the vent's pressures.
    [HarmonyPatch(typeof(ActiveVent), nameof(ActiveVent.GetExtendedText))]
    static class TooltipPatch
    {
        static void Postfix(ActiveVent __instance, StringBuilder __result)
        {
            if (__instance.PrefabHash == FastVent.Hash && __result != null)
                __result.AppendLine($"Multiplier: {__instance.OutputSetting:0.##}x");
        }
    }
}
