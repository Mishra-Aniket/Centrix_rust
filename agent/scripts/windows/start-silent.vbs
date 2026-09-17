' Starts the Centrix watchdog completely hidden (no black window).
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run "cmd /c agent-watchdog.bat", 0, False
