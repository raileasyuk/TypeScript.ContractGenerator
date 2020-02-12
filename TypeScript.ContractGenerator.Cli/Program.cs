using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using CommandLine;

using SkbKontur.TypeScript.ContractGenerator.CodeDom;

namespace SkbKontur.TypeScript.ContractGenerator.Cli
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
                {
                    GenerateByOptions(o);

                    AddFileChangedHandler(o.Assembly, Debounce((source, e) => GenerateByOptions(o), 1000));

                    Console.WriteLine("Press 'q' to quit the sample.");
                    while (Console.Read() != 'q') ;
                });
        }

        private static FileSystemEventHandler Debounce(FileSystemEventHandler func, int milliseconds = 1000)
        {
            CancellationTokenSource? cancelTokenSource = null;

            return (arg1, arg2) =>
                {
                    cancelTokenSource?.Cancel();
                    cancelTokenSource = new CancellationTokenSource();

                    Task.Delay(milliseconds, cancelTokenSource.Token)
                        .ContinueWith(t =>
                            {
                                if (t.IsCompletedSuccessfully)
                                {
                                    func(arg1, arg2);
                                }
                            }, TaskScheduler.Default);
                };
        }

        private static void AddFileChangedHandler(string pathToAssembly, FileSystemEventHandler handler)
        {
            using var watcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(pathToAssembly),
                    NotifyFilter = NotifyFilters.LastAccess
                                   | NotifyFilters.LastWrite
                                   | NotifyFilters.FileName
                                   | NotifyFilters.DirectoryName,
                    Filter = "*"
                };

            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;

            watcher.EnableRaisingEvents = true;

            Console.WriteLine("Press 'q' to quit the sample.");
            while (Console.Read() != 'q') ;
        }

        private static void GenerateByOptions(Options o)
        {
            Console.WriteLine("ReGenerate");

            WeakReference testAlcWeakRef;
            ExecuteAndUnload(o.Assembly, out testAlcWeakRef, o);

            // это нужно для того чтобы сборка из AssemblyLoadContext была выгруженна
            // https://docs.microsoft.com/en-us/dotnet/standard/assembly/unloadability?view=netcore-3.1
            for (var i = 0; testAlcWeakRef.IsAlive && i < 10; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                // здесь может случиться deadlock
                // https://github.com/dotnet/runtime/issues/535
                GC.WaitForPendingFinalizers();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExecuteAndUnload(string assemblyPath, out WeakReference alcWeakRef, Options o)
        {
            var alc = new DirectoryAssemblyLoadContext(assemblyPath);
            var targetAssembly = alc.LoadFromAssemblyPath(assemblyPath);

            alcWeakRef = new WeakReference(alc, true);

            var customTypeGenerator = GetSingleImplementation<ICustomTypeGenerator>(targetAssembly);
            var typesProvider = GetSingleImplementation<ITypesProvider>(targetAssembly);
            if (customTypeGenerator == null || typesProvider == null)
                return;

            var options = BuildTypeScriptGenerationOptionsByOption(o);

            var typeGenerator = new TypeScriptGenerator(options, customTypeGenerator, typesProvider);
            typeGenerator.GenerateFiles(o.OutputDirectory, JavaScriptTypeChecker.TypeScript);

            alc.Unload();
        }

        private static T GetSingleImplementation<T>(Assembly assembly)
            where T : class
        {
            var implementations = assembly.GetImplementations<T>();
            if (!implementations.Any())
                WriteError($"Implementations of `{typeof(T).Name}` not found in assembly {assembly.GetName()}");
            if (implementations.Length != 1)
                WriteError($"Found more than one implementation of `{typeof(T).Name}` in assembly {assembly.GetName()}");
            return implementations.Length == 1 ? implementations[0] : null;
        }

        private static TypeScriptGenerationOptions BuildTypeScriptGenerationOptionsByOption(Options o)
        {
            return new TypeScriptGenerationOptions
                { 
                    EnableExplicitNullability = o.EnableExplicitNullability,
                    EnableOptionalProperties = o.EnableOptionalProperties,
                    EnumGenerationMode = o.EnumGenerationMode,
                    UseGlobalNullable = o.UseGlobalNullable,
                    NullabilityMode = o.NullabilityMode,
                    LinterDisableMode = o.LinterDisableMode
                };
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            WriteError($"Unexpected error occured: \n {exception?.Message ?? "no additional info was provided"}");
        }

        private static void WriteError(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ForegroundColor = oldColor;
        }
    }
}