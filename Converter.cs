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
        private bool AddedEvents { get; set; }

        private CompilerParameters CompilerParams { get; } = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false };
        private Action<string> Logger { get; }

        public Converter(List<RegistryType> regTypes, Type wrapped, Action<string> logger = null)
            : base(wrapped.GetSvcName(), wrapped.GetSvcNamespace())
        {
            this.RegPairs = regTypes;
            this.Wrapped = wrapped;

            Logger = logger == null
                ? s => Console.WriteLine(s)
                : logger;

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
                var compilerResults = provider.CompileAssemblyFromDom(CompilerParams, Unit);
                if (compilerResults.Errors.HasErrors)
                {
                    foreach (var compilerError in compilerResults.Errors)
                    {
                        var error = compilerError.ToString();
                        Logger(error);
                    }

                    throw new InvalidOperationException($"Service compilation failed with {compilerResults.Errors.Count} errors");
                }

                Logger("Service compiled successfully");
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
                    if (parameter.IsOut)
                    {
                        targetMethodArgs.Add(new CodeSnippetExpression("out _"));
                        continue;
                    }

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
                var decl = new CodeVariableDeclarationStatement(new CodeTypeReference(retType), Namer.GetName());
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

        private void AddVoidActionEvent(CodeMemberEvent newEvent, CodeMemberMethod method)
        {
            // Set the return type of the event
            newEvent.Type = new CodeTypeReference("System.EventHandler");

            // Add the code to invoke the event from the method
            // when the event in the wrapped object fires

            CodeEventReferenceExpression newEventRef = new CodeEventReferenceExpression(
              new CodeThisReferenceExpression(), newEvent.Name);

            var invoke = new CodeDelegateInvokeExpression(newEventRef,
                new CodeExpression[] { new CodeSnippetExpression("null"), new CodeSnippetExpression("null") });
            method.Statements.Add(invoke);
        }

        private void AddActionEventWithRetVal(CodeMemberEvent newEvent, Type eventType, CodeMemberMethod method)
        {
            // Set the return type of the event
            newEvent.Type = new CodeTypeReference("System.EventHandler",
              new CodeTypeReference[] { new CodeTypeReference(eventType) });

            // Add the code to invoke the event from the method
            // when the event in the wrapped object fires

            // Add event parameters to the method

            var obj = new CodeParameterDeclarationExpression(eventType, "e");
            method.Parameters.Add(obj);

            CodeEventReferenceExpression newEventRef = new CodeEventReferenceExpression(
              new CodeThisReferenceExpression(), newEvent.Name);

            // Invoke the event with the parameters from the wrapped object event
            CodeArgumentReferenceExpression eventArg = new CodeArgumentReferenceExpression("e");

            var invoke = new CodeDelegateInvokeExpression(newEventRef,
                    new CodeExpression[] { new CodeSnippetExpression("null"), eventArg });

            method.Statements.Add(invoke);
        }

        private void Converter_X(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private CodeMemberEvent CreateEvent(string name)
        {
            var newEvent = new CodeMemberEvent();
            newEvent.Name = name;
            newEvent.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            return newEvent;
        }

        public Converter WithEvents()
        {
            // TODO:
            if (AddedEvents || Wrapped.IsRegMember(RegPairs, out _))
                return this;

            foreach (var e in Wrapped.GetEvents())
            {
                var eventName = e.Name;
                var eventType = e.EventHandlerType
                  .GetGenericArguments()
                  .FirstOrDefault();

                // Creates an Action event that doesn't require ActionProxy when subscribing
                var newEvent = CreateEvent(eventName);

                // Create method a which gets called when the Action event fires, and forwards the event to the normal C# event
                string methodName = "Forward" + eventName + "Event";
                CodeMemberMethod newMethod = CodeDomEx.CreateMethod(methodName, typeof(void));

                if (eventType == null)
                    AddVoidActionEvent(newEvent, newMethod);
                else
                    AddActionEventWithRetVal(newEvent, eventType, newMethod);

                Klass.Members.Add(newEvent);
                Klass.Members.Add(newMethod);

                // Add statements to the constructor to invoke the new event when the event in the
                // wrapped object fires.

                var actionProxyType = eventType == null
                  ? "SuperMemoAssistant.Sys.Remoting.ActionProxy"
                  : $"SuperMemoAssistant.Sys.Remoting.ActionProxy<{eventType.FullName}>";

                CodeDelegateCreateExpression createDelegate1 = new CodeDelegateCreateExpression(
                new CodeTypeReference(actionProxyType), new CodeThisReferenceExpression(), methodName);

                CodeEventReferenceExpression actionEventRef = new CodeEventReferenceExpression(
                  WrappedRef, eventName);

                // Attaches an EventHandler delegate pointing to TestMethod to the TestEvent event.
                CodeAttachEventStatement attachStatement1 = new CodeAttachEventStatement(actionEventRef, createDelegate1);

                Constructor.Statements.Add(attachStatement1);

            }
            AddedEvents = true;
            return this;
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
