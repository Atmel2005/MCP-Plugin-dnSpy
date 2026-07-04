import requests
from mcp.server.fastmcp import FastMCP

# Створюємо MCP сервер з підтримкою stdio
mcp = FastMCP("DNSPY_Bridge")
BASE_URL = "http://localhost:5555/mcp/call"

def call_dnspy(command: str, args: dict = None) -> str:
    """Функція для відправки запитів до C# REST API"""
    if args is None:
        args = {}
    try:
        response = requests.post(BASE_URL, json={"command": command, "args": args}, timeout=10)
        return response.text
    except requests.exceptions.RequestException as e:
        return f'{{"status": "error", "message": "Failed to connect to dnSpy: {e}"}}'

# --- Реєструємо інструменти ---

@mcp.tool()
def dnspy_list_assemblies() -> str:
    """List all loaded assemblies in dnSpy"""
    return call_dnspy("dnspy_list_assemblies")

@mcp.tool()
def dnspy_get_types(assembly_name: str) -> str:
    """List all types (classes, structs, etc.) in an assembly"""
    return call_dnspy("dnspy_get_types", {"assembly_name": assembly_name})

@mcp.tool()
def dnspy_get_methods(type_name: str) -> str:
    """List all methods in a specific type"""
    return call_dnspy("dnspy_get_methods", {"type_name": type_name})

@mcp.tool()
def dnspy_decompile_method(type_name: str, method_name: str) -> str:
    """Decompile a specific method to C#"""
    return call_dnspy("dnspy_decompile_method", {"type_name": type_name, "method_name": method_name})

@mcp.tool()
def dnspy_get_fields(type_name: str) -> str:
    """List all fields and properties in a specific type"""
    return call_dnspy("dnspy_get_fields", {"type_name": type_name})

@mcp.tool()
def dnspy_get_method_il(type_name: str, method_name: str) -> str:
    """Get the raw IL code for a specific method"""
    return call_dnspy("dnspy_get_method_il", {"type_name": type_name, "method_name": method_name})

@mcp.tool()
def dnspy_search_string(search_text: str) -> str:
    """Search for a string literal in all loaded assemblies"""
    return call_dnspy("dnspy_search_string", {"search_text": search_text})

if __name__ == "__main__":
    # FastMCP автоматично використовує stdio
    mcp.run()
