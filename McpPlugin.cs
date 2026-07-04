using System;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Extension;
using dnSpy.Contracts.Menus;

namespace dnSpy.Extension.MCP
{
    // Атрибут Export говорит dnSpy загрузить этот класс как плагин при старте
    [ExportExtension]
    sealed class McpPlugin : IExtension
    {
        private readonly Lazy<dnSpy.Contracts.Documents.TreeView.IDocumentTreeView> documentTreeView;
        private readonly Lazy<dnSpy.Contracts.Decompiler.IDecompilerService> decompilerService;

        [ImportingConstructor]
        public McpPlugin(Lazy<dnSpy.Contracts.Documents.TreeView.IDocumentTreeView> documentTreeView, Lazy<dnSpy.Contracts.Decompiler.IDecompilerService> decompilerService)
        {
            this.documentTreeView = documentTreeView;
            this.decompilerService = decompilerService;
            // Log removed
        }

        private McpServer _mcpServer;

        public ExtensionInfo ExtensionInfo => new ExtensionInfo {
            ShortDescription = "MCP Server Plugin for AI Control",
        };

        public System.Collections.Generic.IEnumerable<string> MergedResourceDictionaries
        {
            get { yield break; }
        }

        public void OnEvent(ExtensionEvent extEvent, object obj)
        {
            // Log removed
            // Запускаем сервер, когда загрузился UI dnSpy
            if (extEvent == ExtensionEvent.AppLoaded)
            {
                _mcpServer = new McpServer(documentTreeView, decompilerService);
                _mcpServer.Start();
            }
            // Останавливаем при закрытии программы
            else if (extEvent == ExtensionEvent.AppExit)
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
