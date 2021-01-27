using System;
using System.CodeDom;

namespace SMAInteropConverter
{
    public class RegistryType
    {
        public string Name { get; }
        public Type Registry { get; }
        public Type Member { get; }
        public Type Key { get; } = typeof(int);
        public CodeIndexerExpression CreateMemberGetter(CodeFieldReferenceExpression regRef, string paramName)
        {
            return new CodeIndexerExpression(regRef, new CodeArgumentReferenceExpression(paramName));
        }

        public RegistryType(Type reg, Type mem, string name) 
        {
            this.Name = name;
            this.Registry = reg;
            this.Member = mem;
        }
    }
}
