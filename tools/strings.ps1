# Helpers for the localized string resources (Strings.resx / Strings.es.resx / Strings.Designer.cs).
# Usage (dot-source first):   . .\tools\strings.ps1
#   Add-OmString UI Client_InputLabel '&Text to send:' '&Texto a enviar:'
#   Remove-OmStrings UI @('Old_Key1', 'Old_Key2')
#   Update-OmStringsDesigner UI

$script:OmRoot = Split-Path -Parent $PSScriptRoot

function Get-OmResourceDir([string]$Project) {
    Join-Path $script:OmRoot "src\Omnimud.$Project\Resources"
}

function Add-OmResxEntry([string]$Path, [string]$Key, [string]$Value) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($Path)
    $existing = $xml.SelectSingleNode("/root/data[@name='$Key']")
    if ($existing) {
        $existing.SelectSingleNode('value').InnerText = $Value
    } else {
        $data = $xml.CreateElement('data')
        $data.SetAttribute('name', $Key)
        $data.SetAttribute('space', 'http://www.w3.org/XML/1998/namespace', 'preserve') | Out-Null
        $val = $xml.CreateElement('value')
        $val.InnerText = $Value
        $data.AppendChild($xml.CreateWhitespace("`n    ")) | Out-Null
        $data.AppendChild($val) | Out-Null
        $data.AppendChild($xml.CreateWhitespace("`n  ")) | Out-Null
        $root = $xml.DocumentElement
        # The root's last child is the whitespace before </root>: insert right before it.
        $last = $root.LastChild
        $root.InsertBefore($xml.CreateWhitespace("`n  "), $last) | Out-Null
        $root.InsertBefore($data, $last) | Out-Null
    }
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}

# Several people (or agents) may add strings at the same time: every read-modify-write of the
# resource files happens under one machine-wide lock, and always re-reads the files from disk.
function Invoke-OmStringsLocked([scriptblock]$Action) {
    $mutex = New-Object System.Threading.Mutex($false, 'Global\OmnimudStringsResx')
    $taken = $false
    try {
        try { $taken = $mutex.WaitOne([TimeSpan]::FromMinutes(2)) } catch [System.Threading.AbandonedMutexException] { $taken = $true }
        if (-not $taken) { throw 'Could not lock the string resources.' }
        & $Action
    } finally {
        if ($taken) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Add-OmString([string]$Project, [string]$Key, [string]$En, [string]$Es, [switch]$NoDesigner) {
    Add-OmStrings $Project @(, @($Key, $En, $Es))
}

# Adds many strings at once: each item is @(key, en, es).
function Add-OmStrings([string]$Project, [object[]]$Items) {
    Invoke-OmStringsLocked {
        $dir = Get-OmResourceDir $Project
        foreach ($item in $Items) {
            Add-OmResxEntry (Join-Path $dir 'Strings.resx') $item[0] $item[1]
            Add-OmResxEntry (Join-Path $dir 'Strings.es.resx') $item[0] $item[2]
        }
        Update-OmStringsDesigner $Project
    }
}

function Remove-OmResxEntries([string]$Path, [string[]]$Keys) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($Path)
    $removed = 0
    foreach ($key in $Keys) {
        $data = $xml.SelectSingleNode("/root/data[@name='$key']")
        if (-not $data) { continue }
        # The whitespace before the entry goes with it, so no blank lines are left behind.
        $before = $data.PreviousSibling
        if ($before -and $before.NodeType -in 'Whitespace', 'SignificantWhitespace') {
            [void]$before.ParentNode.RemoveChild($before)
        }
        [void]$data.ParentNode.RemoveChild($data)
        $removed++
    }
    if ($removed -gt 0) {
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
        try { $xml.Save($writer) } finally { $writer.Dispose() }
    }
    $removed
}

# Removes strings from both languages and regenerates the designer:
#   Remove-OmStrings UI @('Old_Key1', 'Old_Key2')
# Check first that no .cs of the project uses Strings.<Key>. Keys that do not exist are ignored.
function Remove-OmStrings([string]$Project, [string[]]$Keys) {
    Invoke-OmStringsLocked {
        $dir = Get-OmResourceDir $Project
        $neutral = Remove-OmResxEntries (Join-Path $dir 'Strings.resx') $Keys
        $spanish = Remove-OmResxEntries (Join-Path $dir 'Strings.es.resx') $Keys
        Update-OmStringsDesigner $Project
        "Removed from Omnimud.${Project}: $neutral (neutral), $spanish (es)"
    }
}

function Update-OmStringsDesigner([string]$Project) {
    $dir = Get-OmResourceDir $Project
    [xml]$xml = Get-Content (Join-Path $dir 'Strings.resx') -Raw
    $keys = $xml.root.data | ForEach-Object { $_.name }
    $visibility = 'internal'
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('//------------------------------------------------------------------------------')
    [void]$sb.AppendLine('// <auto-generated>')
    [void]$sb.AppendLine('//     Generated by tools/strings.ps1 from Strings.resx. Do not edit by hand.')
    [void]$sb.AppendLine('// </auto-generated>')
    [void]$sb.AppendLine('//------------------------------------------------------------------------------')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('#nullable enable')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('using System.Globalization;')
    [void]$sb.AppendLine('using System.Resources;')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("namespace Omnimud.$Project.Resources;")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('/// <summary>')
    [void]$sb.AppendLine('/// A strongly-typed resource class for looking up localized strings.')
    [void]$sb.AppendLine('/// </summary>')
    [void]$sb.AppendLine("$visibility static class Strings")
    [void]$sb.AppendLine('{')
    [void]$sb.AppendLine('    private static ResourceManager? _resourceManager;')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('    private static ResourceManager ResourceManager =>')
    [void]$sb.AppendLine("        _resourceManager ??= new ResourceManager(`"Omnimud.$Project.Resources.Strings`", typeof(Strings).Assembly);")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("    $visibility static CultureInfo? Culture { get; set; }")
    [void]$sb.AppendLine()
    foreach ($k in $keys) {
        [void]$sb.AppendLine("    $visibility static string $k => ResourceManager.GetString(`"$k`", Culture) ?? `"`";")
    }
    [void]$sb.AppendLine('}')
    [System.IO.File]::WriteAllText((Join-Path $dir 'Strings.Designer.cs'), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
}
