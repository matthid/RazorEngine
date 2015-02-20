#if RAZOR4
using Microsoft.AspNet.Razor.Parser;
#else
using System.Web.Razor.Parser;
#endif
using RazorEngine.Compilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if !RAZOR4
using System.CodeDom.Compiler;
using System.Web.Razor;
using System.IO;
using System.Globalization;
#endif
using RazorEngine.Compilation.CSharp;
using RazorEngine.Compilation.ReferenceResolver;
using Roslyn.Compilers.Common;
using Roslyn.Compilers.CSharp;
using Roslyn.Compilers;

namespace RazorEngine.CtpRoslyn.CSharp
{
    /// <summary>
    /// A concrete <see cref="ICompilerService"/> implementation for C# by using the Roslyn compiler.
    /// </summary>
    public class CSharpRoslynCompilerService : RoslynCompilerServiceBase
    {
#if !RAZOR4
        /// <summary>
        /// We need a CodeDom instance as pre Razor4 uses CodeDom 
        /// internally and we need to generate the source code file...
        /// </summary>
        private Microsoft.CSharp.CSharpCodeProvider _codeDomProvider; 
#endif
        public CSharpRoslynCompilerService(bool strictMode = true, Func<ParserBase> markupParserFactory = null)
            : base(
                new RazorEngine.Compilation.CSharp.CSharpRazorCodeLanguage(strictMode),
                markupParserFactory) {
#if !RAZOR4
            _codeDomProvider = new Microsoft.CSharp.CSharpCodeProvider(); 
#endif
        }

        /// <summary>
        /// Returns "cs".
        /// </summary>
        public override string SourceFileExtension
        {
            get { return "cs"; }
        }

#if !RAZOR4
        /// <summary>
        /// Inspects the GeneratorResults and returns the source code.
        /// </summary>
        /// <param name="results"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override string InspectSource(GeneratorResults results, TypeContext context)
        {
            string generatedCode;
            var builder = new StringBuilder();
            using (var writer = new StringWriter(builder, CultureInfo.InvariantCulture))
            {
                _codeDomProvider.GenerateCodeFromCompileUnit(results.GeneratedCode, writer, new CodeGeneratorOptions());
                generatedCode = builder.ToString();
            }
            return generatedCode;
        }
#endif

        /// <summary>
        /// Build a C# typename.
        /// </summary>
        /// <param name="templateType"></param>
        /// <param name="modelType"></param>
        /// <returns></returns>
        public override string BuildTypeName(Type templateType, Type modelType)
        {
            if (templateType == null)
                throw new ArgumentNullException("templateType");

            var modelTypeName = CompilerServicesUtility.ResolveCSharpTypeName(modelType);
            return CompilerServicesUtility.CSharpCreateGenericType(templateType, modelTypeName, false);
        }

        /// <summary>
        /// Creates a CSharpSyntaxTree instance.
        /// </summary>
        /// <param name="sourceCode"></param>
        /// <param name="sourceCodePath"></param>
        /// <returns></returns>
        public override CommonSyntaxTree GetSyntaxTree(string sourceCode, string sourceCodePath)
        {
            return SyntaxTree.ParseText(sourceCode, path: sourceCodePath);
        }

        /// <summary>
        /// Creates a CSharpCompilation instance
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        public override CommonCompilation GetEmptyCompilation(string assemblyName)
        {
            return Roslyn.Compilers.CSharp.Compilation.Create(assemblyName);
        }
        /// <summary>
        /// Creates a CSharpCompilationOptions instance.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override CommonCompilationOptions CreateOptions(TypeContext context)
        {
            return 
                new CompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithUsings(context.Namespaces);
        }

        /// <summary>
        /// Returns a set of assemblies that must be referenced by the compiled template.
        /// </summary>
        /// <returns>The set of assemblies.</returns>
        public override IEnumerable<CompilerReference> IncludeReferences()
        {
            // Ensure the Microsoft.CSharp assembly is referenced to support dynamic typing.
            return new[] { CompilerReference.From(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly) };
        }
    }
}
