using System;
using System.CodeDom;

namespace SMAInteropConverter.Helpers
{
    public static class CodeDomEx
    {
        public static CodeMemberMethod CreateMethod(string name, Type retType)
        {
            return new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = name,
                ReturnType = new CodeTypeReference(retType),
            };
        }

        public static CodeParameterDeclarationExpression CreateParam(string name, Type type)
        {
            return new CodeParameterDeclarationExpression
            {
                Type = new CodeTypeReference(type),
                Name = name,
            };
        }

        public static CodeFieldReferenceExpression CreateThisFieldRef(string name)
        {
            return new CodeFieldReferenceExpression
            {
                FieldName = name,
                TargetObject = new CodeThisReferenceExpression()
            };
        }

        public static CodeMemberField CreatePrivateField(string name, Type type)
        {
            return new CodeMemberField
            {
                Name = name,
                Type = new CodeTypeReference(type),
                Attributes = MemberAttributes.Private,
            };
        }
    }
}
