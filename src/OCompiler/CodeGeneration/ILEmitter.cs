using System;
using System.Collections.Generic;
using System.Text;
using OCompiler.Parser;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Генерирует текстовое представление IL кода в формате .il (MSIL)
    /// </summary>
    public class ILEmitter
    {
        private StringBuilder _ilCode;
        private int _indentLevel;

        public ILEmitter()
        {
            _ilCode = new StringBuilder();
            _indentLevel = 0;
        }

        private void Emit(string format, params object[] args)
        {
            string indent = new string(' ', _indentLevel * 2);
            _ilCode.AppendLine(indent + string.Format(format, args));
        }

        public void EmitAssemblyDirective(string assemblyName)
        {
            Emit(".assembly {0} {{}}", assemblyName);
            Emit(".assembly extern mscorlib {{}}");
            Emit("");
        }

        public void EmitNamespace(string namespaceName)
        {
            Emit(".namespace {0}", namespaceName);
            Emit("{{");
            _indentLevel++;
        }

        public void EmitNamespaceEnd()
        {
            _indentLevel--;
            Emit("}}");
        }

        public void EmitClassStart(string className, string baseClass = "object")
        {
            Emit(".class public {0} extends [{1}]{2}", className, "mscorlib", baseClass);
            Emit("{{");
            _indentLevel++;
        }

        public void EmitClassEnd()
        {
            _indentLevel--;
            Emit("}}");
        }

        public void EmitFieldDeclaration(string fieldType, string fieldName, bool isPrivate = true)
        {
            string access = isPrivate ? "private" : "public";
            Emit(".field {0} {1} {2}", access, fieldType, fieldName);
        }

        public void EmitMethodStart(string methodName, string returnType, string parameters = "")
        {
            Emit(".method public {0} {1}({2}) cil managed", returnType, methodName, parameters);
            Emit("{{");
            _indentLevel++;
        }

        public void EmitMethodEnd()
        {
            _indentLevel--;
            Emit("}}");
        }

        public void EmitConstructorStart(string parameters = "")
        {
            Emit(".method public specialname rtspecialname instance void .ctor({0}) cil managed", parameters);
            Emit("{{");
            _indentLevel++;
            Emit(".maxstack 8");
        }

        public void EmitConstructorEnd()
        {
            Emit("ret");
            _indentLevel--;
            Emit("}}");
        }

        public void EmitInstruction(string instruction, params object[] args)
        {
            Emit(instruction, args);
        }

        public void EmitLoadConstant(int value)
        {
            Emit("ldc.i4 {0}", value);
        }

        public void EmitLoadString(string value)
        {
            Emit("ldstr \"{0}\"", value.Replace("\"", "\\\""));
        }

        public void EmitLoadLocal(int index)
        {
            Emit("ldloc.{0}", index);
        }

        public void EmitStoreLocal(int index)
        {
            Emit("stloc.{0}", index);
        }

        public void EmitLoadArg(int index)
        {
            Emit("ldarg.{0}", index);
        }

        public void EmitCall(string methodName, string className = "System.Console")
        {
            Emit("call void [{0}]{1}::{2}(string)", "mscorlib", className, methodName);
        }

        public void EmitReturn()
        {
            Emit("ret");
        }

        public string GetILCode()
        {
            return _ilCode.ToString();
        }

        public void Reset()
        {
            _ilCode.Clear();
            _indentLevel = 0;
        }
    }
}
