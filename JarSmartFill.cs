using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/*
 JarSmartFill

Vanilla own fill logic only ever looks at ONE jar worth of capacity -
whatever the currently held item still needs to be full - and then as
long as it finds any usable water nearby swaps the held stack
over to the filled item in one shot all sharing that same single-jar fill amount

Topping off a stack of 80 empty jars from a puddle costs exactly the same water as topping off one

That harmless for a river or lake where the difference is never going to be visible either way
but a small pond turns into a way to mass-produce full jars for next to nothing

This mod keeps the one click whole stack feel of vanilla
but makes the water cost for small bodies of water actually scale with how many jars you walking away with

 Goal
 - Small bodies of water can be drained, but the whole stack of jars still
   fills in one go, same as vanilla

 - Large bodies of water (river/lake) are NOT drained - jars still fill,
   but the world water is left untouched, since computing and
   networking an exact cost against something effectively bottomless isn't worth the overhead

 - Just one simple vanilla mass-probe via
   CollectWaterUtils.CollectWater, reused for both the big-water test and
   the actual small-water collection

 ----------------------------------------------------------------------------
 What the does in order

1) Patch ItemActionCollectWater.OnHoldingUpdate 
Only act when the held item is drinkJarEmpty

Anything else - say, a modded bucket that happens to reuse this same action class 
is left completely alone and keeps behaving exactly like vanilla
since this mod has no opinion about items it doesn recognize

2) Big water check (simple)

got = CollectWaterUtils.CollectWater(cc, BigWaterProbeMass, origin, BigWaterProbeRadius, waterPoints)

- BigWaterProbeMass = 64 * WaterValue.Full.GetMass()
- BigWaterProbeRadius = 10

If got >= BigWaterProbeMass, the body of water is considered big:
- water is NOT reduced (no NetPackageWaterSet is sent),
- the player simply gets the whole stack of jars filled, matching
vanilla existing one dip, whole stack full behavior exactly

 3) If the body of water is small
    - Mass per jar is read straight off the action's own targetMass
      (jarCapacityMass) instead of assuming a fixed fraction of a full
      water block. That's the same number vanilla itself derives from the
      item's MaxMass, so it stays correct on its own if a jar's capacity
      is ever retuned in the XML, rather than quietly drifting out of
      sync with a separately hardcoded guess.

    - wantedMass = emptyCount * jarCapacityMass
    - collectedMass = CollectWaterUtils.CollectWater(... wantedMass ...)
    - jarsToFill = collectedMass / jarCapacityMass (whole jars only - a
      player gets a properly full jar or nothing, never one that's quietly
      a few percent short because the pond ran dry partway through the
      stack)
    - exactMass = jarsToFill * jarCapacityMass
    - a second CollectWater call is made for exactMass, so waterPoints
      line up exactly with the jars actually handed out - otherwise we'd
      either drain more water than the jars we gave account for, or hand
      out a jar that isn't backed by any water actually removed from the
      world

    The collection radius grows with how many jars are being filled
    (GetCollectRadius, capped at MaxCollectRadius so one click on a huge
    stack can't trigger an unbounded search). Vanilla never needs to look
    past a radius of 2 because it only ever needs one jar's worth; once
    the cost scales with stack size, filling a big stack can need far
    more water than a small radius could realistically contain nearby.

    If ReduceWater is enabled on ItemActionCollectWater (isReduceWater == true),
    water changes are applied strictly the vanilla way:
      NetPackageWaterSet.Reset()
      NetPackageWaterSet.AddChange(worldPos, WaterValue)
      GameManager.Instance.SetWaterRPC(pkg)

    To "zero out" water in a cell we do NOT use a manual threshold.
    Instead we do this:
      v = new WaterValue(finalMass)
      if !v.HasMass() => v = WaterValue.Empty

 4) Handing out items (vanilla, no manual stack-splitting)
    - Inventory.DecHoldingItem(...) shrinks the empty-jar stack in hand
    - The amount placed back into the original slot is capped at the
      filled item's own Stacknumber first: SetItem/AddItemAtSlot will
      happily write an oversized stack into an empty slot without
      complaint, so nothing else enforces that limit for us
    - XUiM_PlayerInventory.AddItemToPreferredToolbeltSlot(...) tries to put the filled jars
      back into the same slotIdx
    - XUiM_PlayerInventory.AddItem(...) adds the rest (it stacks and splits by Stacknumber itself)
    - if it does not fit - XUiM_PlayerInventory.DropItem(...)

 ----------------------------------------------------------------------------
 Why the Prefix mirrors vanilla's own trigger timing instead of just
 tweaking the result afterwards

 - Vanilla only fires its fill logic once per click: a click stamps
   lastUseTime with a real timestamp, OnHoldingUpdate then waits out
   IsActionRunning's delay window every frame, and the instant that wait
   is over it resets lastUseTime back to 0 right before doing the actual
   work. That reset is what stops the same click from firing again on the
   next frame - without it, vanilla's fill would refire forever.
 - Since this mod fully replaces that "actual work" (returning false
   skips vanilla's method body entirely, so vanilla's own reset never
   runs), the Prefix has to perform that same reset itself, at the same
   point vanilla would, or our fill would refire every frame instead of
   once per click.
 - That reset only happens once we've confirmed the held item is really
   ours (drinkJarEmpty) - doing it any earlier would consume the trigger
   for some other item that happens to share this action class, even
   though we never actually acted on its behalf, leaving that item's own
   fill silently doing nothing on a legitimate click.
 - The two checks at the very top (lastUseTime == 0f, IsActionRunning)
   mirror vanilla's own gate exactly, so returning true there is a true
   no-op - vanilla's OnHoldingUpdate would have bailed at the same point
   anyway.

 ----------------------------------------------------------------------------
 Important (a common reason "water does not drain")
 - If ItemActionCollectWater has ReduceWater="false" in the XML,
   __instance.isReduceWater will be false and water will never be reduced (vanilla behavior).

 ----------------------------------------------------------------------------
 Integration points (for future migration)
 ItemActionCollectWater.OnHoldingUpdate
 ItemActionCollectWater.CollectWaterActionData targetPosition targetMass
 CollectWaterUtils.CollectWater
 NetPackageWaterSet.Reset / HasChanges / AddChange(Vector3i, WaterValue)
 GameManager.Instance.SetWaterRPC(NetPackageWaterSet)
 Inventory.DecHoldingItem
 XUiM_PlayerInventory.AddItem / DropItem / AddItemToPreferredToolbeltSlot
 WaterValue.Full / Empty / HasMass / GetMass
*/

public class JarSmartFill : IModApi
{
    internal const string HarmonyId = "diqezit.jarsmartfill";
    internal const string EmptyJarName = "drinkJarEmpty";

    internal static readonly int FullMass = WaterValue.Full.GetMass();

    internal static readonly int BigWaterProbeMass = 64 * FullMass;
    internal const int BigWaterProbeRadius = 10;

    internal const int BaseCollectRadius = 2;
    internal const int MaxCollectRadius = 10;
    internal const int JarsPerRadiusStep = 20;

    public void InitMod(Mod modInstance)
    {
        new Harmony(HarmonyId).PatchAll();
    }

    internal static int GetCollectRadius(int holdingCount)
    {
        if (holdingCount <= 0)
            return BaseCollectRadius;

        int extraSteps = (holdingCount - 1) / JarsPerRadiusStep;
        int radius = BaseCollectRadius + extraSteps;

        return Mathf.Clamp(radius, BaseCollectRadius, MaxCollectRadius);
    }
}

[HarmonyPatch(typeof(ItemActionCollectWater), nameof(ItemActionCollectWater.OnHoldingUpdate))]
public static class JarSmartFillPatch_OnHoldingUpdate
{
    private static bool Prefix(ItemActionCollectWater __instance, ItemActionData _actionData)
    {
        if (_actionData == null)
            return true;

        if (_actionData.lastUseTime == 0f)
            return true;

        if (__instance.IsActionRunning(_actionData))
            return true;

        float oldLastUseTime = _actionData.lastUseTime;

        try
        {
            ItemInventoryData invData = _actionData.invData;
            if (invData == null)
                return true;

            EntityPlayerLocal player = invData.holdingEntity as EntityPlayerLocal;
            if (player == null)
                return true;

            ItemClass heldClass = invData.itemValue?.ItemClass;
            if (heldClass == null || heldClass.Name != JarSmartFill.EmptyJarName)
                return true;

            _actionData.lastUseTime = 0f;

            string filledName = __instance.changeItemToItem;
            if (string.IsNullOrEmpty(filledName))
                return false;

            ChunkCluster cc = GameManager.Instance.World?.ChunkCache;
            if (cc == null)
                return true;

            var data = (ItemActionCollectWater.CollectWaterActionData)_actionData;

            int emptyCount = player.inventory.holdingCount;
            if (emptyCount <= 0)
                return false;

            int jarCapacityMass = data.targetMass;
            if (!new WaterValue(jarCapacityMass).HasMass())
                return false;

            List<CollectWaterUtils.WaterPoint> points = __instance.waterPoints;
            points.Clear();

            try
            {
                if (IsBigWater(__instance, cc, data.targetPosition))
                {
                    ItemValue filledAll = ItemClass.GetItem(filledName);

                    GiveFilledJars(
                        player,
                        invData,
                        filledAll,
                        emptyCount,
                        emptyCount,
                        invData.itemValue.Meta + jarCapacityMass
                    );

                    return false;
                }

                int radius = JarSmartFill.GetCollectRadius(emptyCount);
                int wantedMass = emptyCount * jarCapacityMass;

                int collectedMass = CollectWaterUtils.CollectWater(
                    cc,
                    wantedMass,
                    data.targetPosition,
                    radius,
                    points
                );

                int jarsToFill = collectedMass / jarCapacityMass;

                if (jarsToFill <= 0)
                    return false;

                jarsToFill = Mathf.Min(jarsToFill, emptyCount);

                int exactMass = jarsToFill * jarCapacityMass;

                if (exactMass != collectedMass)
                {
                    points.Clear();

                    CollectWaterUtils.CollectWater(
                        cc,
                        exactMass,
                        data.targetPosition,
                        radius,
                        points
                    );
                }

                if (__instance.isReduceWater)
                    ApplyWaterChangesVanilla(points);

                ItemValue filledValue = ItemClass.GetItem(filledName);

                GiveFilledJars(
                    player,
                    invData,
                    filledValue,
                    emptyCount,
                    jarsToFill,
                    invData.itemValue.Meta + jarCapacityMass
                );

                return false;
            }
            finally
            {
                points.Clear();
            }
        }
        catch (Exception ex)
        {
            _actionData.lastUseTime = oldLastUseTime;
            Log.Error($"[JarSmartFill] {ex}");
            return true;
        }
    }

    private static bool IsBigWater(ItemActionCollectWater action, ChunkCluster cc, Vector3i origin)
    {
        List<CollectWaterUtils.WaterPoint> points = action.waterPoints;
        points.Clear();

        int got = CollectWaterUtils.CollectWater(
            cc,
            JarSmartFill.BigWaterProbeMass,
            origin,
            JarSmartFill.BigWaterProbeRadius,
            points
        );

        points.Clear();
        return got >= JarSmartFill.BigWaterProbeMass;
    }

    private static void ApplyWaterChangesVanilla(List<CollectWaterUtils.WaterPoint> points)
    {
        if (points == null || points.Count == 0)
            return;

        NetPackageWaterSet pkg = NetPackageManager.GetPackage<NetPackageWaterSet>();
        pkg.Reset();

        for (int i = 0; i < points.Count; i++)
        {
            CollectWaterUtils.WaterPoint wp = points[i];
            if (wp.massToTake <= 0)
                continue;

            WaterValue v = new WaterValue(wp.finalMass);
            if (!v.HasMass())
                v = WaterValue.Empty;

            pkg.AddChange(wp.worldPos, v);
        }

        if (pkg.HasChanges)
            GameManager.Instance.SetWaterRPC(pkg);
    }

    private static XUiM_PlayerInventory TryGetUiInv(EntityPlayerLocal player)
    {
        return LocalPlayerUI.GetUIForPlayer(player)
            ?.xui
            ?.PlayerInventory;
    }

    private static void AddOrDropVanilla(EntityPlayerLocal player, ItemValue itemValue, int count)
    {
        if (count <= 0)
            return;

        XUiM_PlayerInventory inv = TryGetUiInv(player);

        if (inv == null)
        {
            ItemStack stack = new ItemStack(itemValue.Clone(), count);
            Vector3 dropPos = player.position;
            dropPos.y += 0.5f;
            GameManager.Instance.ItemDropServer(stack, dropPos, Vector3.zero, player.entityId);
            return;
        }

        ItemStack s = new ItemStack(itemValue.Clone(), count);

        inv.AddItem(s, false);

        if (s.count > 0)
            inv.DropItem(s);
    }

    private static void GiveFilledJars(
        EntityPlayerLocal player,
        ItemInventoryData invData,
        ItemValue filledValue,
        int emptyCount,
        int jarsToFill,
        int filledMeta)
    {
        ItemValue filledWithMeta = filledValue.Clone();
        filledWithMeta.Meta = filledMeta;

        int emptyLeft = emptyCount - jarsToFill;

        if (emptyLeft > 0)
        {
            player.inventory.DecHoldingItem(jarsToFill);
            AddOrDropVanilla(player, filledWithMeta, jarsToFill);
            return;
        }

        // Everything currently held is being filled
        //
        // work out the hand-stack size BEFORE touching the inventory, so a failure here cannot leave the empty
        // jars already removed with nothing handed back in return
        //
        int stackLimit = 1;
        if (filledWithMeta.ItemClass != null && filledWithMeta.ItemClass.Stacknumber != null)
            stackLimit = Mathf.Max(1, filledWithMeta.ItemClass.Stacknumber.Value);

        int inHand = Mathf.Min(jarsToFill, stackLimit);

        player.inventory.DecHoldingItem(emptyCount);

        XUiM_PlayerInventory inv = TryGetUiInv(player);

        if (inv != null)
        {
            bool placed = inv.AddItemToPreferredToolbeltSlot(
                new ItemStack(filledWithMeta.Clone(), inHand),
                invData.slotIdx
            );

            if (!placed)
                AddOrDropVanilla(player, filledWithMeta, inHand);

            AddOrDropVanilla(player, filledWithMeta, jarsToFill - inHand);
            return;
        }

        player.inventory.SetItem(invData.slotIdx, new ItemStack(filledWithMeta.Clone(), inHand));
        AddOrDropVanilla(player, filledWithMeta, jarsToFill - inHand);
    }
}