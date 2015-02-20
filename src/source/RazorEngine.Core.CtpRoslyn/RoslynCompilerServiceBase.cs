﻿using RazorEngine.Compilation;
using RazorEngine.Compilation.ReferenceResolver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Roslyn;
using Roslyn.Compilers;
using Roslyn.Utilities;
#if RAZOR4
using Microsoft.AspNet.Razor.Parser;
using Microsoft.AspNet.Razor;
#else
using System.Web.Razor.Parser;
using System.Web.Razor;
#endif
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using Roslyn.Compilers.Common;
using System.Reflection.Emit;


namespace RazorEngine.CtpRoslyn
{
    /// <summary>
    /// Base compiler service class for roslyn compilers
    /// </summary>
    public abstract class RoslynCompilerServiceBase : CompilerServiceBase
    {
        private class SelectMetadataReference : CompilerReference.ICompilerReferenceVisitor<MetadataReference>
        {
            public MetadataReference Visit(System.Reflection.Assembly assembly)
            {
                if (assembly.Location != null)
                {
                    return new MetadataFileReference(assembly.Location);
                }
                return MetadataReference.CreateAssemblyReference(assembly.GetName().FullName);
            }

            public MetadataReference Visit(string file)
            {
                return new MetadataFileReference(file);
            }

            public MetadataReference Visit(System.IO.Stream stream)
            {
                throw new NotImplementedException();
            }

            public MetadataReference Visit(byte[] byteArray)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="RoslynCompilerServiceBase"/> class.
        /// </summary>
        /// <param name="codeLanguage"></param>
        /// <param name="markupParserFactory"></param>
        public RoslynCompilerServiceBase(RazorCodeLanguage codeLanguage, Func<ParserBase> markupParserFactory)
            : base(codeLanguage, new ParserBaseCreator(markupParserFactory))
        {

        }

        /// <summary>
        /// Get a new empty compilation instance from the concrete implementation.
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        public abstract global::Roslyn.Compilers.Common.CommonCompilation GetEmptyCompilation(string assemblyName);

        /// <summary>
        /// Gets a SyntaxTree from the given source code.
        /// </summary>
        /// <param name="sourceCode"></param>
        /// <param name="sourceCodeFile"></param>
        /// <returns></returns>
        public abstract CommonSyntaxTree GetSyntaxTree(string sourceCode, string sourceCodeFile);

        /// <summary>
        /// Create a empty CompilationOptions with the given namespace usings.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public abstract CommonCompilationOptions CreateOptions(TypeContext context);

        /// <summary>
        /// Check for mono runtime as Roslyn doesn't support generating debug symbols on mono/unix
        /// </summary>
        /// <returns></returns>
        private static bool IsMono()
        {
            return Type.GetType("Mono.Runtime") != null;
        }

        /// <summary>
        /// Configures and runs the compiler.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Tuple<Type, CompilationData> CompileType(TypeContext context)
        {
            var sourceCode = GetCodeCompileUnit(context);
            var assemblyName = GetAssemblyName(context);

            (new PermissionSet(PermissionState.Unrestricted)).Assert();
            var tempDir = GetTemporaryDirectory();

            var sourceCodeFile = Path.Combine(tempDir, String.Format("{0}.{1}", assemblyName, SourceFileExtension));
            File.WriteAllText(sourceCodeFile, sourceCode);
            
            var references = GetAllReferences(context);

            var compilation =
                GetEmptyCompilation(assemblyName)
                .AddSyntaxTrees(
                    GetSyntaxTree(sourceCode, sourceCodeFile))
                .AddReferences(references.Select(reference => reference.Visit(new SelectMetadataReference())));

            compilation =
                compilation
                .UpdateOptions(
                    CreateOptions(context)
                    .WithOutputKind(OutputKind.DynamicallyLinkedLibrary));
                    //.WithPlatform(Platform.AnyCpu)
                    //.WithSourceReferenceResolver(new RazorEngineSourceReferenceResolver(sourceCodeFile)));

            var assemblyBuilder = AppDomain.CurrentDomain
                .DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.RunAndCollect);
            var moduleBuilder = 
                assemblyBuilder
                .DefineDynamicModule(assemblyName);

            var result = compilation.Emit(moduleBuilder);

            var assemblyFile = Path.Combine(tempDir, String.Format("{0}.dll", assemblyName));
            //var assemblyPdbFile = Path.Combine(tempDir, String.Format("{0}.pdb", assemblyName));
            var compilationData = new CompilationData(sourceCode, tempDir);
            
            if (!result.Success)
            {
                var errors =
                    result.Diagnostics.Select(diag =>
                    {
                        var lineSpan = diag.Location.GetLineSpan(true);
                        return new Templating.RazorEngineCompilerError(
                            string.Format("{0}", diag.Info.GetMessage()),
                            lineSpan.Path, 
                            lineSpan.StartLinePosition.Line, 
                            lineSpan.StartLinePosition.Character, 
                            diag.Info.MessageIdentifier, 
                            diag.Info.Severity != DiagnosticSeverity.Error); ;
                    });
                        
                throw new Templating.TemplateCompilationException(errors, compilationData, context.TemplateContent);
            }
            if (result.IsUncollectible) throw new InvalidOperationException("expected collectible assembly!");
            
            // load file and return loaded type.
            //var assembly = Assembly.LoadFrom(assemblyFile);
            assemblyBuilder.Save(assemblyFile);
            var type = moduleBuilder.GetType(DynamicTemplateNamespace + "." + context.ClassName);
            return Tuple.Create(type, compilationData);
        }
    }
}
