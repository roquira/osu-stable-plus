using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Osu.StablePlus.Utils.Extensions;

namespace Osu.StablePlus.Utils.IL;

/// <summary>
///     Sequentially reads all OpCodes from a method body's IL byte instructions.
/// </summary>
internal class OpCodeReader
{
    private static readonly OpCode[] OneByteOpcodes, TwoByteOpcodes;

    private readonly IReadOnlyList<byte> _ilInstructions;
    private int _position;

    static OpCodeReader()
    {
        OneByteOpcodes = new OpCode[0xe1];
        TwoByteOpcodes = new OpCode[0x1f];

        var fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (var field in fields)
        {
            var opcode = field.GetValue<OpCode>(null);
            if (opcode.OpCodeType == OpCodeType.Nternal)
                continue;

            if (opcode.Size == 1)
                OneByteOpcodes[opcode.Value] = opcode;
            else
                TwoByteOpcodes[opcode.Value & 0xff] = opcode;
        }
    }

    public OpCodeReader(IReadOnlyList<byte> ilInstructions)
    {
        _ilInstructions = ilInstructions;
        _position = 0;
    }

    /// <summary>
    ///     Gets the opcode with the given <see cref="OpCode.Value" />.
    /// </summary>
    internal static OpCode FromValue(short value) =>
        (value & 0xff00) == 0xfe00 ? TwoByteOpcodes[value & 0xff] : OneByteOpcodes[value];

    public IEnumerable<OpCode> GetOpCodes() => GetOperandOffsets().Select(instruction => instruction.Key);

    /// <summary>
    ///     Reads every opcode together with the offset of its operand in the IL.
    /// </summary>
    internal IEnumerable<KeyValuePair<OpCode, int>> GetOperandOffsets()
    {
        while (_position < _ilInstructions.Count)
        {
            var op = ReadOpCode();
            var operand = _position;
            AdvanceThroughOperand(op);
            yield return new KeyValuePair<OpCode, int>(op, operand);
        }
    }

    private OpCode ReadOpCode()
    {
        var op = _ilInstructions[_position++];
        var table = OneByteOpcodes;
        if (op == 0xfe)
        {
            EnsureAvailable(1);
            op = _ilInstructions[_position++];
            table = TwoByteOpcodes;
        }

        if (op >= table.Length || table[op].Size == 0)
            throw new InvalidDataException("Invalid IL opcode.");
        return table[op];
    }

    private void AdvanceThroughOperand(OpCode op)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineSwitch:
                var count = ReadInt32();
                if (count < 0 || count > (_ilInstructions.Count - _position) / 4)
                    throw new InvalidDataException("Invalid or truncated IL switch table.");
                Advance(count * 4);
                break;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                Advance(8);
                break;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.InlineSig:
            case OperandType.ShortInlineR:
                Advance(4);
                break;
            case OperandType.InlineVar:
                Advance(2);
                break;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                Advance(1);
                break;
            case OperandType.InlineNone:
                break;
#pragma warning disable CS0618
            case OperandType.InlinePhi:
#pragma warning restore CS0618
            default:
                throw new ArgumentException("Unsupported operand type " + op.OperandType);
        }
    }

    private int ReadInt32()
    {
        EnsureAvailable(4);
        var value = _ilInstructions[_position]
                    | (_ilInstructions[_position + 1] << 8)
                    | (_ilInstructions[_position + 2] << 16)
                    | (_ilInstructions[_position + 3] << 24);
        _position += 4;
        return value;
    }

    private void Advance(int count)
    {
        EnsureAvailable(count);
        _position += count;
    }

    private void EnsureAvailable(int count)
    {
        if (count > _ilInstructions.Count - _position)
            throw new InvalidDataException("Truncated IL operand.");
    }
}
