// Copyright (c) 2022 bradson
// This Source Code Form is subject to the terms of the MIT license.
// If a copy of the license was not distributed with this file,
// You can obtain one at https://opensource.org/licenses/MIT/.

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Reflection;
global using System.Reflection.Emit;
global using HarmonyLib;
global using RimWorld;
global using UnityEngine;
global using Verse;
global using static FisheryLib.Aliases;
using System.Runtime.CompilerServices;
using FisheryLib;
using Log = Verse.Log;
using CodeInstructions = System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>;
using System.Globalization;

namespace OversizedApparel;

[StaticConstructorOnStartup]
public static class OversizedApparel
{
	static OversizedApparel()
	{
		var vanillaExpandedDisableCachingEnabled = false;
		if (Type.GetType("VFECore.VFEGlobal, VFECore")?.GetField("settings")?.GetValue(null) is { } vEFsettings)
			vanillaExpandedDisableCachingEnabled = (bool)Type.GetType("VFECore.VFEGlobalSettings, VFECore")?.GetField("disableCaching")?.GetValue(vEFsettings)!;

		if (!vanillaExpandedDisableCachingEnabled)
		{
			Harmony.Patch(
				AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt)),
				transpiler: new(methodof(PawnRenderer_RenderPawnAt_Transpiler)));
		}

		Harmony.Patch(
			AccessTools.FindIncludingInnerTypes(typeof(PawnRenderer), type
				=> AccessTools.FirstMethod(type, m
					=> m.Name.Contains("DrawHeadHair")
					&& m.Name.Contains("DrawApparel")
					//&& m.HasAttribute<CompilerGeneratedAttribute>()
					&& m.GetParameters().Any(p => p.ParameterType == typeof(ApparelGraphicRecord)))),
			transpiler: new(methodof(PawnRenderer_DrawHeadHair_DrawApparel_Transpiler), Priority.Last));

		Harmony.Patch(AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.DrawBodyApparel)), transpiler: new(methodof(PawnRenderer_DrawBodyApparel_Transpiler), Priority.Last));

		//Combat Extended replaces the whole head apparel method, so here's another hat patch for their version
		if (Type.GetType("CombatExtended.HarmonyCE.Harmony_PawnRenderer, CombatExtended") is { } ceType)
		{
			Harmony.Patch(
				AccessTools.FindIncludingInnerTypes(ceType, t => AccessTools.Method(t, "DrawHeadApparel")),
				transpiler: new(methodof(CombatExtended_PawnRenderer_DrawHeadHair_DrawApparel_Transpiler), Priority.Last, debug: true));
		}
	}

	public static CodeInstructions PawnRenderer_RenderPawnAt_Transpiler(CodeInstructions instructions, MethodBase original)
	{
		var flag = FishTranspiler.FirstLocalVariable(original, typeof(bool));

		return instructions
			.ReplaceAt((codes, i)
				=> i - 1 >= 0
				&& CompareInstructions(codes[i - 1], flag)
				&& codes[i].operand is Label,
			code => new[]
			{
				code,
				FishTranspiler.This,
				FishTranspiler.Call(ShouldSkipCache),
				FishTranspiler.IfTrue_Short((Label)code.operand)
			},
			false);
	}

	public static CodeInstructions PawnRenderer_DrawHeadHair_DrawApparel_Transpiler(CodeInstructions instructions, MethodBase original)
	{
		var compilerGenStructType
			= typeof(PawnRenderer).GetNestedTypes(AccessTools.all).Where(type
				=> AccessTools.Field(type, "headFacing") is { } headFacingField
				&& headFacingField.FieldType == typeof(Rot4)
				&& AccessTools.Field(type, "onHeadLoc") is { } onHeadLocField
				&& onHeadLocField.FieldType == typeof(Vector3)
				&& type.HasAttribute<CompilerGeneratedAttribute>())
			.First();

		var apparelRecord = FishTranspiler.FirstArgument(original, typeof(ApparelGraphicRecord));

		var headFacing = FishTranspiler.Field(compilerGenStructType, "headFacing");

		var compilerGenArg
			= FishTranspiler.FirstArgument(original,
			 compilerGenStructType.IsValueType ? compilerGenStructType.MakeByRefType() : compilerGenStructType);

		var onHeadLoc = FishTranspiler.Field(compilerGenStructType, "onHeadLoc");

		try
		{
			return instructions
				.ReplaceAt((codes, i)
					=> codes[i].CallReturns(typeof(Mesh))
					&& codes[i + 1].IsStloc(),
				code => new[]
					{
					code,
					compilerGenArg,
					headFacing,
					apparelRecord,
					FishTranspiler.Call(ChangeMeshSize)
					})
				.ReplaceAt((codes, i)
					=> (codes[i].CallReturns(typeof(Vector3))
					&& codes[i + 1].IsStloc()) || CompareInstructions(codes[i], onHeadLoc),
				code => new[]
					{
					code,
					apparelRecord,
					compilerGenArg,
					headFacing,
					FishTranspiler.Call(GetDrawOffset),
					FishTranspiler.Call(typeof(Vector3), "op_Addition")
					});
		}
		catch
		{
			Log.Error("Oversized Apparel failed to patch PawnRenderer.DrawHeadHair. This is likely an incompatibility with another mod. Hugslib (opened by pressing F12) could reveal which other mod is patching this same method.");
			return null!;
		}
	}

	public static CodeInstructions PawnRenderer_DrawBodyApparel_Transpiler(CodeInstructions instructions, MethodBase original)
	{
		var codes = instructions.ToList();

		var mesh = FishTranspiler.FirstArgument(original, typeof(Mesh));
		var rot4 = FishTranspiler.FirstArgument(original, typeof(Rot4));
		var shellLoc = FishTranspiler.FirstArgument(original, p => p.ParameterType == typeof(Vector3) && p.Name == "shellLoc");
		var apparelRecord = FishTranspiler.LocalVariable(FishTranspiler.GetLocalOperandsOrIndices(codes, c => c.CallReturns(typeof(ApparelGraphicRecord))).First());

		try
		{
			return codes
				.ReplaceAt((codes, i)
					=> CompareInstructions(codes[i], shellLoc)
					&& codes[i + 1].IsStloc(),
				code => new[]
					{
					code,
					apparelRecord,
					rot4,
					FishTranspiler.Call(GetDrawOffset),
					FishTranspiler.Call(typeof(Vector3), "op_Addition")
					})
				.Replace(code => CompareInstructions(code, mesh),
				code => new[]
					{
					code,
					rot4,
					apparelRecord,
					FishTranspiler.Call(ChangeMeshSize)
					});
		}
		catch
		{
			Log.Error("Oversized Apparel failed to patch PawnRenderer.DrawBodyApparel. This is likely an incompatibility with another mod. Hugslib (opened by pressing F12) could reveal which other mod is patching this same method.");
			return null!;
		}
	}

	private static bool CompareOperands(object? lhs, object? rhs)
		=> lhs.TryGetIndex() is int lhIndex && rhs.TryGetIndex() is int rhIndex
		? lhIndex == rhIndex
		: lhs == rhs;

	private static int? TryGetIndex(this object? obj)
		=> obj is LocalVariableInfo info ? info.LocalIndex
		: obj is not string and IConvertible convertible ? convertible.ToInt32(CultureInfo.InvariantCulture)
		: null;

	[Obsolete("Use == instead, once possible")]
	private static bool CompareInstructions(CodeInstruction code, FishTranspiler.Container helper) // temporary workaround for a bug in fishery
		=> helper.OpCode == code.opcode
		&& CompareOperands(helper.Operand, code.operand);

	public static CodeInstructions CombatExtended_PawnRenderer_DrawHeadHair_DrawApparel_Transpiler(CodeInstructions instructions, MethodBase original)
	{
		var codes = instructions.ToList();

		var vector3VarIndices = FishTranspiler.GetLocalOperandsOrIndices(codes, c => c.Calls(AccessTools.Method(typeof(Vector3), "op_Addition"))).ToArray();

		var mesh = FishTranspiler.FirstLocalVariable(codes, typeof(Mesh));
		var headWearPos = FishTranspiler.LocalVariable(vector3VarIndices[0]);
		var maskLoc = FishTranspiler.LocalVariable(vector3VarIndices[1]);
		var apparelRecord = FishTranspiler.FirstLocalVariable(codes, typeof(ApparelGraphicRecord));
		var headFacing = FishTranspiler.Argument(original, "headFacing");

		try
		{
			return codes
				.Replace(code => CompareInstructions(code, mesh),
				code => new[]
					{
					code,
					headFacing,
					apparelRecord,
					FishTranspiler.Call(ChangeMeshSize)
					})
				.Replace(code
					=> CompareInstructions(code, headWearPos) || CompareInstructions(code, maskLoc),
				code => new[]
					{
					code,
					apparelRecord,
					headFacing,
					FishTranspiler.Call(GetDrawOffset),
					FishTranspiler.Call(typeof(Vector3), "op_Addition")
					});

		}
		catch (Exception ex)
		{
			Log.Error($"Oversized Apparel failed to patch Combat Extended's Harmony_PawnRenderer_DrawHeadHair.DrawHeadApparel. This is CE's fault.\n{ex}");
			return null!;
		}
	}

	public static bool ShouldSkipCache(PawnRenderer instance)
	{
		var graphics = instance.graphics.apparelGraphics;
		for (var i = 0; i < graphics.Count; i++)
		{
			if (graphics[i].sourceApparel.def.GetModExtension<Extension>() is { } extension && extension.drawSize is var size && (size.x >= 1 || size.y >= 1))
				return true;
		}
		return false;
	}

	public static Vector3 GetDrawOffset(ApparelGraphicRecord apparelRecord, Rot4 rot)
		=> apparelRecord.sourceApparel.def.graphicData.DrawOffsetForRot(rot); //taking this directly from graphic instead of the def doesn't work for some reason

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