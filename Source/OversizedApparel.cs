global using System;
global using System.Collections.Generic;
global using RimWorld;
global using UnityEngine;
global using Verse;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using static System.Reflection.Emit.OpCodes;

namespace OversizedApparel;
[StaticConstructorOnStartup]
internal static class OversizedApparel
{
	static OversizedApparel()
	{
		//Enable Vanilla Expanded Framework's "Disable Texture Caching" option, as it's required for oversized apparel to not get cut off
		if (Type.GetType("VFECore.VFEGlobal, VFECore")?.GetField("settings")?.GetValue(null) is { } vEFsettings)
			Type.GetType("VFECore.VFEGlobalSettings, VFECore")?.GetField("disableCaching")?.SetValue(vEFsettings, true);

		Harmony.Patch(AccessTools.FirstMethod(typeof(PawnRenderer), m => m.Name.Contains("DrawApparel") && m.HasAttribute<CompilerGeneratedAttribute>() && m.GetParameters().Any(p => p.ParameterType == typeof(ApparelGraphicRecord))),
			transpiler: new(typeof(OversizedApparel).GetMethod(nameof(PawnRenderer_DrawHeadHair_DrawApparel_Transpiler)), Priority.Last));

		Harmony.Patch(AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.DrawBodyApparel)), transpiler: new(typeof(OversizedApparel).GetMethod(nameof(PawnRenderer_DrawBodyApparel_Transpiler)), Priority.Last));
	}

	public static IEnumerable<CodeInstruction> PawnRenderer_DrawHeadHair_DrawApparel_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		List<CodeInstruction> codes = new(instructions);

		var meshSuccess = false;
		var offsetSuccess = false;
		var apparelRecordArgIndex = GetArgumentIndex(original, typeof(ApparelGraphicRecord));
		var apparelRecordArgOpcode = GetArgumentOpCode(apparelRecordArgIndex);
		var rot4Field = typeof(PawnRenderer).GetNestedTypes(AccessTools.all).Where(type => type.HasAttribute<CompilerGeneratedAttribute>() && type.IsValueType && type.IsSealed)
			.Select(type => AccessTools.Field(type, "headFacing")).First(f => f.FieldType == typeof(Rot4));
		//var compilerGenStructType = typeof(PawnRenderer).GetNestedTypes(AccessTools.all).Where(type => AccessTools.Field(type, "headFacing") is not null && type.HasAttribute<CompilerGeneratedAttribute>() && type.IsValueType && type.IsSealed).First();
		// p.ParameterType == compilerGenStructType returns null, so I have to be a bit less specific here. Though it would work with null too, just ends up being unsafe with pointer
		var compilerGenArgIndex = GetArgumentIndex(original, p => /*p.ParameterType == compilerGenStructType &&*/ p.ParameterType.IsByRef && p.Position != 0);
		var compilerGenArgOpcode = GetArgumentOpCode(compilerGenArgIndex);

		for (var i = 0; i < codes.Count; i++)
		{
			if (CallReturns(codes[i], typeof(Mesh)) && codes[i + 1].IsStloc())
			{
				yield return codes[i];
				yield return new(compilerGenArgOpcode, compilerGenArgIndex > 3 ? compilerGenArgIndex : null);
				yield return new(Ldfld, rot4Field);
				yield return new(apparelRecordArgOpcode, apparelRecordArgIndex > 3 ? apparelRecordArgIndex : null);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(ChangeMeshSize)));
				meshSuccess = true;
			}
			else if (CallReturns(codes[i], typeof(Vector3)) && codes[i + 1].IsStloc())
			{
				yield return codes[i];
				yield return new(apparelRecordArgOpcode, apparelRecordArgIndex > 3 ? apparelRecordArgIndex : null);
				yield return new(compilerGenArgOpcode, compilerGenArgIndex > 3 ? compilerGenArgIndex : null);
				yield return new(Ldfld, rot4Field);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(GetDrawOffset)));
				yield return new(Call, AccessTools.Method(typeof(Vector3), "op_Addition"));
				offsetSuccess = true;
			}
			else
			{
				yield return codes[i];
			}
		}

		if (!meshSuccess && !offsetSuccess)
			Log.Error("Oversized Apparel failed to patch PawnRenderer.DrawHeadHair. This is likely an incompatibility with another mod. Hugslib (opened by pressing F12) could reveal which other mod is patching this same method.");
	}

	public static IEnumerable<CodeInstruction> PawnRenderer_DrawBodyApparel_Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		var codes = instructions.ToList();
		var meshSuccess = false;
		var offsetSuccess = false;
		var meshArgumentIndex = GetArgumentIndex(original, typeof(Mesh));
		var meshArgumentOpCode = GetArgumentOpCode(meshArgumentIndex);
		var rot4ArgumentIndex = GetArgumentIndex(original, typeof(Rot4));
		var rot4ArgumentOpCode = GetArgumentOpCode(rot4ArgumentIndex);
		var shellLocArgumentIndex = GetArgumentIndex(original, p => p.ParameterType == typeof(Vector3) && p.Name == "shellLoc");
		var shellLocArgumentOpCode = GetArgumentOpCode(shellLocArgumentIndex);
		var apparelRecordLocalOpCode = Ldloc_0;
		var apparelRecordLocalIndex = 0;
		object meshArgumentOperand = meshArgumentOpCode == Ldarg_S ? meshArgumentIndex : null;
		object shellLocArgumentOperand = shellLocArgumentOpCode == Ldarg_S ? shellLocArgumentIndex : null;

		for (var i = 0; i < codes.Count; i++)
		{
			if (CallReturns(codes[i], typeof(ApparelGraphicRecord)) && codes[i + 1].IsStloc())
			{
				var code = codes[i + 1].opcode;
				apparelRecordLocalOpCode = code == Stloc_0 ? Ldloc_0
					: code == Stloc_1 ? Ldloc_1
					: code == Stloc_2 ? Ldloc_2
					: code == Stloc_3 ? Ldloc_3
					: Ldloc_S;
				if (codes[i + 1].operand is byte index)
					apparelRecordLocalIndex = index;
				yield return codes[i];
			}
			else if (codes[i].opcode == shellLocArgumentOpCode && codes[i].operand == shellLocArgumentOperand && codes[i + 1].IsStloc())
			{
				yield return codes[i];
				yield return new(apparelRecordLocalOpCode, apparelRecordLocalIndex == 0 ? null : apparelRecordLocalIndex);
				yield return new(rot4ArgumentOpCode, rot4ArgumentOpCode == Ldarg_S ? rot4ArgumentIndex : null);
				yield return new(Call, typeof(OversizedApparel).GetMethod(nameof(GetDrawOffset)));
				yield return new(Call, AccessTools.Method(typeof(Vector3), "op_Addition"));
				offsetSuccess = true;
			}
			else if (codes[i].opcode == meshArgumentOpCode && codes[i].operand == meshArgumentOperand)
			{
				yield return codes[i];
				yield return new(rot4ArgumentOpCode, rot4ArgumentOpCode == Ldarg_S ? rot4ArgumentIndex : null);
				yield return new(apparelRecordLocalOpCode, apparelRecordLocalIndex == 0 ? null : apparelRecordLocalIndex);
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

	public static bool CallReturns(CodeInstruction instruction, Type type) => (instruction.opcode == Callvirt || instruction.opcode == Call) && (instruction.operand as MethodInfo).ReturnType == type;
	public static int GetArgumentIndex(MethodBase method, Func<ParameterInfo, bool> predicate) => method.GetParameters().FirstIndexOf(predicate) + 1;
	public static int GetArgumentIndex(MethodBase method, Type argumentType) => method.GetParameters().FirstIndexOf(p => p.ParameterType == argumentType) + 1;
	public static OpCode GetArgumentOpCode(int index) => index switch
	{
		0 => Ldarg_0,
		1 => Ldarg_1,
		2 => Ldarg_2,
		3 => Ldarg_3,
		_ => Ldarg_S
	};

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