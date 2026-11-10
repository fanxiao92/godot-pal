# Shared strict JSON reader. ISO dates remain strings; duplicate properties are rejected.
function Convert-AgentJsonValue($Element) {
    switch ($Element.ValueKind.ToString()) {
        'Object' {
            $value = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                if ($value.ContainsKey($property.Name)) { throw "[agent-evidence:json] Duplicate property: $($property.Name)" }
                $value.Add($property.Name, (Convert-AgentJsonValue $property.Value))
            }
            return ,$value
        }
        'Array' { return ,@(foreach ($item in $Element.EnumerateArray()) { Convert-AgentJsonValue $item }) }
        'String' { return $Element.GetString() }
        'Number' { $number = 0L; if ($Element.TryGetInt64([ref]$number)) { return $number }; return $Element.GetDouble() }
        'True' { return $true }; 'False' { return $false }; 'Null' { return $null }
        default { throw '[agent-evidence:json] Unsupported JSON value.' }
    }
}
function Read-AgentJson {
    param([Parameter(Mandatory)][string]$Path, [string]$SchemaFile)
    # ReadAllText detects UTF-16/32 BOMs even when supplied a strict UTF-8 encoding.
    $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path))
    if ($text.StartsWith([string][char]0xFEFF, [StringComparison]::Ordinal)) { $text = $text.Substring(1) }
    $json = [Text.Json.JsonDocument]::Parse($text)
    try { $value = Convert-AgentJsonValue $json.RootElement } finally { $json.Dispose() }
    if ($SchemaFile) {
        try { $valid = Test-Json -Json $text -SchemaFile $SchemaFile -ErrorAction Stop }
        catch { throw "[agent-evidence:schema] $($_.Exception.Message)" }
        if (-not $valid) { throw '[agent-evidence:schema] Input does not match the evidence schema.' }
    }
    return ,$value
}
function Write-AgentNewFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}
function Write-AgentNewJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    $text = (ConvertTo-Json -InputObject $Value -Depth 100) + "`n"
    Write-AgentNewFile $Path ([Text.UTF8Encoding]::new($false).GetBytes($text))
}
Export-ModuleMember -Function Read-AgentJson, Write-AgentNewFile, Write-AgentNewJson
