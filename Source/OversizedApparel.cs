global using System;
global using System.Collections.Generic;
global using System.Reflection;
global using System.Reflection.Emit;
global using HarmonyLib;
global using RimWorld;
global using UnityEngine;
global using Verse;
global using System.Linq;
using System.Runtime.CompilerServices;
using static System.Reflection.Emit.OpCodes;
using static OversizedApparel.TranspilerHelpers;

namespace OversizedApparel;
[StaticConstructorOnStartup]
public static class OversizedApparel
{
	static OversizedApparel()
	{
		var vanillaExpandedDisableCachingEnabled = false;
		if (Type.GetType("VFECore.VFEGlobal, VFECore")?.GetField("settings")?.GetValue(null) is { } vEFsettings)
			vanillaExpandedDisableCachingEnabled = (bool)Type.GetType("VFECore.VFEGlobalSettings, VFECore")?.GetField("disableCaching")?.GetValue(vEFsettings);

		if (!vanillaExpandedDisableCachingEnabled)
			Harmony.Patch(AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt)), transpiler: new(typeof(OversizedApparel).GetMethod(nameof(PawnRenderer_RenderPawnAt_Transpiler))));

		Harmony.Patch(AccessTools.FirstMethod(typeof(PawnRenderer), m => m.Name.Contains("DrawApparel") && m.HasAttribute<CompilerGeneratedAttribute>() && m.GetParameters().Any(p => p.ParameterType == typeof(ApparelGraphicRecord))),
			transpiler: new(typeof(OversizedApparel).GetMethod(nameof(PawnRenderer_DrawHeadHair_DrawApparel_Transpiler)), Priority.Last));

		Harmony.Patch(AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.DrawBodyApparel)), transpiler: new(typeof(OversizedApparel).GetMethod(nameof(PawnRenderer_DrawBodyApparel_Transpiler)), Priority.Last));

		//Combat Extended replaces the whole head apparel method, so here's another damn hat patch for their version
		if (Type.GetType("CombatExtended.HarmonyCE.Harmony_PawnRenderer, CombatExtended") is { } ceType)
			Harmony.Patch(AccessTools.FindIncludingInnerTypes(ceType, t => AccessTools.Method(t, "DrawHeadApparel")), transpiler: new(typeof(OversizedApparel).GetMethod(nameof(CombatExtended_PawnRenderer_DrawHeadHair_DrawApparel_Transpiler)), Priority.Last));
	}

	public static IEnumerable<CodeInstruction> PawnRenderer_RenderPawnAt_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		var codes = instructions.ToList();
		var flag = FirstLdloc(original, typeof(bool));

		for (var i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode == flag.OpCode && codes[i].operand == flag.Operand && codes[i + 1].operand is Label label)
			{
				yield return codes[i];
				yield return codes[i + 1];
				yield return new(Ldarg_0);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(ShouldSkipCache)));
				yield return new(Brtrue_S, label);
				i++;
			}
			else
			{
				yield return codes[i];
			}
		}
	}

	public static IEnumerable<CodeInstruction> PawnRenderer_DrawHeadHair_DrawApparel_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		var codes = instructions.ToList();
		var meshSuccess = false;
		var offsetSuccess = false;

		var compilerGenStructType = typeof(PawnRenderer).GetNestedTypes(AccessTools.all).Where(type => AccessTools.Field(type, "headFacing") is { } field
			&& field.FieldType == typeof(Rot4) && type.HasAttribute<CompilerGeneratedAttribute>() && type.IsValueType).First();

		var apparelRecord = FirstLdarg(original, typeof(ApparelGraphicRecord));
		var headFacing = AccessTools.Field(compilerGenStructType, "headFacing");
		// p.ParameterType == compilerGenStructType returns null, so I have to be a bit less specific here. Though it would work with null too, just ends up being unsafe with pointer
		var compilerGenArg = FirstLdarg(original, p => /*p.ParameterType == compilerGenStructType &&*/ p.ParameterType.IsByRef && p.Position != 0);
		var onHeadLoc = AccessTools.Field(compilerGenStructType, "onHeadLoc");

		for (var i = 0; i < codes.Count; i++)
		{
			if (codes[i].CallReturns(typeof(Mesh)) && codes[i + 1].IsStloc())
			{
				yield return codes[i];
				yield return new(compilerGenArg.OpCode, compilerGenArg.Operand);
				yield return new(Ldfld, headFacing);
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(ChangeMeshSize)));
				meshSuccess = true;
			}
			else if ((codes[i].CallReturns(typeof(Vector3)) && codes[i + 1].IsStloc()) || codes[i].LoadsField(onHeadLoc))
			{
				yield return codes[i];
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(compilerGenArg.OpCode, compilerGenArg.Operand);
				yield return new(Ldfld, headFacing);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(GetDrawOffset)));
				yield return new(Call, AccessTools.Method(typeof(Vector3), "op_Addition"));
				offsetSuccess = true;
			}
			else
			{
				yield return codes[i];
			}
		}

		if (!meshSuccess || !offsetSuccess)
			Log.Error("Oversized Apparel failed to patch PawnRenderer.DrawHeadHair. This is likely an incompatibility with another mod. Hugslib (opened by pressing F12) could reveal which other mod is patching this same method.");
	}

	public static IEnumerable<CodeInstruction> PawnRenderer_DrawBodyApparel_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		var codes = instructions.ToList();
		var meshSuccess = false;
		var offsetSuccess = false;

		var mesh = FirstLdarg(original, typeof(Mesh));
		var rot4 = FirstLdarg(original, typeof(Rot4));
		var shellLoc = FirstLdarg(original, p => p.ParameterType == typeof(Vector3) && p.Name == "shellLoc");
		var apparelRecord = GetLdloc(GetLocalOperands(codes, c => c.CallReturns(typeof(ApparelGraphicRecord))).First());

		for (var i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode == shellLoc.OpCode && codes[i].operand == shellLoc.Operand && codes[i + 1].IsStloc())
			{
				yield return codes[i];
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(rot4.OpCode, rot4.Operand);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(GetDrawOffset)));
				yield return new(Call, AccessTools.Method(typeof(Vector3), "op_Addition"));
				offsetSuccess = true;
			}
			else if (codes[i].opcode == mesh.OpCode && codes[i].operand == mesh.Operand)
			{
				yield return codes[i];
				yield return new(rot4.OpCode, rot4.Operand);
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(ChangeMeshSize)));
				meshSuccess = true; //should get inserted in multiple places, but only errors when all insertions fail anyway
			}
			else
			{
				yield return codes[i];
			}
		}

		if (!meshSuccess || !offsetSuccess)
			Log.Error("Oversized Apparel failed to patch PawnRenderer.DrawBodyApparel. This is likely an incompatibility with another mod. Hugslib (opened by pressing F12) could reveal which other mod is patching this same method.");
	}

	public static IEnumerable<CodeInstruction> CombatExtended_PawnRenderer_DrawHeadHair_DrawApparel_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		var codes = instructions.ToList();
		var meshSuccess = false;
		var offsetSuccess = false;

		var vector3VarIndices = GetLocalOperands(codes, c => c.Calls(AccessTools.Method(typeof(Vector3), "op_Addition"))).ToArray();

		var mesh = FirstLdloc(codes, typeof(Mesh));
		var headWearPos = GetLdloc(vector3VarIndices[0]);
		var maskLoc = GetLdloc(vector3VarIndices[1]);
		var apparelRecord = FirstLdloc(codes, typeof(ApparelGraphicRecord));
		var headFacing = FirstLdarg(original, p => p.ParameterType == typeof(Rot4) && p.Name == "headFacing");

		for (var i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode == mesh.OpCode && codes[i].operand == mesh.Operand)
			{
				yield return codes[i];
				yield return new(headFacing.OpCode, headFacing.Operand);
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(ChangeMeshSize)));
				meshSuccess = true;
			}
			else if ((codes[i].opcode == headWearPos.OpCode && codes[i].operand == headWearPos.Operand) || (codes[i].opcode == maskLoc.OpCode && codes[i].operand == maskLoc.Operand))
			{
				yield return codes[i];
				yield return new(apparelRecord.OpCode, apparelRecord.Operand);
				yield return new(headFacing.OpCode, headFacing.Operand);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(GetDrawOffset)));
				yield return new(Call, AccessTools.Method(typeof(Vector3), "op_Addition"));
				offsetSuccess = true;
			}
			else
			{
				yield return codes[i];
			}
		}

		if (!meshSuccess || !offsetSuccess)
			Log.Error("Oversized Apparel failed to patch Combat Extended's Harmony_PawnRenderer_DrawHeadHair.DrawHeadApparel. This is CE's fault.");
	}

	public static bool ShouldSkipCache(PawnRenderer instance)
	{
		var graphics = instance.graphics.apparelGraphics;
		for (var i = 0; i < graphics.Count; i++)
		{
			if (graphics[i].sourceApparel.def.GetModExtension<Extension>() is { } extension && extension.drawSize is var size && (size.x > 1 || size.y > 1))
				return true;
		}
		return false;
	}

	public static Vector3 GetDrawOffset(ApparelGraphicRecord apparelRecord, Rot4 rot) => apparelRecord.sourceApparel.def.graphicData.DrawOffsetForRot(rot); //taking this directly from graphic instead of the def doesn't work for some reason

	public static Mesh ChangeMeshSize(Mesh mesh, Rot4 rot4, ApparelGraphicRecord apparelRecord)
	{
		var extension = apparelRecord.sourceApparel.def.GetModExtension<Extension>();
		if (extension is null)
			return mesh;
		var size = extension.drawSize;
		if (size == default)
			return mesh;

		size.x *= mesh.vertices[2].x * 2f; //mesh.vertices[2].x = drawSize.x / 0.5f in new GraphicMeshSet(drawSize)
		size.y *= mesh.vertices[2].z * 2f; //mesh.vertices[2].z = drawSize.y / 0.5f in new GraphicMeshSet(drawSize)

		if (!OversizedPlanes.TryGetValue(size, out var value))
		{
			value = new(size.x, size.y);
			OversizedPlanes.Add(size, value);
		}
		return value.MeshAt(rot4);
	}

	public static Harmony Harmony { get; } = new("oversized.apparel");
	public static Dictionary<Vector2, GraphicMeshSet> OversizedPlanes { get; } = new();
}