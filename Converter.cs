using Microsoft.CSharp;
using SMAInteropConverter.Helpers;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SMAInteropConverter
{
    public class Converter : ConverterBase
    {
        private List<RegistryType> RegPairs { get; }
        private Type Wrapped { get; }
        private CodeFieldReferenceExpression WrappedRef { get; set; }
        private VariableNameCreator Namer { get; } = new VariableNameCreator();

        public CodeConstructor Constructor { get; } = new CodeConstructor
        {
            Attributes = MemberAttributes.Public | MemberAttributes.Final
        };

        private bool AddedSetters { get; set; }
        private bool AddedGetters { get; set; }
        private bool AddedMethods { get; set; }

        public Converter(List<RegistryType> regTypes, Type wrapped)
            : base(wrapped.Name.AsAlphabetic() + "Svc")
        {
            this.RegPairs = regTypes;
            this.Wrapped = wrapped;
            if (!Wrapped.IsRegMember(regTypes, out _))
                WrappedRef = AddConstructorInitializedField(Wrapped);
        }

        public string GenerateSource()
        {
            Klass.Members.Add(Constructor);

            var options = new CodeGeneratorOptions();
            using (var writer = new StringWriter())
            using (var provider = new CSharpCodeProvider())
            {
                provider.GenerateCodeFromCompileUnit(Unit, writer, options);
                return writer.ToString();
            }
        }

        private bool TryGetReferenceToFieldOfType(Type type, out CodeFieldReferenceExpression reference)
        {
            var field = (CodeMemberField)Klass.Members
                .Cast<CodeObject>()
                .Where(x => x is CodeMemberField f && f.Type.BaseType == type.FullName)
                .FirstOrDefault();

            reference = field != null
                ? CodeDomEx.CreateThisFieldRef(field.Name)
                : null;

            return field != null;
        }

        private CodeFieldReferenceExpression AddConstructorInitializedField(Type type)
        {
            // Add parameter arg
            var param = CodeDomEx.CreateParam(Namer.GetName(), type);
            Constructor.Parameters.Add(param);

            // Add field
            var field = CodeDomEx.CreatePrivateField(Namer.GetName(), type);
            Klass.Members.Add(field);
            var fieldRef = CodeDomEx.CreateThisFieldRef(field.Name);

            // Assign to field from constructor
            var paramRef = new CodeArgumentReferenceExpression(param.Name);
            var assignment = new CodeAssignStatement(fieldRef, paramRef);
            Constructor.Statements.Add(assignment);

            return CodeDomEx.CreateThisFieldRef(field.Name);
        }

        public CompilerResults Compile()
        {
            Klass.Members.Add(Constructor);

            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false };
                var compilerResults = provider.CompileAssemblyFromDom(parameters, Unit);
                if (compilerResults.Errors.HasErrors)
                {
                    foreach (var compilerError in compilerResults.Errors)
                    {
                        var error = compilerError.ToString();
                        Console.WriteLine(error);
                    }

                    throw new InvalidOperationException($"Service compilation failed with {compilerResults.Errors.Count} errors");
                }

                Console.WriteLine("Service compiled successfully");
                return compilerResults;
            }
        }

        private CodeVariableReferenceExpression GetRegistryMemberFromRegistryField(CodeMemberMethod method, RegistryType regPair, CodeFieldReferenceExpression regRef)
        {
            // Add as the method's first parameter
            var param = new CodeParameterDeclarationExpression(regPair.Key, Namer.GetName());
            method.Parameters.Add(param);

            // Create the local variable to store the reference to the registry member
            var thisVarDeclaration = new CodeVariableDeclarationStatement(
                regPair.Member,
                Namer.GetName());

            method.Statements.Add(thisVarDeclaration);


            // eg. IElement thisIElement = Svc.SM.Registry.Element[thisId];
            CodeAssignStatement thisVarAssignment = new CodeAssignStatement(
                new CodeVariableReferenceExpression(thisVarDeclaration.Name),
                new CodeIndexerExpression(regRef, new CodeArgumentReferenceExpression(param.Name)));

            method.Statements.Add(thisVarAssignment);

            return new CodeVariableReferenceExpression(thisVarDeclaration.Name);
        }

        private void ConvertGetter(MethodInfo getter)
        {
            var retType = getter.ReturnType;
            var propertyName = getter.Name.GetPropertyName();
            var method = CodeDomEx.CreateMethod(getter.Name.GetNormalMethodName(), retType);

            if (Wrapped.IsRegMember(RegPairs, out var regPair))
            {
                CodeFieldReferenceExpression regRef;
                if (!TryGetReferenceToFieldOfType(regPair.Registry, out regRef))
                {
                    regRef = AddConstructorInitializedField(regPair.Registry);
                }

                var localInstanceRef = GetRegistryMemberFromRegistryField(method, regPair, regRef);
                var propertyReference = new CodePropertyReferenceExpression(localInstanceRef, propertyName);
                method.Statements.Add(new CodeMethodReturnStatement(propertyReference));
            }
            else
            {
                var wrapPropRef = new CodePropertyReferenceExpression(WrappedRef, propertyName);
                method.Statements.Add(new CodeMethodReturnStatement(wrapPropRef));
            }

            Klass.Members.Add(method);
        }

        private void ConvertSetter(MethodInfo setter)
        {
            var propertyName = setter.Name.GetPropertyName();
            var method = CodeDomEx.CreateMethod(setter.Name.GetNormalMethodName(), typeof(void));

            CodePropertyReferenceExpression targetProp =
              new CodePropertyReferenceExpression(WrappedRef, propertyName);

            if (Wrapped.IsRegMember(RegPairs, out var regPair))
            {
                CodeFieldReferenceExpression regRef;
                if (!TryGetReferenceToFieldOfType(regPair.Registry, out regRef))
                {
                    regRef = AddConstructorInitializedField(regPair.Registry);
                }

                var localInstanceRef = GetRegistryMemberFromRegistryField(method, regPair, regRef);
                targetProp = new CodePropertyReferenceExpression(new CodeVariableReferenceExpression(localInstanceRef.VariableName), propertyName);
            }

            var info = setter.GetParameters()[0];
            var paramType = info.ParameterType;
            if (paramType.IsRegMember(RegPairs, out _))
            {
                string x = ConvertParameter(info, method); 
                method.Statements.Add(new CodeAssignStatement(targetProp, new CodeVariableReferenceExpression(x)));
            }
            else
            {
                string x = KeepParameter(info, method);
                method.Statements.Add(new CodeAssignStatement(targetProp, new CodeArgumentReferenceExpression(x)));
            }

            Klass.Members.Add(method);
        }

        private string KeepParameter(ParameterInfo info, CodeMemberMethod method)
        {
            var param = new CodeParameterDeclarationExpression(new CodeTypeReference(info.ParameterType), Namer.GetName());
            method.Parameters.Add(param);
            return param.Name;
        }

        public Converter WithGetters()
        {
            if (!AddedGetters)
            {
                var getters = Wrapped.GetGetters();
                if (getters != null)
                {
                    foreach (var getter in getters)
                    {
                        ConvertGetter(getter);
                    }
                }
            }

            AddedGetters = true;
            return this;
        }

        public Converter WithSetters()
        {
            if (!AddedSetters)
            {
                var setters = Wrapped.GetSetters();
                if (setters != null)
                {
                    foreach (var setter in setters)
                    {
                        ConvertSetter(setter);
                    }
                }
            }

            AddedSetters = true;
            return this;
        }

        private string ConvertParameter(ParameterInfo info, CodeMemberMethod method)
        {
            var type = info.ParameterType;
            if (type.IsRegMember(RegPairs, out var regPair))
            {
                CodeFieldReferenceExpression regRef;
                if (!TryGetReferenceToFieldOfType(regPair.Registry, out regRef))
                {
                    regRef = AddConstructorInitializedField(regPair.Registry);
                }

                var param = new CodeParameterDeclarationExpression(regPair.Key, Namer.GetName());
                method.Parameters.Add(param);

                // Declare localVar
                var localVarDeclaration = new CodeVariableDeclarationStatement(regPair.Member, Namer.GetName());
                method.Statements.Add(localVarDeclaration);

                // Assign to localVar
                var localVarAssignment = new CodeAssignStatement(
                  new CodeVariableReferenceExpression(localVarDeclaration.Name),
                  new CodeIndexerExpression(regRef, new CodeArgumentReferenceExpression(param.Name)));

                method.Statements.Add(localVarAssignment);
                return localVarDeclaration.Name;
            }
            else
            {
                Console.WriteLine("ERROR IN PARAMETER CONVERSION");
                return null;
            }
        }

        private void ConvertMethod(MethodInfo info)
        {
            var method = CodeDomEx.CreateMethod(info.Name, info.ReturnType);
            var targetMethodRef = new CodeMethodReferenceExpression(WrappedRef, info.Name);
            var targetMethodArgs = new List<CodeExpression>();

            if (Wrapped.IsRegMember(RegPairs, out var regPair))
            {
                CodeFieldReferenceExpression regRef;
                if (!TryGetReferenceToFieldOfType(regPair.Registry, out regRef))
                {
                    regRef = AddConstructorInitializedField(regPair.Registry);
                }

                var localInstanceRef = GetRegistryMemberFromRegistryField(method, regPair, regRef);
                targetMethodRef = new CodeMethodReferenceExpression(localInstanceRef, info.Name);
            }

            var parameters = info.GetParameters();
            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter.ParameterType.IsRegMember(RegPairs, out _))
                    {
                        string x = ConvertParameter(parameter, method);
                        targetMethodArgs.Add(new CodeVariableReferenceExpression(x));
                    }
                    else
                    {
                        string x = KeepParameter(parameter, method);
                        targetMethodArgs.Add(new CodeArgumentReferenceExpression(x));
                    }
                }
            }

            method.Statements.Add(
                new CodeMethodReturnStatement(
                new CodeMethodInvokeExpression(targetMethodRef, targetMethodArgs.ToArray())));

            Klass.Members.Add(method);
        }

        public Converter WithMethods()
        {
            if (!AddedMethods)
            {
                var methods = Wrapped.GetConvertableMethods();
                if (methods != null)
                {
                    foreach (var m in methods)
                    {
                        ConvertMethod(m);
                    }
                }
            }

            AddedMethods = true;
            return this;
        }
    }
}
