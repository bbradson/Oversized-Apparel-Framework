namespace OversizedApparel;
public class Extension : DefModExtension
{
	public Vector2 drawSize; // necessary because vanilla makes use of apparel <drawSize> for ground textures. Directly applying it without extension would result in many hats becoming slightly smaller.
}