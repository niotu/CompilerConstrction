using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.CodeGen
{
    public class CodeGenerator
    {
        private readonly AssemblyBuilder _assemblyBuilder;
        private readonly ModuleBuilder _moduleBuilder;
        private readonly TypeMapper _typeMapper;
        private readonly Dictionary<string, TypeBuilder> _typeBuilders;
        private readonly ClassHierarchy _hierarchy;

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _hierarchy = hierarchy;
            
            // Создаем динамическую сборку
            var assemblyNameObj = new AssemblyName(assemblyName);
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyNameObj, 
                AssemblyBuilderAccess.RunAndCollect); // Для выполнения в памяти
            
            // Создаем модуль
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
            
            _typeMapper = new TypeMapper(_moduleBuilder);
            _typeBuilders = new Dictionary<string, TypeBuilder>();
        }

        public Assembly Generate(ProgramNode program)
        {
            // Фаза 1: Объявление всех типов
            foreach (var classDecl in program.Classes)
            {
                DeclareType(classDecl);
            }

            // Фаза 2: Генерация тел методов
            foreach (var classDecl in program.Classes)
            {
                GenerateClassMembers(classDecl);
            }

            // Фаза 3: Завершение типов
            foreach (var typeBuilder in _typeBuilders.Values)
            {
                typeBuilder.CreateType();
            }

            return _assemblyBuilder;
        }

        private void DeclareType(ClassDeclaration classDecl)
        {
            // Определяем базовый тип
            Type? baseType = null;
            if (!string.IsNullOrEmpty(classDecl.Extension))
            {
                baseType = _typeMapper.GetNetType(classDecl.Extension);
            }
            else
            {
                baseType = typeof(object); // По умолчанию наследуемся от object
            }

            // Создаем TypeBuilder
            var typeBuilder = _moduleBuilder.DefineType(
                classDecl.Name,
                TypeAttributes.Public | TypeAttributes.Class,
                baseType
            );

            _typeBuilders[classDecl.Name] = typeBuilder;
            _typeMapper.RegisterType(classDecl.Name, typeBuilder);
        }

        private void GenerateClassMembers(ClassDeclaration classDecl)
        {
            var typeBuilder = _typeBuilders[classDecl.Name];
            var methodGen = new MethodGenerator(_typeMapper, _hierarchy);

            // Генерация полей
            foreach (var member in classDecl.Members)
            {
                if (member is VariableDeclaration varDecl)
                {
                    GenerateField(typeBuilder, varDecl);
                }
            }

            // Генерация конструкторов
            foreach (var member in classDecl.Members)
            {
                if (member is ConstructorDeclaration ctorDecl)
                {
                    methodGen.GenerateConstructor(typeBuilder, ctorDecl, classDecl);
                }
            }

            // Генерация методов
            foreach (var member in classDecl.Members)
            {
                if (member is MethodDeclaration methodDecl)
                {
                    methodGen.GenerateMethod(typeBuilder, methodDecl);
                }
            }
        }

        private void GenerateField(TypeBuilder typeBuilder, VariableDeclaration varDecl)
        {
            Type fieldType = _typeMapper.InferFieldType(varDecl.Expression);
            
            var fieldBuilder = typeBuilder.DefineField(
                varDecl.Identifier,
                fieldType,
                FieldAttributes.Private
            );
        }

        public void SaveToFile(string outputPath)
        {
            // Для .NET Core/.NET 5+ нужен другой подход
            // Используйте PersistedAssemblyBuilder (доступен в .NET 9+)
            // Или сохраняйте через MetadataLoadContext
            throw new NotImplementedException("Save to file requires .NET 9+ or MetadataLoadContext");
        }
    }
}
