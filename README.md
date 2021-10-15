# Oversized Apparel Framework

![](About/Preview.png?raw=true)
 Enables custom apparel sizes and offsets in Rimworld.  
Vanilla Rimworld lacks this functionality. Preview shows an example of what's possible.  
  
Compatibility and Performance:  
Very good.  
Confirmed to be compatible with Enable Oversized Weapons and Humanoid Alien Races.  
  
Usage (for modders, not users):  
  
To change the size add a modExtension like this:  
<Def>  
	<modExtensions>  
		<li Class="OversizedApparel.Extension">  
			<drawSize>(x, y)</drawSize>  
		</li>  
	</modExtensions>  
</Def>  
  
x is a multiplier for the width, y applies on the height.  
<drawSize>#<drawSize> without brackets and a single number results in both dimensions changing by the same amount.  
<drawSize>1.0</drawSize> would be the default.  
  
To adjust offsets one or more of these ones to Def/graphicData:  
<drawOffset>(x,z,y)</drawOffset>  
<drawOffsetNorth>(x,z,y)</drawOffsetNorth>  
<drawOffsetEast>(x,z,y)</drawOffsetEast>  
<drawOffsetSouth>(x,z,y)</drawOffsetSouth>  
<drawOffsetWest>(x,z,y)</drawOffsetWest>  
  
x is the horizontal offset here, y is vertical, z is the distance to the camera, used for layers. An extension like for drawSize is not necessary here.  
  
Credits:  
Armor and pauldrons are expanded versions of Oskar Potocki's heavy plate armor and plate shoulderpads.  
Top hat is from Royalty.  
This mod uses Harmony.  
  
No assets are included in this mod. This is just a framework.  
  
License:  
MIT  
  
Github:  
https://github.com/bbradson/Oversized-Apparel-Framework  