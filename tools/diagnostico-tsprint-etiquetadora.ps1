# Diagnostico SOMENTE LEITURA: etiquetadoras Zebra e configuracao do TSPrint.
# Nao altera nada. Valores com nome de senha/chave aparecem como (oculto).
# Uso (no PC da unidade, como o proprio usuario):
#   powershell -ExecutionPolicy Bypass -File diagnostico-tsprint-etiquetadora.ps1
# Gera: Desktop\diagnostico-tsprint.txt

$ErrorActionPreference = 'SilentlyContinue'
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'diagnostico-tsprint.txt'
$secret = 'pass|senha|pwd|secret|token|cred|licen|serial|key$'
$lines = New-Object System.Collections.Generic.List[string]
function W($t) { $lines.Add([string]$t) }

W "=== Diagnostico TSPrint / etiquetadora - $(Get-Date -Format 'yyyy-MM-dd HH:mm') ==="
W "Windows: $([Environment]::OSVersion.VersionString) | Sessao: $env:SESSIONNAME"

W ""; W "=== 1. Impressoras Zebra/ZDesigner ==="
$zebras = Get-CimInstance Win32_Printer | Where-Object { $_.DriverName -match 'ZDesigner|Zebra' -or $_.Name -match 'Zebra|ZDesigner' }
foreach ($p in $zebras) {
    W ("Nome='{0}' | Driver='{1}' | Porta='{2}' | Padrao={3} | WorkOffline={4} | Status={5} | Local={6} | Rede={7}" -f `
        $p.Name, $p.DriverName, $p.PortName, $p.Default, $p.WorkOffline, $p.PrinterStatus, $p.Local, $p.Network)
    $port = Get-PrinterPort -Name $p.PortName
    if ($port) { W ("    Porta: Descricao='{0}' Monitor='{1}' Host='{2}'" -f $port.Description, $port.PortMonitor, $port.PrinterHostAddress) }
}
if (-not $zebras) { W "(nenhuma)" }
$names = @($zebras | ForEach-Object { $_.Name })

W ""; W "=== 2. Dispositivos Zebra conectados agora (PnP) ==="
Get-PnpDevice -PresentOnly | Where-Object { $_.FriendlyName -match 'Zebra|ZDesigner|GC420|ZD2|TLP' } |
    ForEach-Object { W ("{0} | Classe={1} | Status={2} | Id={3}" -f $_.FriendlyName, $_.Class, $_.Status, $_.InstanceId) }

W ""; W "=== 3. TSPrint instalado ==="
$uninst = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
          'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
          'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
Get-ItemProperty $uninst | Where-Object { $_.DisplayName -match 'TSPrint|TerminalWorks' } |
    ForEach-Object { W ("{0} {1} | Fabricante={2} | Pasta={3}" -f $_.DisplayName, $_.DisplayVersion, $_.Publisher, $_.InstallLocation) }
Get-Process | Where-Object { $_.ProcessName -match 'TSPrint' } |
    ForEach-Object { W ("Processo aberto: {0} | {1}" -f $_.ProcessName, $_.Path) }

W ""; W "=== 4. Registro (chaves TSPrint/TerminalWorks) ==="
$roots = 'HKCU:\Software', 'HKLM:\SOFTWARE', 'HKLM:\SOFTWARE\WOW6432Node'
foreach ($r in $roots) {
    Get-ChildItem $r | Where-Object { $_.PSChildName -match 'TSPrint|TerminalWorks' } | ForEach-Object {
        $keys = @($_) + @(Get-ChildItem $_.PSPath -Recurse)
        foreach ($k in $keys) {
            W "[$($k.Name)]"
            foreach ($v in $k.GetValueNames()) {
                $val = $k.GetValue($v)
                if ($v -match $secret) { $shown = '(oculto)' }
                elseif ($val -is [byte[]]) { $shown = "(binario, $($val.Length) bytes)" }
                else { $shown = ([string]($val -join ' ; ')); if ($shown.Length -gt 200) { $shown = $shown.Substring(0,200) + '...' } }
                W ("    {0} = {1}" -f $(if ($v) { $v } else { '(Padrao)' }), $shown)
            }
        }
    }
}

W ""; W "=== 5. Arquivos de configuracao do TSPrint ==="
$bases = $env:APPDATA, $env:LOCALAPPDATA, $env:ProgramData, ${env:ProgramFiles}, ${env:ProgramFiles(x86)}
foreach ($b in $bases) {
    if (-not $b) { continue }
    Get-ChildItem $b -Directory | Where-Object { $_.Name -match 'TSPrint|TerminalWorks' } | ForEach-Object {
        Get-ChildItem $_.FullName -Recurse -File -Include *.ini,*.xml,*.config,*.json,*.txt,*.cfg,*.conf,*.settings |
            Where-Object { $_.Length -lt 2MB } | ForEach-Object {
                W "--- $($_.FullName) ($($_.Length) bytes, alterado $($_.LastWriteTime))"
                $i = 0
                foreach ($l in Get-Content $_.FullName) {
                    $i++
                    $hitName = $names | Where-Object { $_ -and $l.Contains($_) }
                    if ($hitName -or $l -match 'printer|impressora|zebra|zdesigner|default') {
                        if ($l -match $secret) { W "    linha ${i}: (oculta - parece senha/chave)" }
                        else { W ("    linha {0}: {1}" -f $i, $l.Trim()) }
                    }
                }
            }
    }
}

W ""; W "=== 6. Ultimas linhas do log do WP Support Tools (etiquetadora) ==="
$log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'WPSupportTools\Logs') -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1
if ($log) { Get-Content $log.FullName | Select-String 'labels\.|Zebra instalada' | Select-Object -Last 15 | ForEach-Object { W $_.Line } }

$lines | Set-Content -Path $out -Encoding UTF8
$lines | ForEach-Object { Write-Host $_ }
Write-Host ""; Write-Host "Salvo em: $out"
