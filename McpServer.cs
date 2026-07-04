using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Decompiler;
using dnlib.DotNet;

namespace dnSpy.Extension.MCP
{
    public class McpToolRequest {
        public string command { get; set; }
        public Dictionary<string, string> args { get; set; }
    }

    public class McpServer
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly Lazy<IDocumentTreeView> _documentTreeView;
        private readonly Lazy<IDecompilerService> _decompilerService;

        public McpServer(Lazy<IDocumentTreeView> documentTreeView, Lazy<IDecompilerService> decompilerService)
        {
            _documentTreeView = documentTreeView;
            _decompilerService = decompilerService;
        }

        public void Start()
        {
            try
            {
                // Log removed
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                // Вы можете изменить порт, если 5555 занят
                _listener.Prefixes.Add("http://localhost:5555/mcp/");
                _listener.Start();
                // Log removed

                Task.Run(() => ListenAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                // Log removed
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
                // Log removed
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
