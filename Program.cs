﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynAnalysisSampleConsole
{
    class Program
    {
        private const string ARG_HINT_SINGLE_SOURCE = "/singlesource";

        //private const string RESULT_OUTPUT_DIR_NAME = "Output";

        private const string TAB = "\t";
        private const string INDENT = TAB;

        private const string TEST_SOURCE_PATH = @"";
        private const string TEST_DLL_PATH = @"";

        private static string _executingAsmPath = string.Empty;

        private static readonly string[] _exceptDirs = { @"\obj\", ".designer.cs" };
        private static readonly string[] _exceptNamespacesStartsWith = { "System.", "<global namespace", "Microsoft." };

        private static readonly string[] _loadingAssemblyDirs =
        {
            // ここは見直す必要有り
            @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\"
        };

        static void Main(string[] args)
        {
            Console.WriteLine($"ExecutionDateTime: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
            Console.WriteLine("[parameters]");
            for (int i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"p{i}: {args[i]}");
            }
            Console.WriteLine();

            // EXE実行中のパスを取得
            _executingAsmPath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;

            string _sourceDirPath = null;
            string _dllPath = null;

            bool isSingleSource = false;

            if (args.Length == 0)
            {
                // デバッグ用
                //isSingleSource = true;

                // テスト用
                _sourceDirPath = TEST_SOURCE_PATH;
                _dllPath = TEST_DLL_PATH;
            }
            else if (args.Length < 2)
            {
                Console.Error.WriteLine("使い方①： RoslynAnalysisSampleConsole [ソース格納ディレクトリパス] [bin格納ディレクトリパス]");
                Console.Error.WriteLine($"使い方②： RoslynAnalysisSampleConsole [ソースファイルパス] [bin格納ディレクトリパス] {ARG_HINT_SINGLE_SOURCE}");
                return;
            }
            else
            {
                _sourceDirPath = args[0];
                _dllPath = args[1];
            }

            if (!isSingleSource)
            {
                foreach (string arg in args)
                {
                    if (arg?.ToLower() == ARG_HINT_SINGLE_SOURCE)
                    {
                        isSingleSource = true;
                        break;
                    }
                }
            }

            if (!isSingleSource && !Directory.Exists(_sourceDirPath))
            {
                Console.Error.WriteLine($"指定されたソース格納ディレクトリパスが見つかりません({_sourceDirPath})");
                return;
            }

            if (isSingleSource && !File.Exists(_sourceDirPath))
            {
                Console.Error.WriteLine($"指定されたソースファイルが見つかりません({_sourceDirPath})");
                return;
            }

            if (!Directory.Exists(_dllPath))
            {
                Console.Error.WriteLine($"指定されたbin格納ディレクトリパスが見つかりません({_sourceDirPath})");
                return;
            }

            try
            {
                List<PortableExecutableReference> asmList = new List<PortableExecutableReference>();

                asmList.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                foreach (string path in Directory.GetFiles(_dllPath)
                    .Where(p => p.EndsWith(".dll") || p.EndsWith(".exe")))
                {
                    var asm = MetadataReference.CreateFromFile(path);
                    asmList.Add(asm);
                }

                foreach (string path in _loadingAssemblyDirs)
                {
                    foreach (string asmPath in Directory.GetFiles(path)
                        .Where(p => p.EndsWith(".dll") || p.EndsWith(".exe")))
                    {
                        var asm = MetadataReference.CreateFromFile(asmPath);
                        asmList.Add(asm);
                    }
                }

                if (isSingleSource)
                {
                    SyntaxTree tree;
                    using (TextReader reader = new StreamReader(_sourceDirPath))
                    {
                        tree = CSharpSyntaxTree.ParseText(reader.ReadToEnd());
                    }

                    Dictionary<string, SyntaxTree> dic = new Dictionary<string, SyntaxTree> {{_sourceDirPath, tree}};
                    WriteSingleFileAnalyzeResult(_sourceDirPath, asmList, dic);
                }
                else
                {
                    var sourceFilePathList =
                        from s in Directory.GetFiles(_sourceDirPath, "*.cs", SearchOption.AllDirectories)
                        where !_exceptDirs.Any(s2 => s.ToLower().Contains(s2))
                        select s;

                    Dictionary<string, SyntaxTree> treeDic = new Dictionary<string, SyntaxTree>();

                    var filePathList = sourceFilePathList as string[] ?? sourceFilePathList.ToArray();
                    foreach (string tmpPath in filePathList)
                    {
                        using (TextReader reader = new StreamReader(tmpPath))
                        {
                            treeDic.Add(tmpPath, CSharpSyntaxTree.ParseText(reader.ReadToEnd()));
                        }
                    }

                    foreach (string sourcePath in filePathList)
                    {
                        WriteSingleFileAnalyzeResult(sourcePath, asmList, treeDic);
                    }
                }

                // 最後に空行入れる
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                WriteExceptionLog(ex);
                Environment.Exit(-1);
            }
        }

        /// <summary>
        /// 一つのソースファイルを解析
        /// </summary>
        /// <param name="sourceFilePath">ソースファイルパス</param>
        /// <param name="assemblyList">アセンブリリスト(名前空間・クラス名解決に使用)</param>
        /// <param name="indent">インデント(省略可能)</param>
        private static void WriteSingleFileAnalyzeResult(string sourceFilePath, IEnumerable<PortableExecutableReference> assemblyList,
            Dictionary<string, SyntaxTree> syntaxTreeDic, string indent = null)
        {
            try
            {
                Console.WriteLine($"[Source: {sourceFilePath}]");
                
                SyntaxTree target = syntaxTreeDic[sourceFilePath];

                var compilation = CSharpCompilation.Create("TypeInfo", syntaxTreeDic.Values, assemblyList);
                var model = compilation.GetSemanticModel(target);

                var root = target.GetRoot();
                var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (ClassDeclarationSyntax cls in classes)
                {
                    WriteClassDeclaration(cls, model);
                }
            }
            catch (Exception ex)
            {
                List<string> errorReport = new List<string>();
                errorReport.Add($"AnalyzingSourceFile: {sourceFilePath}");

                WriteExceptionLog(new ApplicationException("[" + string.Join(",", errorReport) + "]", ex));
            }
        }

        /// <summary>
        /// 一つのクラスを解析
        /// </summary>
        /// <param name="cls">クラス定義</param>
        /// <param name="model">セマンティックモデル</param>
        /// <param name="indent">インデント(省略可能)</param>
        private static void WriteClassDeclaration(ClassDeclarationSyntax cls, SemanticModel model, string indent = null)
        {
            string nameSpace = string.Empty;
            string className = string.Empty;

            ClassDeclarationSyntax tmp = cls;
            while (tmp.Parent is ClassDeclarationSyntax syntax1)
            {
                className = tmp.Identifier.Text;
                nameSpace = string.IsNullOrEmpty(nameSpace) ? className : className + "." + nameSpace;

                tmp = syntax1;
            }

            if (cls.Parent is NamespaceDeclarationSyntax syntax2)
            {
                nameSpace = syntax2.Name.ToString() + nameSpace;
            }
            else
            {
                nameSpace = "(名前空間無し)";
            }

            try
            {
                Console.WriteLine((indent ?? string.Empty) + "Class Declaration: {0}.{1}", nameSpace, cls.Identifier.Text);

                var constructorBlocks = cls.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
                foreach (ConstructorDeclarationSyntax constructorBlock in constructorBlocks)
                {
                    WriteConstructorDeclaration(constructorBlock, model, nameSpace, cls.Identifier.Text,
                        (indent ?? string.Empty) + INDENT);
                }

                var methodBlocks = cls.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (MethodDeclarationSyntax methodBlock in methodBlocks)
                {
                    WriteMethodDeclaration(methodBlock, model, nameSpace, cls.Identifier.Text,
                        (indent ?? string.Empty) + INDENT);
                }
            }
            catch (Exception ex)
            {
                List<string> errorReport = new List<string>();
                errorReport.Add($"AnalyzingNamespace: {nameSpace}");
                errorReport.Add($"AnalyzingClass: {className}");

                WriteExceptionLog(new ApplicationException("[" + string.Join(",", errorReport) + "]", ex));
            }
        }

        /// <summary>
        /// 一つのコンストラクタ定義を解析
        /// </summary>
        /// <param name="constructorBlock">コンストラクタ定義</param>
        /// <param name="model">セマンティックモデル</param>
        /// <param name="nameSpace">呼び出し元クラスの属する名前空間</param>
        /// <param name="className">呼び出し元クラス名</param>
        /// <param name="indent">インデント(省略可能)</param>
        private static void WriteConstructorDeclaration(ConstructorDeclarationSyntax constructorBlock, SemanticModel model,
            string nameSpace, string className, string indent = null)
        {
            StringBuilder str = new StringBuilder();

            try
            {
                str.Append("Constructor Declaration: ");
                str.Append("\t");
                str.Append(nameSpace + "." + className);
                str.Append("\t");
                str.Append(constructorBlock.Identifier.Text);

                List<string> paramTypeStrings = new List<string>();
                foreach (var item in constructorBlock.ParameterList.Parameters)
                {
                    paramTypeStrings.Add(model.GetTypeInfo(item.Type).Type.Name);
                }

                str.Append($"({string.Join(",", paramTypeStrings)})");
                str.Append("\t");

                string modifier = constructorBlock.Modifiers.ToString();
                str.Append(modifier);
                str.Append("\t");

                int lineCount = 0;
                using (StringReader sReader = new StringReader(constructorBlock.WithoutTrivia().ToFullString()))
                {
                    string line;
                    while ((line = sReader.ReadLine()) != null)
                    {
                        if (line != string.Empty &&
                            !string.IsNullOrWhiteSpace(line) &&
                            !line.Trim().StartsWith("//"))
                        {
                            lineCount++;
                        }
                    }
                }

                str.Append(lineCount);
                Console.WriteLine((indent ?? string.Empty) + str);

                WriteCallingMethods(constructorBlock, model, nameSpace, className, (indent ?? string.Empty) + INDENT);
            }
            catch (Exception ex)
            {
                List<string> errorReport = new List<string>();
                errorReport.Add($"AnalyzingNamespace: {nameSpace}");
                errorReport.Add($"AnalyzingClass: {className}");
                errorReport.Add($"AnalyzingMethod: {constructorBlock.Identifier.Text}");

                WriteExceptionLog(new ApplicationException("[" + string.Join(",", errorReport) + "]", ex));
            }
        }

        /// <summary>
        /// 一つのメソッド定義を解析
        /// </summary>
        /// <param name="methodBlock">メソッド定義</param>
        /// <param name="model">セマンティックモデル</param>
        /// <param name="nameSpace">呼び出し元クラスの属する名前空間</param>
        /// <param name="className">呼び出し元クラス名</param>
        /// <param name="indent">インデント(省略可能)</param>
        private static void WriteMethodDeclaration(MethodDeclarationSyntax methodBlock, SemanticModel model,
            string nameSpace, string className, string indent = null)
        {
            StringBuilder str = new StringBuilder();

            try
            {
                var symbol = model.GetDeclaredSymbol(methodBlock);

                str.Append("Method Declaration: ");
                str.Append("\t");

                if (symbol != null)
                {
                    str.Append(symbol.ContainingType);
                }
                else
                {
                    str.Append(nameSpace + "." + className);
                }
                
                str.Append("\t");
                str.Append(methodBlock.Identifier.Text);

                str.Append(methodBlock.ParameterList);
                str.Append("\t");

                string modifier = methodBlock.Modifiers.ToString();
                str.Append(modifier);
                str.Append("\t");

                int lineCount = 0;
                using (StringReader sReader = new StringReader(methodBlock.WithoutTrivia().ToFullString()))
                {
                    string line;
                    while ((line = sReader.ReadLine()) != null)
                    {
                        if (line != string.Empty &&
                            !string.IsNullOrWhiteSpace(line) &&
                            !line.Trim().StartsWith("//"))
                        {
                            lineCount++;
                        }
                    }
                }

                str.Append(lineCount);
                Console.WriteLine((indent ?? string.Empty) + str);

                WriteCallingMethods(methodBlock, model, nameSpace, className, (indent ?? string.Empty) + INDENT);
            }
            catch (Exception ex)
            {
                List<string> errorReport = new List<string>();
                errorReport.Add($"AnalyzingNamespace: {nameSpace}");
                errorReport.Add($"AnalyzingClass: {className}");
                errorReport.Add($"AnalyzingMethod: {methodBlock.Identifier.Text}");

                WriteExceptionLog(new ApplicationException("[" + string.Join(",", errorReport) + "]", ex));
            }
        }

        public static void WriteCallingMethods(MethodDeclarationSyntax methodBlock, SemanticModel model,
            string thisNameSpace, string thisClassName, string indent = null)
        {
            var symbol = model.GetDeclaredSymbol(methodBlock);

            var calling = methodBlock.DescendantNodes().OfType<InvocationExpressionSyntax>();
            WriteCallingMethods(calling, model, thisNameSpace, thisClassName, indent);
        }

        public static void WriteCallingMethods(ConstructorDeclarationSyntax constructorBlock, SemanticModel model,
            string thisNameSpace, string thisClassName, string indent = null)
        {
            var calling = constructorBlock.DescendantNodes().OfType<InvocationExpressionSyntax>();
            WriteCallingMethods(calling, model, thisNameSpace, thisClassName, indent);
        }

        /// <summary>
        /// 一つのメソッド呼び出し結果を出力
        /// </summary>
        /// <param name="methodBlock">呼び出し元のメソッド定義</param>
        /// <param name="model">セマンティックモデル</param>
        /// <param name="thisNameSpace">呼び出し元クラスの属する名前空間</param>
        /// <param name="thisClassName">呼び出し元クラス名</param>
        /// <param name="indent">インデント(省略可能)</param>
        private static void WriteCallingMethods(IEnumerable<InvocationExpressionSyntax> calling, SemanticModel model, string thisNameSpace, string thisClassName, string indent = null)
        {
            foreach (InvocationExpressionSyntax invoke in calling)
            {
                string nameSpace = null;
                string typeName = null;
                string callingMethodName = null;

                try
                {
                    var symbol1 = model.GetSymbolInfo(invoke).Symbol;

                    if (symbol1 != null)
                    {
                        nameSpace = symbol1.ContainingNamespace.ToString();

                        if (nameSpace == "System")
                        {
                            continue;
                        }

                        if (_exceptNamespacesStartsWith.Any(s => nameSpace.StartsWith(s)))
                        {
                            continue;
                        }
                        
                        Console.WriteLine((indent ?? string.Empty) + "Specified Syntax MethodCall: " + symbol1.OriginalDefinition);
                    }
                    else
                    {
                        if (invoke.Expression is IdentifierNameSyntax syn1)
                        {
                            var v = model.GetSymbolInfo(syn1).Symbol;

                            nameSpace = thisNameSpace;
                            typeName = thisClassName;
                            callingMethodName = syn1.ToString();
                        }
                        else if (invoke.Expression is MemberAccessExpressionSyntax syn2)
                        {
                            var v = model.GetSymbolInfo(syn2).Symbol;

                            nameSpace = model.GetTypeInfo(syn2.Expression).Type.ContainingNamespace.ToString();
                            typeName = model.GetTypeInfo(syn2.Expression).Type.Name;
                            callingMethodName = syn2.Name.ToString();
                        }
                        else
                        {
                            // ここに入ってくるパターンがあるかどうか・・・
                        }

                        List<string> argumentTypesList = new List<string>();
                        var symbols = invoke.ArgumentList.Arguments.Select(s => s.Expression);

                        foreach (var ar in symbols)
                        {
                            if (ar.IsKind(SyntaxKind.NullLiteralExpression))
                            {
                                argumentTypesList.Add("*");
                            }
                            else
                            {
                                var info = model.GetTypeInfo(ar);
                                argumentTypesList.Add(info.ConvertedType?.ToString() ?? "?");
                            }
                        }

                        if (nameSpace == "System")
                        {
                            continue;
                        }

                        if (_exceptNamespacesStartsWith.Any(s => nameSpace.StartsWith(s)))
                        {
                            continue;
                        }

                        string argumentTypes = "(" + string.Join(", ", argumentTypesList) + ")";

                        Console.WriteLine((indent ?? string.Empty) + "Non-specified Syntax MethodCall: " + nameSpace + "." + typeName + "." +
                                          callingMethodName + argumentTypes);
                    }
                }
                catch (Exception ex)
                {
                    List<string> errorReport = new List<string>();
                    errorReport.Add($"AnalyzingNamespace: {nameSpace}");
                    errorReport.Add($"AnalyzingClass: {typeName}");
                    errorReport.Add($"AnalyzingMethod: {callingMethodName}");

                    throw new ApplicationException("[" + string.Join(",", errorReport) + "]", ex);
                }
            }
        }

        private static void WriteExceptionLog(Exception ex)
        {
            DateTime now = DateTime.Now;

            List<string> errorReport = new List<string>();
            errorReport.Add($"出力日付： {now:yyyy/MM/dd}");
            errorReport.Add($"出力時刻： {now:HH:mm:ss}");
            errorReport.Add("詳細情報：");

            errorReport.Add(ex.ToString());

            Exception current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
                errorReport.Add(current.ToString());
                errorReport.Add(current.Message);
                errorReport.Add(current.StackTrace);
            }

            Console.Error.WriteLine(string.Join(Environment.NewLine, errorReport));

            string logOutputPath = Path.Combine(_executingAsmPath, "Exception.log");
            using (StreamWriter writer = new StreamWriter(logOutputPath, true, Encoding.UTF8))
            {
                writer.WriteLine("========================================");
                writer.WriteLine(string.Join(Environment.NewLine, errorReport));
                writer.WriteLine();
            }
        }
    }
}
