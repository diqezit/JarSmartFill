using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/*
 JarSmartFill

 Goal
 -----
 Vanilla fill logic only ever costs one jar worth of water no matter
 how many empty jars are in the held stack then swaps the whole stack
 to filled items in one shot Topping off a stack of 80 jars from a
 puddle costs exactly the same water as topping off one
 This mod keeps the one click whole stack feel of vanilla but makes
 the water cost for small bodies of water scale with how many jars the
 player walks away with
 Large bodies of water river or lake are not drained jars still fill
 but world water is left untouched since costing something effectively
 bottomless is not worth the compute and network overhead

 Why a Prefix that mirrors vanilla trigger timing instead of a Postfix
 ----------------------------------------------------------------------
 Vanilla fires its fill once per click a click stamps lastUseTime with
 a real timestamp OnHoldingUpdate waits out IsActionRunning every
 frame and the instant the wait is over it resets lastUseTime to 0
 right before doing the actual work That reset is what stops the same
 click from refiring on the next frame
 This mod fully replaces that work returning false skips the vanilla
 body entirely so vanilla's own reset never runs The Prefix performs
 the same reset itself at the same point vanilla would or the fill
 would refire every frame instead of once per click
 The reset only happens once the held item is confirmed to be
 drinkJarEmpty doing it any earlier would consume the trigger for some
 other item that happens to share this action class say a modded
 bucket and leave that item's own fill silently dead on a legit click
 The two checks at the very top lastUseTime equals 0 and
 IsActionRunning mirror vanilla's own gate exactly so returning true
 there is a true no-op vanilla would have bailed at the same point

 Why one mass probe is reused for the big water test
 ----------------------------------------------------
 CollectWaterUtils.CollectWater only fills a WaterPoint list world
 water is mutated exclusively through NetPackageWaterSet so calling it
 without sending a package is a pure read The big water probe asks for
 BigWaterProbeBlocks worth of full block mass within
 BigWaterProbeRadius If satisfied the body of water is treated as
 bottomless the whole stack fills and no package is sent matching
 vanilla's one dip whole stack full behavior exactly

 Why jar capacity is read from targetMass instead of a constant
 ---------------------------------------------------------------
 Vanilla derives targetMass in ExecuteAction from the item's own
 MaxMass minus current Meta Reading it back off CollectWaterActionData
 stays correct on its own if a jar's capacity is ever retuned in XML
 rather than quietly drifting out of sync with a separately hardcoded
 guess A WaterValue built from targetMass failing HasMass guards both
 a zero capacity and the division by jarCapacityMass below

 Important whole jar note
 -------------------------
 jarsToFill is collectedMass divided by jarCapacityMass whole jars
 only A player gets a properly full jar or nothing never one quietly a
 few percent short because the pond ran dry partway through the stack
 When the exact mass differs from the first collection a second
 CollectWater call is made for exactMass so waterPoints line up
 exactly with the jars actually handed out otherwise the world would
 lose more water than the jars account for or a jar would be handed
 out that is not backed by any water actually removed
 If the pond has less than one jar of water left and cannot fill a
 whole jar the remainder is drained from the world anyway and the
 player gets no jar This prevents a tiny remainder from becoming an
 untouchable block that never depletes The points collected by the
 first CollectWater call already represent everything found in range
 since the request exceeded what was available so no second call is
 needed to drain that remainder

 Important radius note
 ----------------------
 Vanilla never looks past radius 2 because it only ever needs one jar
 Once cost scales with stack size filling a big stack can need far
 more water than a small radius realistically contains nearby
 GetCollectRadius grows the radius by one per JarsPerRadiusStep jars
 and caps it at MaxCollectRadius so one click on a huge stack cannot
 trigger an unbounded search MaxCollectRadius matches
 BigWaterProbeRadius so a body already confirmed small can never yield
 more than BigWaterProbeBlocks worth of mass through this path

 Important water zeroing note
 -----------------------------
 A cell is never zeroed by a manual threshold A WaterValue is built
 from finalMass and if HasMass is false it is replaced with
 WaterValue.Empty so empty means exactly what the engine thinks it
 means
 If ItemActionCollectWater has ReduceWater false in the XML
 isReduceWater is false and water is never reduced vanilla behavior
 and a common reason water does not drain

 Important hand out note
 ------------------------
 Items are handed out through vanilla paths no manual stack splitting
 Inventory.DecHoldingItem shrinks the empty jar stack in hand The
 amount placed back into the original slot is capped at the filled
 item's own Stacknumber first SetItem and AddItemAtSlot will happily
 write an oversized stack into an empty slot without complaint nothing
 else enforces that limit
 The hand stack size is computed before the inventory is touched so a
 failure here cannot leave empty jars already removed with nothing
 handed back in return
 XUiM_PlayerInventory.AddItem stacks and splits by Stacknumber itself
 and anything that does not fit is dropped via DropItem
 Without a player UI items are dropped through
 GameManager.ItemDropServer at the player's own drop position

 What the mod does in order
 ---------------------------
 1 Patch ItemActionCollectWater.OnHoldingUpdate
   Harmony Prefix Acts only when the held item is drinkJarEmpty
   anything else keeps vanilla behavior untouched
 2 Big water probe
   One CollectWater call for BigWaterProbeBlocks of full block mass in
   BigWaterProbeRadius If satisfied fill the whole stack send nothing
 3 Small water collection
   wantedMass equals emptyCount times jarCapacityMass collect once
   compute whole jarsToFill recollect for the exact mass when it
   differs then apply changes strictly the vanilla way
   NetPackageWaterSet Reset AddChange GameManager.Instance.SetWaterRPC
   If jarsToFill comes out to zero the water already collected in the
   attempt is still drained from the world so a sub jar remainder does
   not become a permanent untouchable puddle no jar is handed out
 4 Hand out filled jars
   DecHoldingItem then AddItemToPreferredToolbeltSlot into the
   original slotIdx then AddItem for the rest then DropItem for
   whatever does not fit

 Integration points for future migration
 -----------------------------------------
 IModApi.InitMod(Mod)
 Harmony(string).PatchAll()
 ItemActionCollectWater.OnHoldingUpdate(ItemActionData)
 ItemActionCollectWater.IsActionRunning(ItemActionData)
 ItemActionCollectWater.changeItemToItem (PublicizedFrom private string)
 ItemActionCollectWater.isReduceWater (PublicizedFrom private bool)
 ItemActionCollectWater.waterPoints (PublicizedFrom private List<WaterPoint>)
 ItemActionCollectWater.CollectWaterActionData.targetPosition/targetMass (PublicizedFrom private)
 ItemActionData.lastUseTime/invData
 ItemInventoryData.holdingEntity/itemValue/slotIdx
 CollectWaterUtils.CollectWater(ChunkCluster, int, Vector3i, int, List<WaterPoint>)
 CollectWaterUtils.WaterPoint.worldPos/massToTake/finalMass
 WaterValue(int) WaterValue.Full/Empty/HasMass()/GetMass()
 NetPackageManager.GetPackage<NetPackageWaterSet>()
 NetPackageWaterSet.Reset()/AddChange(Vector3i, WaterValue)/HasChanges
 GameManager.Instance.SetWaterRPC(NetPackageWaterSet)
 GameManager.Instance.World.ChunkCache
 GameManager.ItemDropServer(ItemStack, Vector3, Vector3, int, float, bool)
 Inventory.holdingCount/DecHoldingItem(int)/SetItem(int, ItemStack)
 EntityPlayerLocal.GetDropPosition()/entityId
 LocalPlayerUI.GetUIForPlayer(EntityPlayerLocal) then xui.PlayerInventory
 XUiM_PlayerInventory.AddItem(ItemStack, bool)/DropItem(ItemStack)/AddItemToPreferredToolbeltSlot(ItemStack, int)
 ItemClass.GetItem(string, bool)/GetItemName()/Stacknumber
 ItemValue.Clone()/IsEmpty()/Meta/ItemClass
*/

public class JarSmartFill : IModApi
{
    public void InitMod(Mod modInstance)
    {
        new Harmony(Config.HarmonyId).PatchAll();
        Log.Out("[JarSmartFill] Loaded");
    }
}

internal static class Config
{
    internal const string HarmonyId = "diqezit.jarsmartfill";
    internal const string EmptyJarName = "drinkJarEmpty";

    internal const int BigWaterProbeBlocks = 64;
    internal const int BigWaterProbeRadius = 10;

    internal const int BaseCollectRadius = 2;
    internal const int MaxCollectRadius = 10;
    internal const int JarsPerRadiusStep = 20;

    internal const int MinStackLimit = 1;
}

[HarmonyPatch(typeof(ItemActionCollectWater), nameof(ItemActionCollectWater.OnHoldingUpdate))]
public static class JarSmartFillPatch_OnHoldingUpdate
{
    private static readonly int BigWaterProbeMass =
        Config.BigWaterProbeBlocks * WaterValue.Full.GetMass();

    private static bool Prefix(ItemActionCollectWater __instance, ItemActionData _actionData)
    {
        if (_actionData == null || _actionData.lastUseTime == 0f)
            return true;

        if (__instance.IsActionRunning(_actionData))
            return true;

        float clickTime = _actionData.lastUseTime;

        try
        {
            return RunFill(__instance, _actionData);
        }
        catch (Exception ex)
        {
            _actionData.lastUseTime = clickTime;
            Log.Error("[JarSmartFill] " + ex);
            return true;
        }
    }

    private static bool RunFill(ItemActionCollectWater action, ItemActionData actionData)
    {
        ItemInventoryData invData = actionData.invData;
        if (invData == null)
            return true;

        EntityPlayerLocal player = invData.holdingEntity as EntityPlayerLocal;
        if (player == null)
            return true;

        ItemClass heldClass = invData.itemValue?.ItemClass;
        if (heldClass == null || heldClass.GetItemName() != Config.EmptyJarName)
            return true;

        if (!(actionData is ItemActionCollectWater.CollectWaterActionData data))
            return true;

        actionData.lastUseTime = 0f;

        string filledName = action.changeItemToItem;
        if (string.IsNullOrEmpty(filledName))
            return false;

        ChunkCluster cc = GameManager.Instance.World?.ChunkCache;
        if (cc == null)
            return false;

        int emptyCount = player.inventory.holdingCount;
        if (emptyCount <= 0)
            return false;

        int jarCapacityMass = data.targetMass;
        if (!new WaterValue(jarCapacityMass).HasMass())
            return false;

        ItemValue filledValue = ItemClass.GetItem(filledName);
        if (filledValue.IsEmpty())
            return false;

        List<CollectWaterUtils.WaterPoint> points = action.waterPoints;
        points.Clear();

        try
        {
            if (IsBigWater(cc, data.targetPosition, points))
            {
                int filledMeta = invData.itemValue.Meta + jarCapacityMass;
                GiveFilledJars(player, invData, filledValue, filledMeta, emptyCount, emptyCount);
                return false;
            }

            int jarsToFill = CollectForJars(
                cc,
                data.targetPosition,
                emptyCount,
                jarCapacityMass,
                points,
                invData.itemValue.Meta,
                out int smallFilledMeta);

            if (action.isReduceWater)
                ApplyWaterChanges(points);

            if (jarsToFill <= 0)
                return false;

            GiveFilledJars(player, invData, filledValue, smallFilledMeta, emptyCount, jarsToFill);
            return false;
        }
        finally
        {
            points.Clear();
        }
    }

    private static bool IsBigWater(
        ChunkCluster cc,
        Vector3i origin,
        List<CollectWaterUtils.WaterPoint> points)
    {
        int probedMass = CollectWaterUtils.CollectWater(
            cc, BigWaterProbeMass, origin, Config.BigWaterProbeRadius, points);

        points.Clear();
        return probedMass >= BigWaterProbeMass;
    }

    private static int CollectForJars(
        ChunkCluster cc,
        Vector3i origin,
        int emptyCount,
        int jarCapacityMass,
        List<CollectWaterUtils.WaterPoint> points,
        int baseMeta,
        out int filledMeta)
    {
        int radius = GetCollectRadius(emptyCount);
        int wantedMass = emptyCount * jarCapacityMass;

        int collectedMass = CollectWaterUtils.CollectWater(cc, wantedMass, origin, radius, points);

        int jarsToFill = Mathf.Min(collectedMass / jarCapacityMass, emptyCount);
        if (jarsToFill <= 0)
        {
            filledMeta = 0;
            return 0;
        }

        int exactMass = jarsToFill * jarCapacityMass;
        if (exactMass != collectedMass)
        {
            points.Clear();
            CollectWaterUtils.CollectWater(cc, exactMass, origin, radius, points);
        }

        filledMeta = baseMeta + jarCapacityMass;
        return jarsToFill;
    }

    private static int GetCollectRadius(int emptyCount)
    {
        int extraSteps = (emptyCount - 1) / Config.JarsPerRadiusStep;

        return Mathf.Clamp(
            Config.BaseCollectRadius + extraSteps,
            Config.BaseCollectRadius,
            Config.MaxCollectRadius);
    }

    private static void ApplyWaterChanges(List<CollectWaterUtils.WaterPoint> points)
    {
        if (points.Count == 0)
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

    private static void GiveFilledJars(
        EntityPlayerLocal player,
        ItemInventoryData invData,
        ItemValue filledValue,
        int filledMeta,
        int emptyCount,
        int jarsToFill)
    {
        ItemValue filled = filledValue.Clone();
        filled.Meta = filledMeta;

        if (jarsToFill < emptyCount)
        {
            player.inventory.DecHoldingItem(jarsToFill);
            AddOrDrop(player, filled, jarsToFill);
            return;
        }

        int inHand = Mathf.Min(jarsToFill, GetStackLimit(filled));

        player.inventory.DecHoldingItem(emptyCount);

        XUiM_PlayerInventory uiInventory = GetUiInventory(player);

        if (uiInventory == null)
        {
            player.inventory.SetItem(invData.slotIdx, new ItemStack(filled.Clone(), inHand));
            AddOrDrop(player, filled, jarsToFill - inHand);
            return;
        }

        bool placed = uiInventory.AddItemToPreferredToolbeltSlot(
            new ItemStack(filled.Clone(), inHand), invData.slotIdx);

        if (!placed)
            AddOrDrop(player, filled, inHand);

        AddOrDrop(player, filled, jarsToFill - inHand);
    }

    private static int GetStackLimit(ItemValue itemValue)
    {
        DataItem<int> stackNumber = itemValue.ItemClass?.Stacknumber;
        if (stackNumber == null)
            return Config.MinStackLimit;

        return Mathf.Max(Config.MinStackLimit, stackNumber.Value);
    }

    private static XUiM_PlayerInventory GetUiInventory(EntityPlayerLocal player)
    {
        return LocalPlayerUI.GetUIForPlayer(player)?.xui?.PlayerInventory;
    }

    private static void AddOrDrop(EntityPlayerLocal player, ItemValue filled, int count)
    {
        if (count <= 0)
            return;

        ItemStack stack = new ItemStack(filled.Clone(), count);
        XUiM_PlayerInventory uiInventory = GetUiInventory(player);

        if (uiInventory == null)
        {
            GameManager.Instance.ItemDropServer(
                stack, player.GetDropPosition(), Vector3.zero, player.entityId);
            return;
        }

        uiInventory.AddItem(stack, _playCollectSound: false);

        if (stack.count > 0)
            uiInventory.DropItem(stack);
    }
}