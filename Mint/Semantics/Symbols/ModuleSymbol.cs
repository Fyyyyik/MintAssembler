using System;
using System.Collections.Generic;
using System.Text;

namespace Mint.Semantics.Symbols
{
    public class ModuleSymbol
    {
        public required string Name { get; init; }
        public Dictionary<string, ObjectSymbol> LocalObjects { get; } = new();
        public Dictionary<string, XRefSymbol> XRefObjects { get; } = new();
        public Dictionary<string, EnumSymbol> Enums { get; } = new();

        public IInstanciable GetInstanciable(string objName)
        {
            if (LocalObjects.TryGetValue(objName, out ObjectSymbol? objSbl))
                return objSbl;
            if (XRefObjects.TryGetValue(objName, out XRefSymbol? xRefSbl))
                return xRefSbl;

            foreach (ObjectSymbol obj in LocalObjects.Values)
                if (obj.FullName == objName)
                    return obj;

            throw new Exception($"No instanciable object with name '{objName}' has been found.");
        }
    }
}
