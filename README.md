# JarSmartFill

Fixes the jar filling exploit for water in 7 Days to Die.

Made and tested for game version 3.0


<img width="800" height="450" alt="ezgif-5d93d3e5f7e34bbf" src="https://github.com/user-attachments/assets/d2ed8fc5-6cf3-46bf-945d-8a81c4651d51" />


## The problem

In vanilla the game lets you fill an entire stack of empty jars in a single click but only ever charges the water cost of one jar for the whole stack. This means if you are holding a full stack of 80 empty jars and you dip them into any water source you walk away with 80 full jars while the game only removes enough water for a single jar.

For big water sources like rivers or lakes this does not really matter since the amount of water there is practically endless anyway. But for small water sources like puddles or small ponds this turns into a way to generate large amounts of water for almost no cost at all. A puddle that should only give you a jar or two of water ends up being able to fill an entire stack over and over again.

## What this mod does

This mod keeps the exact same convenience that vanilla has. You still click once and the whole stack of jars gets filled in one go if there is enough water available. Nothing changes about how fast or how easy it is to fill jars.

What changes is how much water is actually taken from the world when you do this. Instead of always taking the cost of a single jar no matter how many jars you are filling the mod now takes into account how many jars you are actually filling and adjusts the water cost accordingly.

The mod also splits water sources into two categories.

Large water sources such as rivers and lakes are treated the same way vanilla treats them. Since there is effectively an unlimited amount of water in these locations the mod does not bother draining them at all. It simply lets you fill your jars like normal without doing any extra calculations or sending any extra network updates for water changes. This keeps performance the same as vanilla for these cases.

Small water sources such as puddles and small ponds are treated differently. Here the mod actually checks how much water is available nearby and calculates exactly how many jars can be filled from that amount of water. If you are holding more empty jars than the water source can support you will only get as many full jars as the water can actually provide. You will never end up with a jar that is only partially filled. It is either a proper full jar or nothing at all.

## How it works in more detail

When you use an empty jar item near water the mod first checks how much water is present in a fairly wide radius around the point you are collecting from. If this amount is large enough to be considered a big water source the mod treats it as effectively infinite. In this case your jars get filled just like in vanilla and no water is actually removed from the world.

If the water source does not qualify as big the mod switches to a more careful calculation. It figures out how much water is needed to fill all the empty jars you are currently holding and then checks how much of that water is actually available nearby. Based on this it determines how many jars can be completely filled. Only that exact amount of water is then removed from the world and only that many jars are handed back to you as full jars. Any jars that could not be filled remain empty in your inventory.

The radius the mod searches for water in also increases slightly as you try to fill more jars. This is because filling a large stack of jars from a small water source may require water from a slightly bigger area than filling just one or two jars would. This radius still has a maximum limit so it will never search an unreasonably large area even if you are holding a huge stack of jars.

## Included XML changes

The mod comes with a small config file that adjusts the empty jar item itself. It does two things.

First it lowers the delay on the jar filling action from the vanilla value down to half a second. This makes filling jars feel faster and more responsive since the whole point of this mod is to let you comfortably fill large stacks of jars at once and a long delay before the fill triggers makes that feel sluggish.

Second it makes sure the ReduceWater option is turned on for the jar filling action if it is not already enabled in the base game files. This option controls whether water is actually removed from the world when you fill jars. Without this option enabled the mod would have nothing to work with since there would be no water reduction happening in the first place for it to calculate properly. This part of the config only adds the option if it is missing so it will not conflict with other mods that may have already changed this value.

```xml
<append xpath="/items/item[@name='drinkJarEmpty']/property[@class='Action1'][property[@name='Class' and @value='CollectWater'] and not(property[@name='ReduceWater'])]">
    <property name="ReduceWater" value="true"/>
</append>
```

If you have another mod that adds a new item for collecting water and you want that item to work correctly with JarSmartFill you can use the exact same pattern in your own mod's config file. Just replace drinkJarEmpty with the name of your item. The xpath condition already makes sure the property is only added when it is not already present so there is no risk of doubling up if something else already set it.

```xml
<append xpath="/items/item[@name='yourItemNameHere']/property[@class='Action1'][property[@name='Class' and @value='CollectWater'] and not(property[@name='ReduceWater'])]">
    <property name="ReduceWater" value="true"/>
</append>
```

This is the only XML change needed on the item side. The mod will then handle that item correctly as long as its name is also registered inside the mod itself as described in the adding support for other items section below.

## Adding support for other items

By default this mod only handles the vanilla empty jar item called drinkJarEmpty. If you have another mod that adds a new container for collecting water and you want it to benefit from the same smart filling behavior you need to make sure that item is set up correctly in XML and that its item name is registered in the mod.

For the mod side open the main CS file and find the line that defines EmptyJarName near the top of the JarSmartFill class. By default it looks like this.

```csharp
internal const string EmptyJarName = "drinkJarEmpty";
```

The relevant check in the patch that uses this name looks like this.

```csharp
if (heldClass.Name != JarSmartFill.EmptyJarName)
    return true;
```

You could change this to also allow your custom item name alongside the vanilla one. Everything else in the mod will work the same way for any item that passes this check as long as the item is set up correctly in XML with ReduceWater enabled as shown above.

## Compatibility

This mod only affects items that are explicitly handled inside the patch. Anything else that uses the same water collecting action class will continue to work exactly like vanilla and will not be touched in any way.

This mod is built for game version 3.0. It relies on the internal structure of a few vanilla classes related to water collecting so it may not work correctly on older versions and may need updates if the relevant vanilla code changes in future game versions.

## Installation

Extract the mod folder into the Mods directory of your game installation. This follows the same installation process as any other standard 7 Days to Die mod. No additional steps or extra files are required.

## Notes

If the water reduction option is turned off for the water collecting action in the game files then no water will ever be removed from the world no matter what regardless of this mod being installed. This matches how vanilla behaves in that same situation and is fully intended. The included XML config already makes sure this option is turned on for the empty jar by default.

For multiplayer servers it is recommended to install this mod on both the server and all connecting clients to make sure everyone experiences the same consistent behavior.

## Requirements

7 Days to Die version 3.0 with mod support through Harmony. No additional downloads or setup is required since Harmony already comes bundled with the game.
