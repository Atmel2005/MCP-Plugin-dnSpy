import requests
from mcp.server.fastmcp import FastMCP

# Створюємо MCP сервер з підтримкою stdio
mcp = FastMCP("DNSPY_Bridge")
BASE_URL = "http://127.0.0.1:31337/mcp/call"

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

@mcp.tool()
def dnspy_run() -> str:
    """Resume execution in the debugger"""
    return call_dnspy("dnspy_run")

@mcp.tool()
def dnspy_start_debugging(filename: str, command_line: str = "", working_dir: str = "") -> str:
    """Start debugging a .NET executable directly. Pass the full path to the .exe in 'filename'."""
    return call_dnspy("dnspy_start_debugging", {"filename": filename, "command_line": command_line, "working_dir": working_dir})

@mcp.tool()
def dnspy_set_breakpoint(type_name: str, method_name: str, offset: int = 0) -> str:
    """Set a breakpoint at the specified method. Offset is the IL offset (default 0)."""
    return call_dnspy("dnspy_set_breakpoint", {"type_name": type_name, "method_name": method_name, "offset": str(offset)})

@mcp.tool()
def dnspy_remove_breakpoint(type_name: str, method_name: str, offset: int = 0) -> str:
    """Remove a breakpoint at the specified method."""
    return call_dnspy("dnspy_remove_breakpoint", {"type_name": type_name, "method_name": method_name, "offset": str(offset)})

@mcp.tool()
def dnspy_roslyn_evaluate(code: str) -> str:
    """Evaluate arbitrary C# code inside dnSpy using Roslyn (Interactive C#). 
    You have access to a global variable `DbgManager` (of type dnSpy.Contracts.Debugger.DbgManager) to control dnSpy."""
    return call_dnspy("dnspy_roslyn_evaluate", {"code": code})

@mcp.tool()
def dnspy_read_memory(address: str, size: int) -> str:
    """Read memory from the currently active process. Address should be in hex (e.g. '0x12345678' or '12345678')."""
    return call_dnspy("dnspy_read_memory", {"address": address.replace("0x", ""), "size": str(size)})

@mcp.tool()
def dnspy_write_memory(address: str, hex_data: str) -> str:
    """Write hex bytes to the active process memory. Address in hex, hex_data as continuous hex string (e.g. '909090')."""
    return call_dnspy("dnspy_write_memory", {"address": address.replace("0x", ""), "hex": hex_data.replace(" ", "")})

@mcp.tool()
def dnspy_stop_debugging() -> str:
    """Stop debugging and kill the attached process."""
    return call_dnspy("dnspy_stop_debugging")

@mcp.tool()
def dnspy_get_callstack() -> str:
    """Get the current call stack if the debugger is paused."""
    return call_dnspy("dnspy_get_callstack")

@mcp.tool()
def dnspy_pause() -> str:
    """Pause execution (Break All) in the debugger"""
    return call_dnspy("dnspy_pause")

@mcp.tool()
def dnspy_step_into() -> str:
    """Step into the next statement"""
    return call_dnspy("dnspy_step_into")

@mcp.tool()
def dnspy_step_over() -> str:
    """Step over the next statement"""
    return call_dnspy("dnspy_step_over")

@mcp.tool()
def dnspy_patch_il(type_name: str, method_name: str, index: int, opcode: str, operand: str = "") -> str:
    """Patch an IL instruction in a method. Requires 'index' (0-based IL instruction index) and 'opcode' (e.g. Ldc_I4_1, Nop, Ret). Optional 'operand' as string."""
    return call_dnspy("dnspy_patch_il", {"type_name": type_name, "method_name": method_name, "index": str(index), "opcode": opcode, "operand": operand})

@mcp.tool()
def dnspy_save_module(module_name: str, save_path: str) -> str:
    """Save the patched module (assembly) to a new file path. Example: save_path='C:\\patched.dll'"""
    return call_dnspy("dnspy_save_module", {"module_name": module_name, "save_path": save_path})

if __name__ == "__main__":
    # FastMCP автоматично використовує stdio
    mcp.run()
