Set sh = CreateObject("Wscript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
sh.CurrentDirectory = folder
cmd = "powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & folder & "\Rech.ps1"""
code = sh.Run(cmd, 0, True)
If code <> 0 Then
  MsgBox "RECH did not start. Right-click this folder, Properties, Unblock, then run RECH.vbs again.", 48, "RECH"
End If
