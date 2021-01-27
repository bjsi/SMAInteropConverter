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
            : base(wrapped.GetSvcName(), wrapped.GetSvcNamespace())
        {
            this.RegPairs = regTypes;
            this.Wrapped = wrapped;

            // TODO: UI 
            if (!Wrapped.IsRegMember(regTypes, out _) && !Wrapped.IsReg(regTypes, out _) && !Wrapped.Name.Contains("IElementWdw"))
                WrappedRef = AddConstructorInitializedField(Wrapped);

            RegistryType regType;
            if (Wrapped.IsRegMember(regTypes, out regType) || Wrapped.IsReg(regTypes, out regType))
                WrappedRef = AddDirectlyInitializedField(regType.Registry, $"Registry.{regType.Name}");

            if (Wrapped.Name.Contains("IElementWdw"))
                WrappedRef = AddDirectlyInitializedField(Wrapped, $"UI.ElementWdw");
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

        private CodeFieldReferenceExpression AddDirectlyInitializedField(Type type, string svcString)
        {
            var field = CodeDomEx.CreatePrivateField(Namer.GetName(), type);
            field.InitExpression = new CodeSnippetExpression("SuperMemoAssistant.Services.Svc.SM." + svcString);
            Klass.Members.Add(field);
            return CodeDomEx.CreateThisFieldRef(field.Name);
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

        public CompilerResults Compile(IEnumerable<string> referencedAssemblies)
        {
            Klass.Members.Add(Constructor);
            Unit.ReferencedAssemblies.AddRange(referencedAssemblies.ToArray());
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

        // TODO: IEnumerable<IElement> etc.
        private void ConvertReturnedProperty(CodeMemberMethod method, Type retType, CodePropertyReferenceExpression prop)
        {
            if (retType.IsRegMember(RegPairs, out var regPair))
            {
                var idRef = new CodePropertyReferenceExpression(prop, "Id");
                method.Statements.Add(new CodeMethodReturnStatement(idRef));
                method.ReturnType = new CodeTypeReference(typeof(int));
            }
            else if (retType.Name == "IEnumerable`1") // TODO
            {
                //if (retType.GetIEnumerableTypeArgs().First().IsRegMember(RegPairs, out _))
                //{
                //    method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression()));
                //}
                //else
                //{
                    method.Statements.Add(new CodeMethodReturnStatement(prop));
                //}
            }
            else
            {
                method.Statements.Add(new CodeMethodReturnStatement(prop));
            }
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
                ConvertReturnedProperty(method, retType, propertyReference);
            }
            else
            {
                var wrapPropRef = new CodePropertyReferenceExpression(WrappedRef, propertyName);
                ConvertReturnedProperty(method, retType, wrapPropRef);
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
            var retType = info.ReturnType;

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

            var invoke = new CodeMethodInvokeExpression(targetMethodRef, targetMethodArgs.ToArray());
            ConvertReturnedInvoke(method, retType, invoke);
            Klass.Members.Add(method);
        }

        private void ConvertReturnedInvoke(CodeMemberMethod method, Type retType, CodeMethodInvokeExpression invoke)
        {
            if (retType.IsRegMember(RegPairs, out _))
            {
                var decl = new CodeVariableDeclarationStatement(new CodeTypeReference(typeof(int)), Namer.GetName());
                var varRef = new CodeVariableReferenceExpression(decl.Name);
                var assignment = new CodeAssignStatement(varRef, invoke);
                var prop = new CodePropertyReferenceExpression(varRef, "Id");
                method.Statements.Add(decl);
                method.Statements.Add(assignment);
                method.Statements.Add(new CodeMethodReturnStatement(prop));
                method.ReturnType = new CodeTypeReference(typeof(int));
            }
            else
            {
                method.Statements.Add(
                    new CodeMethodReturnStatement(
                        invoke
                    ));
            }
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
