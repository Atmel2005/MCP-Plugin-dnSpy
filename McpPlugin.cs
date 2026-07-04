using System;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Extension;
using dnSpy.Contracts.Menus;

namespace dnSpy.Extension.MCP
{
    // Атрибут Export говорит dnSpy загрузить этот класс как плагин при старте
    [ExportExtension]
    public sealed class McpPlugin : IExtension
    {
        [Import]
        private Lazy<dnSpy.Contracts.Documents.TreeView.IDocumentTreeView> documentTreeView;

        [Import]
        private Lazy<dnSpy.Contracts.Decompiler.IDecompilerService> decompilerService;

        [Import]
        private Lazy<dnSpy.Contracts.Debugger.DbgManager> dbgManager;

        public McpPlugin()
        {
            try {
                System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", "McpPlugin instantiated\n");
            } catch {}
        }

        private McpServer _mcpServer;

        public ExtensionInfo ExtensionInfo => new ExtensionInfo {
            ShortDescription = "MCP Server Plugin for AI Control",
        };

        public System.Collections.Generic.IEnumerable<string> MergedResourceDictionaries
        {
            get { yield break; }
        }

        public void OnEvent(ExtensionEvent @event, object obj)
        {
            System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", $"Event: {@event}\n");
            // Запускаем сервер, когда загрузился UI dnSpy
            if (@event == ExtensionEvent.AppLoaded)
            {
                try {
                    _mcpServer = new McpServer(documentTreeView, decompilerService, dbgManager);
                    _mcpServer.Start();
                    System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", "McpServer started successfully\n");
                } catch (System.Exception ex) {
                    System.IO.File.AppendAllText(@"C:\Users\atmel\.gemini\antigravity-ide\mcp.log", $"Error: {ex}\n");
                }
            }
            // Останавливаем при закрытии программы
            else if (@event == ExtensionEvent.AppExit)
            {
                _mcpServer?.Stop();
            }
        }
    }

    [ExportMenuItem(OwnerGuid = MenuConstants.APP_MENU_VIEW_GUID, Header = "MCP Server Plugin (Active)", Group = MenuConstants.GROUP_APP_MENU_VIEW_WINDOWS, Order = 5000)]
    sealed class McpMenuCommand : MenuItemBase
    {
        public override void Execute(IMenuItemContext context)
        {
            try { 
                System.Windows.MessageBox.Show("By Atmel Metall\n\nhttps://atmel2005.github.io/", "About MCP Plugin", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            } catch {}
        }
    }
}
