using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.CodeGenerators
{
    public class RegisterManager
    {
        public Dictionary<string, byte> VarToReg = new();
        public byte RegisterCount = 0;

        private Stack<HashSet<byte>> _usedRegisters = new();

        public byte AllocateRegister(string name)
        {
            byte reg = FindFreeRegisterRange();
            _usedRegisters.Peek().Add(reg);
            VarToReg.Add(name, reg);
            return reg;
        }

        public byte AllocateRegister(byte count = 1)
        {
            byte reg = FindFreeRegisterRange(count);
            for (byte b = 0; b < count; b++)
                _usedRegisters.Peek().Add((byte)(reg + b));
            return reg;
        }

        public void FreeRegister(byte register)
        {
            List<string> removeKeys = new();
            foreach (KeyValuePair<string, byte> pair in VarToReg)
                if (pair.Value == register)
                    removeKeys.Add(pair.Key);
            foreach (string key in removeKeys)
                VarToReg.Remove(key);

            foreach (HashSet<byte> layer in _usedRegisters)
                layer.Remove(register);
        }

        public void PushNewBlock() => _usedRegisters.Push(new());

        public HashSet<byte> ExitBlock()
        {
            HashSet<byte> cleared = _usedRegisters.Pop();
            foreach (byte reg in cleared)
                FreeRegister(reg);
            return cleared;
        }

        // Finds the first range of free registers and returns the index of the lowest
        // register of that range
        private byte FindFreeRegisterRange(byte count = 1)
        {
            if (count == 0)
                throw new RegisterManagerException("Tried finding space for 0 registers.");

            byte found = 0;
            for (byte b = 0; true; b++)
            {
                if (IsRegisterUsed(b))
                    found = 0;
                else if (++found == count)
                {
                    if (b + count > RegisterCount)
                        RegisterCount = (byte)(b + count);
                    return b;
                }

                if (b == byte.MaxValue)
                    break;
            }

            throw new RegisterManagerException("Reached byte 255 when searching for free registers. There are no more registers available.");
        }

        private bool IsRegisterUsed(byte register)
        {
            foreach (HashSet<byte> layer in _usedRegisters)
                if (layer.Contains(register))
                    return true;
            return false;
        }
    }
}
