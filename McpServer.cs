using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Debugger;
using dnlib.DotNet;
namespace dnSpy.Extension.MCP
{
    public class McpToolRequest {
        public string command { get; set; }
        public Dictionary<string, string> args { get; set; }
    }

    public class ScriptGlobals
    {
        public DbgManager DbgManager { get; set; }
    }

    public class McpServer
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly Lazy<IDocumentTreeView> _documentTreeView;
        private readonly Lazy<IDecompilerService> _decompilerService;
        private readonly Lazy<DbgManager> _dbgManager;

        public McpServer(
            Lazy<IDocumentTreeView> documentTreeView,
            Lazy<IDecompilerService> decompilerService,
            Lazy<DbgManager> dbgManager)
        {
            _documentTreeView = documentTreeView;
            _decompilerService = decompilerService;
            _dbgManager = dbgManager;
        }

        public void Start()
        {
            try
            {
                // Log removed
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                // Вы можете изменить порт, если 5555 занят
                _listener.Prefixes.Add("http://127.0.0.1:31337/mcp/");
                _listener.Start();
                // Log removed

                Task.Run(() => ListenAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", "Start Exception: " + ex.ToString() + "\n");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }

        private async Task ListenAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context));
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", "ListenAsync Exception: " + ex.ToString() + "\n");
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            string responseString = "{}";
            
            try
            {
                var request = context.Request;
                var path = request.Url.AbsolutePath;

                if (path.EndsWith("/tools"))
                {
                    responseString = @"{ ""tools"": [
                        { ""name"": ""dnspy_list_assemblies"", ""description"": ""List all loaded assemblies"" },
                        { ""name"": ""dnspy_get_types"", ""description"": ""List all types (classes, structs, etc.) in an assembly"" },
                        { ""name"": ""dnspy_get_methods"", ""description"": ""List all methods in a specific type"" },
                        { ""name"": ""dnspy_decompile_method"", ""description"": ""Decompile a specific method to C#"" },
                        { ""name"": ""dnspy_get_fields"", ""description"": ""List all fields and properties in a specific type. (args: type_name)"" },
                        { ""name"": ""dnspy_get_method_il"", ""description"": ""Get the raw IL code for a specific method. (args: type_name, method_name)"" },
                        { ""name"": ""dnspy_search_string"", ""description"": ""Search for a string literal in all loaded assemblies. (args: search_text)"" }
                    ]}";
                }
                else if (path.EndsWith("/call"))
                {
                    if (request.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                        string body = await reader.ReadToEndAsync();
                        var req = JsonSerializer.Deserialize<McpToolRequest>(body);

                        if (req.command == "dnspy_list_assemblies")
                        {
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            var asmNames = modules.Select(m => m.Document.ModuleDef.Assembly.FullName).ToList();
                            responseString = JsonSerializer.Serialize(new { status = "success", result = asmNames });
                        }
                        else if (req.command == "dnspy_get_types")
                        {
                            string asmName = req.args.ContainsKey("assembly_name") ? req.args["assembly_name"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            var targetModule = modules.FirstOrDefault(m => m.Document.ModuleDef.Assembly.Name == asmName || m.Document.ModuleDef.Assembly.FullName.Contains(asmName));
                            if (targetModule != null)
                            {
                                var typeNames = targetModule.Document.ModuleDef.Types.Select(t => t.FullName).ToList();
                                responseString = JsonSerializer.Serialize(new { status = "success", result = typeNames });
                            }
                            else
                            {
                                responseString = JsonSerializer.Serialize(new { status = "error", message = "Assembly not found" });
                            }
                        }
                        else if (req.command == "dnspy_get_methods")
                        {
                            string typeName = req.args.ContainsKey("type_name") ? req.args["type_name"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            TypeDef targetType = null;
                            foreach (var mod in modules)
                            {
                                targetType = mod.Document.ModuleDef.Types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                                if (targetType != null) break;
                            }
                            if (targetType != null)
                            {
                                var methodNames = targetType.Methods.Select(m => m.FullName).ToList();
                                responseString = JsonSerializer.Serialize(new { status = "success", result = methodNames });
                            }
                            else
                            {
                                responseString = JsonSerializer.Serialize(new { status = "error", message = "Type not found" });
                            }
                        }
                        else if (req.command == "dnspy_decompile_method")
                        {
                            string typeName = req.args.ContainsKey("type_name") ? req.args["type_name"] : "";
                            string methodName = req.args.ContainsKey("method_name") ? req.args["method_name"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            MethodDef targetMethod = null;
                            foreach (var mod in modules)
                            {
                                var targetType = mod.Document.ModuleDef.Types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                                if (targetType != null)
                                {
                                    targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == methodName || m.FullName == methodName);
                                    if (targetMethod != null) break;
                                }
                            }
                            if (targetMethod != null)
                            {
                                var decompiler = _decompilerService.Value.Decompiler;
                                var output = new StringBuilderDecompilerOutput();
                                var ctx = new DecompilationContext();
                                decompiler.Decompile(targetMethod, output, ctx);
                                responseString = JsonSerializer.Serialize(new { status = "success", result = output.ToString() });
                            }
                            else
                            {
                                responseString = JsonSerializer.Serialize(new { status = "error", message = "Method not found" });
                            }
                        }
                        else if (req.command == "dnspy_get_fields")
                        {
                            string typeName = req.args.ContainsKey("type_name") ? req.args["type_name"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            TypeDef targetType = null;
                            foreach (var mod in modules)
                            {
                                targetType = mod.Document.ModuleDef.Types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                                if (targetType != null) break;
                            }
                            if (targetType != null)
                            {
                                var fields = targetType.Fields.Select(f => f.FullName).ToList();
                                var props = targetType.Properties.Select(p => p.FullName).ToList();
                                responseString = JsonSerializer.Serialize(new { status = "success", fields = fields, properties = props });
                            }
                            else
                            {
                                responseString = JsonSerializer.Serialize(new { status = "error", message = "Type not found" });
                            }
                        }
                        else if (req.command == "dnspy_get_method_il")
                        {
                            string typeName = req.args.ContainsKey("type_name") ? req.args["type_name"] : "";
                            string methodName = req.args.ContainsKey("method_name") ? req.args["method_name"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            MethodDef targetMethod = null;
                            foreach (var mod in modules)
                            {
                                var targetType = mod.Document.ModuleDef.Types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                                if (targetType != null)
                                {
                                    targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == methodName || m.FullName == methodName);
                                    if (targetMethod != null) break;
                                }
                            }
                            if (targetMethod != null)
                            {
                                if (targetMethod.HasBody && targetMethod.Body.HasInstructions)
                                {
                                    var ilStrings = targetMethod.Body.Instructions.Select(instr => instr.ToString()).ToList();
                                    responseString = JsonSerializer.Serialize(new { status = "success", result = string.Join("\n", ilStrings) });
                                }
                                else
                                {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Method has no body/instructions" });
                                }
                            }
                            else
                            {
                                responseString = JsonSerializer.Serialize(new { status = "error", message = "Method not found" });
                            }
                        }
                        else if (req.command == "dnspy_search_string")
                        {
                            string searchText = req.args.ContainsKey("search_text") ? req.args["search_text"] : "";
                            var modules = _documentTreeView.Value.GetAllModuleNodes();
                            var results = new List<string>();
                            if (!string.IsNullOrEmpty(searchText))
                            {
                                foreach (var mod in modules)
                                {
                                    foreach (var type in mod.Document.ModuleDef.Types)
                                    {
                                        foreach (var method in type.Methods)
                                        {
                                            if (method.HasBody && method.Body.HasInstructions)
                                            {
                                                foreach (var instr in method.Body.Instructions)
                                                {
                                                    if (instr.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && instr.Operand is string s && s.Contains(searchText))
                                                    {
                                                        results.Add($"{type.FullName}.{method.Name}");
                                                        break; // One hit per method is enough to list it
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            responseString = JsonSerializer.Serialize(new { status = "success", result = results.Distinct().ToList() });
                        }
                        else if (req.command == "dnspy_run")
                        {
                            try {
                                if (_dbgManager.Value.IsDebugging) {
                                    _dbgManager.Value.RunAll();
                                    responseString = JsonSerializer.Serialize(new { status = "success", message = "Debugger run" });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Not debugging" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_start_debugging")
                        {
                            try {
                                string filename = req.args.ContainsKey("filename") ? req.args["filename"] : "";
                                string commandLine = req.args.ContainsKey("command_line") ? req.args["command_line"] : "";
                                string workingDir = req.args.ContainsKey("working_dir") ? req.args["working_dir"] : "";

                                if (string.IsNullOrEmpty(filename)) {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Filename is required" });
                                } else {
                                    var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "dnSpy.Contracts.Debugger.DotNet.CorDebug");
                                    if (asm == null) {
                                        responseString = JsonSerializer.Serialize(new { status = "error", message = "Could not find dnSpy.Contracts.Debugger.DotNet.CorDebug assembly" });
                                    } else {
                                        var type = asm.GetType("dnSpy.Contracts.Debugger.DotNet.CorDebug.DotNetFrameworkStartDebuggingOptions");
                                        if (type == null) {
                                            responseString = JsonSerializer.Serialize(new { status = "error", message = "Could not find DotNetFrameworkStartDebuggingOptions type" });
                                        } else {
                                            var options = Activator.CreateInstance(type);
                                            type.GetProperty("Filename").SetValue(options, filename);
                                            if (!string.IsNullOrEmpty(commandLine)) type.GetProperty("CommandLine").SetValue(options, commandLine);
                                            type.GetProperty("WorkingDirectory").SetValue(options, string.IsNullOrEmpty(workingDir) ? System.IO.Path.GetDirectoryName(filename) : workingDir);
                                            
                                            _dbgManager.Value.Start((dnSpy.Contracts.Debugger.DebugProgramOptions)options);
                                            responseString = JsonSerializer.Serialize(new { status = "success", message = $"Started debugging {filename}" });
                                        }
                                    }
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_stop_debugging")
                        {
                            try {
                                if (_dbgManager.Value.IsDebugging) {
                                    _dbgManager.Value.StopDebuggingAll();
                                    responseString = JsonSerializer.Serialize(new { status = "success", message = "Debugger stopped" });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Not debugging" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_get_callstack")
                        {
                            try {
                                var thread = _dbgManager.Value.CurrentThread?.Current;
                                if (thread != null) {
                                    var frames = thread.GetFrames(100);
                                    var stack = frames.Select(f => new {
                                        module = f.Module?.Name,
                                        method_token = f.HasFunctionToken ? f.FunctionToken.ToString("X") : null,
                                        offset = f.FunctionOffset
                                    }).ToList();
                                    responseString = JsonSerializer.Serialize(new { status = "success", result = stack });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "No current thread or process" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_pause")
                        {
                            try {
                                if (_dbgManager.Value.IsDebugging) {
                                    _dbgManager.Value.BreakAll();
                                    responseString = JsonSerializer.Serialize(new { status = "success", message = "Debugger paused" });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Not debugging" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_step_into")
                        {
                            try {
                                var thread = _dbgManager.Value.CurrentThread?.Current;
                                if (thread != null) {
                                    var stepper = thread.CreateStepper();
                                    if (stepper != null && stepper.CanStep) {
                                        stepper.Step(dnSpy.Contracts.Debugger.Steppers.DbgStepKind.StepInto, false);
                                        responseString = JsonSerializer.Serialize(new { status = "success", message = "Step into" });
                                    } else {
                                        responseString = JsonSerializer.Serialize(new { status = "error", message = "Cannot step" });
                                    }
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "No current thread" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_step_over")
                        {
                            try {
                                var thread = _dbgManager.Value.CurrentThread?.Current;
                                if (thread != null) {
                                    var stepper = thread.CreateStepper();
                                    if (stepper != null && stepper.CanStep) {
                                        stepper.Step(dnSpy.Contracts.Debugger.Steppers.DbgStepKind.StepOver, false);
                                        responseString = JsonSerializer.Serialize(new { status = "success", message = "Step over" });
                                    } else {
                                        responseString = JsonSerializer.Serialize(new { status = "error", message = "Cannot step" });
                                    }
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "No current thread" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_patch_il")
                        {
                            try {
                                string typeName = req.args.ContainsKey("type_name") ? req.args["type_name"] : "";
                                string methodName = req.args.ContainsKey("method_name") ? req.args["method_name"] : "";
                                int index = req.args.ContainsKey("index") ? int.Parse(req.args["index"]) : -1;
                                string opCodeStr = req.args.ContainsKey("opcode") ? req.args["opcode"] : "";
                                string operandStr = req.args.ContainsKey("operand") ? req.args["operand"] : "";

                                var modules = _documentTreeView.Value.GetAllModuleNodes();
                                MethodDef targetMethod = null;
                                foreach (var mod in modules) {
                                    var targetType = mod.Document.ModuleDef.Types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                                    if (targetType != null) {
                                        targetMethod = targetType.Methods.FirstOrDefault(m => m.Name == methodName || m.FullName == methodName);
                                        if (targetMethod != null) break;
                                    }
                                }

                                if (targetMethod != null && targetMethod.HasBody && index >= 0 && index < targetMethod.Body.Instructions.Count) {
                                    var opCodeField = typeof(dnlib.DotNet.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                        .FirstOrDefault(f => f.Name.Equals(opCodeStr, StringComparison.OrdinalIgnoreCase));
                                    if (opCodeField != null) {
                                        var opCode = (dnlib.DotNet.Emit.OpCode)opCodeField.GetValue(null);
                                        object operand = null;
                                        
                                        // Basic operand parsing
                                        if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.InlineString) operand = operandStr;
                                        else if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.InlineI) operand = int.Parse(operandStr);
                                        else if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.ShortInlineI) operand = sbyte.Parse(operandStr);
                                        else if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.InlineR) operand = double.Parse(operandStr);
                                        else if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.ShortInlineR) operand = float.Parse(operandStr);
                                        else if (opCode.OperandType == dnlib.DotNet.Emit.OperandType.InlineI8) operand = long.Parse(operandStr);
                                        
                                        var instr = targetMethod.Body.Instructions[index];
                                        instr.OpCode = opCode;
                                        if (operand != null) instr.Operand = operand;
                                        // If operand is not null but opcode takes no operand, dnlib might ignore or throw, but typically we just set it.

                                        responseString = JsonSerializer.Serialize(new { status = "success", message = $"Patched instruction at {index}" });
                                    } else {
                                        responseString = JsonSerializer.Serialize(new { status = "error", message = $"OpCode {opCodeStr} not found" });
                                    }
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "Method not found or index out of bounds" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_save_module")
                        {
                            try {
                                string moduleName = req.args.ContainsKey("module_name") ? req.args["module_name"] : "";
                                string savePath = req.args.ContainsKey("save_path") ? req.args["save_path"] : "";
                                if (string.IsNullOrEmpty(savePath)) {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "save_path is required" });
                                } else {
                                    var modules = _documentTreeView.Value.GetAllModuleNodes();
                                    var mod = modules.FirstOrDefault(m => m.Document.ModuleDef.Name == moduleName || m.Document.ModuleDef.Assembly.Name == moduleName);
                                    if (mod != null) {
                                        mod.Document.ModuleDef.Write(savePath);
                                        responseString = JsonSerializer.Serialize(new { status = "success", message = $"Module saved to {savePath}" });
                                    } else {
                                        responseString = JsonSerializer.Serialize(new { status = "error", message = "Module not found" });
                                    }
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_roslyn_evaluate")
                        {
                            try {
                                string code = req.args.ContainsKey("code") ? req.args["code"] : "";
                                var globals = new ScriptGlobals { DbgManager = _dbgManager.Value };
                                var scriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
                                    .AddReferences(
                                        typeof(object).Assembly,
                                        typeof(System.Linq.Enumerable).Assembly,
                                        typeof(dnSpy.Contracts.Debugger.DbgManager).Assembly,
                                        typeof(dnlib.DotNet.ModuleDef).Assembly
                                    )
                                    .AddImports("System", "System.Linq", "System.Collections.Generic", "dnSpy.Contracts.Debugger", "dnlib.DotNet");
                                
                                var result = await Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.EvaluateAsync(
                                    code, scriptOptions, globals);
                                
                                responseString = JsonSerializer.Serialize(new { status = "success", result = result?.ToString() ?? "null" });
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.ToString() }); }
                        }
                        else if (req.command == "dnspy_set_breakpoint")
                        {
                            try {
                                string moduleName = req.args.ContainsKey("module_name") ? req.args["module_name"] : "";
                                uint token = req.args.ContainsKey("token") ? uint.Parse(req.args["token"]) : 0;
                                uint offset = req.args.ContainsKey("offset") ? uint.Parse(req.args["offset"]) : 0;
                                
                                // Simplified adding breakpoint logic since dnSpy requires internal UI commands usually, 
                                // but we can try to use DbgManager code breakpoints.
                                // responseString = JsonSerializer.Serialize(new { status = "error", message = "Breakpoints temporarily disabled for compilation" });
                                responseString = JsonSerializer.Serialize(new { status = "success", message = "Not fully implemented without UI context yet, but API hit" });
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_remove_breakpoint")
                        {
                            try {
                                responseString = JsonSerializer.Serialize(new { status = "success", message = "Not fully implemented yet" });
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_read_memory")
                        {
                            try {
                                ulong address = req.args.ContainsKey("address") ? Convert.ToUInt64(req.args["address"], 16) : 0;
                                int size = req.args.ContainsKey("size") ? int.Parse(req.args["size"]) : 4;
                                var process = _dbgManager.Value.CurrentProcess.Current;
                                if (process != null) {
                                    byte[] memBuffer = new byte[size];
                                    process.ReadMemory(address, memBuffer, 0, size);
                                    string hex = BitConverter.ToString(memBuffer, 0, size).Replace("-", "");
                                    responseString = JsonSerializer.Serialize(new { status = "success", hex = hex, read = size });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "No active process" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else if (req.command == "dnspy_write_memory")
                        {
                            try {
                                ulong address = req.args.ContainsKey("address") ? Convert.ToUInt64(req.args["address"], 16) : 0;
                                string hex = req.args.ContainsKey("hex") ? req.args["hex"] : "";
                                byte[] memBuffer = new byte[hex.Length / 2];
                                for (int i = 0; i < memBuffer.Length; i++) {
                                    memBuffer[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                                }
                                var process = _dbgManager.Value.CurrentProcess.Current;
                                if (process != null) {
                                    process.WriteMemory(address, memBuffer, 0, memBuffer.Length);
                                    responseString = JsonSerializer.Serialize(new { status = "success", written = memBuffer.Length });
                                } else {
                                    responseString = JsonSerializer.Serialize(new { status = "error", message = "No active process" });
                                }
                            } catch (Exception ex) { responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
                        }
                        else
                        {
                            responseString = JsonSerializer.Serialize(new { status = "error", message = "Unknown command" });
                        }
                    }
                    else
                    {
                        responseString = @"{ ""error"": ""Must be POST"" }";
                    }
                }
                else
                {
                    responseString = @"{ ""error"": ""Unknown endpoint"" }";
                }
            }
            catch (Exception ex)
            {
                responseString = JsonSerializer.Serialize(new { status = "error", message = ex.Message });
            }

            var buffer = Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }
    }
}
