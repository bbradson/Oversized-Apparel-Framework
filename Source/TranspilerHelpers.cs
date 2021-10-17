using static System.Reflection.Emit.OpCodes;

namespace OversizedApparel;
public static class TranspilerHelpers
{
	public struct InstructionHelper
	{
		public OpCode OpCode { get; set; }
		public object Operand { get; set; }
		public override string ToString() => $"{OpCode} {Operand}";
	}

	public static InstructionHelper FirstLdarg(MethodBase method, Type argumentType) => FirstLdarg(method, p => p.ParameterType == argumentType);
	public static InstructionHelper FirstLdarg(MethodBase method, Func<ParameterInfo, bool> predicate) => GetLdarg(FirstArgumentIndex(method, predicate));
	public static InstructionHelper GetLdarg(int index) => new() { OpCode = GetLdargOpCode(index), Operand = GetOperandFromIndex(index) };

	public static InstructionHelper FirstLdloc(MethodBase method, Type localType) => FirstLdloc(method, l => l.LocalType == localType);
	public static InstructionHelper FirstLdloc(MethodBase method, Predicate<LocalVariableInfo> predicate) => GetLdlocs(method, predicate).First();
	public static IEnumerable<InstructionHelper> GetLdlocs(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		foreach (var index in GetLocalIndices(method, predicate))
			yield return new() { OpCode = GetLdlocOpCode(index), Operand = GetOperandFromIndex(index) };
	}
	public static InstructionHelper FirstLdloc(IEnumerable<CodeInstruction> codes, Type localType) => GetLdloc(GetLocalOperands(codes, c => c.Returns(localType)).First());
	public static InstructionHelper GetLdloc(object operand) => operand is LocalBuilder builder ? GetLdloc(builder) : GetLdloc((int)operand);
	public static InstructionHelper GetLdloc(LocalBuilder builder) => new() { OpCode = GetLdlocOpCode(builder.LocalIndex), Operand = builder };
	public static InstructionHelper GetLdloc(int index) => new() { OpCode = GetLdlocOpCode(index), Operand = GetOperandFromIndex(index) };

	public static InstructionHelper FirstStloc(MethodBase method, Type localType) => FirstStloc(method, l => l.LocalType == localType);
	public static InstructionHelper FirstStloc(MethodBase method, Predicate<LocalVariableInfo> predicate) => GetStlocs(method, predicate).First();
	public static IEnumerable<InstructionHelper> GetStlocs(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		foreach (var index in GetLocalIndices(method, predicate))
			yield return new() { OpCode = GetStlocOpCode(index), Operand = GetOperandFromIndex(index) };
	}
	public static InstructionHelper FirstStloc(IEnumerable<CodeInstruction> codes, Type localType) => GetStloc(GetLocalOperands(codes, c => c.Returns(localType)).First());
	public static InstructionHelper GetStloc(object operand) => operand is LocalBuilder builder ? GetStloc(builder) : GetStloc((int)operand);
	public static InstructionHelper GetStloc(LocalBuilder builder) => new() { OpCode = GetStlocOpCode(builder.LocalIndex), Operand = builder };
	public static InstructionHelper GetStloc(int index) => new() { OpCode = GetStlocOpCode(index), Operand = GetOperandFromIndex(index) };

	public static bool CallReturns(this CodeInstruction instruction, Type type) => (instruction.opcode == Callvirt || instruction.opcode == Call) && ((MethodInfo)instruction.operand).ReturnType == type;
	public static bool FieldReturns(this CodeInstruction instruction, Type type) => (instruction.opcode == Ldfld || instruction.opcode == Ldsfld) && ((FieldInfo)instruction.operand).FieldType == type;
	public static bool Returns(this CodeInstruction instruction, Type type) => instruction.CallReturns(type) || instruction.FieldReturns(type);

	public static int FirstArgumentIndex(MethodBase method, Func<ParameterInfo, bool> predicate) => method.GetParameters().First(predicate).Position + (method.IsStatic ? 0 : 1);
	public static int FirstArgumentIndex(MethodBase method, Type argumentType) => FirstArgumentIndex(method, p => p.ParameterType == argumentType);

	public static IEnumerable<object> GetLocalOperands(IEnumerable<CodeInstruction> codes, Predicate<CodeInstruction> predicate)
	{
		CodeInstruction previousCode = null;
		foreach (var code in codes)
		{
			if (previousCode is not null && predicate(previousCode) && code.IsStloc() && code.opcode is var opcode)
			{
				yield return opcode == Stloc_0 ? 0
					: opcode == Stloc_1 ? 1
					: opcode == Stloc_2 ? 2
					: opcode == Stloc_3 ? 3
					: code.operand is LocalBuilder builder ? builder
					: code.operand is byte index ? index
					: code.operand is ushort unsigned ? unsigned
					: throw new NotSupportedException($"{code.opcode} returned {code.operand}. This is not supported.");
			}
			previousCode = code;
		}
	}
	public static IEnumerable<int> GetLocalIndices(IEnumerable<CodeInstruction> codes, Predicate<CodeInstruction> predicate)
	{
		foreach (var operand in GetLocalOperands(codes, predicate))
			yield return operand is LocalBuilder builder ? builder.LocalIndex : (int)operand;
	}
	public static IEnumerable<int> GetLocalIndices(MethodBase method, Predicate<LocalVariableInfo> predicate)
	{
		var variables = method.GetMethodBody().LocalVariables;
		for (var i = 0; i < variables.Count; i++)
		{
			if (predicate(variables[i]))
				yield return variables[i].LocalIndex;
		}
	}

	public static OpCode GetLdargOpCode(int index) => index switch
	{
		0 => Ldarg_0,
		1 => Ldarg_1,
		2 => Ldarg_2,
		3 => Ldarg_3,
		< 256 => Ldarg_S,
		_ => Ldarg
	};
	public static OpCode GetStlocOpCode(int index) => index switch
	{
		0 => Stloc_0,
		1 => Stloc_1,
		2 => Stloc_2,
		3 => Stloc_3,
		< 256 => Stloc_S,
		_ => Stloc
	};
	public static OpCode GetLdlocOpCode(int index) => index switch
	{
		0 => Ldloc_0,
		1 => Ldloc_1,
		2 => Ldloc_2,
		3 => Ldloc_3,
		< 256 => Ldloc_S,
		_ => Ldloc
	};
	public static object GetOperandFromIndex(int index) => index > 3 ? index : null;
}