# Diagnostico SOMENTE LEITURA: impressoras do Windows e configuracao do TSPrint (versao antiga ou nova).
# Nao altera nada. Nunca le arquivos "geral*" (usuario/senha) e esconde valores com cara de senha/chave.
# Uso (no PC da unidade, como o proprio usuario):
#   powershell -ExecutionPolicy Bypass -File diagnostico-impressoras.ps1
# Gera: Desktop\diagnostico-impressoras.txt

$ErrorActionPreference = 'SilentlyContinue'
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'diagnostico-impressoras.txt'
$secret = 'pass|senha|pwd|secret|token|cred|licen|serial|key|auth|cookie|session'
$lines = New-Object System.Collections.Generic.List[string]
function W($t) { $lines.Add([string]$t) }

W "=== Diagnostico de impressoras - $(Get-Date -Format 'yyyy-MM-dd HH:mm') ==="
W "Windows: $([Environment]::OSVersion.VersionString) | PC: $env:COMPUTERNAME | Sessao: $env:SESSIONNAME"

W ""; W "=== 1. Impressoras (Windows) ==="
$wmi = @{}; Get-CimInstance Win32_Printer | ForEach-Object { $wmi[$_.Name] = $_ }
foreach ($p in Get-Printer) {
    $w = $wmi[$p.Name]
    W ("Nome='{0}' | Driver='{1}' | Porta='{2}' | Tipo={3} | Status='{4}' | Documentos={5}" -f `
        $p.Name, $p.DriverName, $p.PortName, $p.Type, $p.PrinterStatus, $p.JobCount)
    if ($w) {
        W ("    WMI: Padrao={0} WorkOffline={1} PrinterStatus={2} ExtendedPrinterStatus={3} DetectedErrorState={4} ExtendedDetectedErrorState={5} Rede={6}" -f `
            $w.Default, $w.WorkOffline, $w.PrinterStatus, $w.ExtendedPrinterStatus, $w.DetectedErrorState, $w.ExtendedDetectedErrorState, $w.Network)
    }
    $port = Get-PrinterPort -Name $p.PortName
    if ($port) {
        W ("    Porta: Monitor='{0}' Descricao='{1}' Host='{2}'" -f $port.PortMonitor, $port.Description, $port.PrinterHostAddress)
        if ($port.PrinterHostAddress) {
            $ok = Test-Connection -ComputerName $port.PrinterHostAddress -Count 1 -Quiet
            W ("    Ping {0}: {1}" -f $port.PrinterHostAddress, $(if ($ok) { 'responde' } else { 'NAO responde' }))
        }
    }
    Get-PrintJob -PrinterName $p.Name | Select-Object -First 5 | ForEach-Object {
        W ("    Documento: '{0}' Status='{1}' Enviado={2}" -f $_.DocumentName, $_.JobStatus, $_.SubmittedTime)
    }
}

W ""; W "=== 2. Dispositivos de impressao (PnP: presente agora ou nao) ==="
$present = @{}; Get-PnpDevice -PresentOnly | ForEach-Object { $present[$_.InstanceId] = $true }
Get-PnpDevice | Where-Object { $_.Class -in 'PrintQueue', 'Printer', 'WSDPrintDevice' -or $_.FriendlyName -match 'Brother|Samsung|HP |Epson|Canon|Zebra|ZDesigner|Lexmark|Kyocera|Ricoh' } |
    Sort-Object Class, FriendlyName | ForEach-Object {
        W ("{0} | Classe={1} | Status={2} | Presente={3} | Id={4}" -f $_.FriendlyName, $_.Class, $_.Status, [bool]$present[$_.InstanceId], $_.InstanceId)
    }

W ""; W "=== 3. Erros recentes do servico de impressao ==="
Get-WinEvent -LogName 'Microsoft-Windows-PrintService/Admin' -MaxEvents 15 |
    ForEach-Object { W ("{0} | Id={1} | {2}" -f $_.TimeCreated, $_.Id, (($_.Message -split "`n")[0]).Trim()) }

W ""; W "=== 4. TSPrint: programas encontrados ==="
$dirs = New-Object System.Collections.Generic.List[string]
Get-Process | Where-Object { $_.ProcessName -match 'TSPrint|Unilab|Uniware' } | ForEach-Object {
    W ("Processo aberto: {0} | {1} | versao {2}" -f $_.ProcessName, $_.Path, $_.MainModule.FileVersionInfo.FileVersion)
    if ($_.Path) { $dirs.Add((Split-Path $_.Path)) }
}
$bases = 'C:\Rede_Unilab', $env:APPDATA, $env:LOCALAPPDATA, (Join-Path $env:LOCALAPPDATA 'Programs'), $env:ProgramData, ${env:ProgramFiles}, ${env:ProgramFiles(x86)}
foreach ($b in $bases) {
    if (-not $b -or -not (Test-Path $b)) { continue }
    Get-ChildItem $b -Directory | Where-Object { $_.Name -match 'TSPrint|Unilab|Uniware|Rede_Unilab' } | ForEach-Object { $dirs.Add($_.FullName) }
}
if (Test-Path 'C:\Rede_Unilab') { $dirs.Add('C:\Rede_Unilab') }
$dirs = @($dirs | Sort-Object -Unique)
# Sem repetir subpastas de pastas ja listadas.
$dirs = @($dirs | Where-Object { $d = $_; -not ($dirs | Where-Object { $_ -ne $d -and $d.StartsWith($_ + '\') }) })
foreach ($d in $dirs) { W "Pasta: $d" }
foreach ($d in $dirs) {
    Get-ChildItem $d -Recurse -File -Filter 'TSPrint*.exe' | ForEach-Object {
        W ("Executavel: {0} | versao {1} | alterado {2}" -f $_.FullName, $_.VersionInfo.FileVersion, $_.LastWriteTime)
    }
}

W ""; W "=== 5. TSPrint: arquivos de configuracao (so pastas e impressoras) ==="
foreach ($d in $dirs) {
    Get-ChildItem $d -Recurse -File -Include *.data,*.json,*.ini,*.cfg,*.conf,*.config,*.xml,*.txt,*.db |
        Where-Object { $_.Length -lt 1MB -and $_.Name -notmatch 'geral' -and $_.FullName -notmatch '\\(logs?|node_modules|locales|resources|CustomCache|EBWebView)\\' } |
        ForEach-Object {
            $text = Get-Content $_.FullName -Raw
            if (-not $text) { return }
            if ($text -notmatch 'PRINTERNAME|FOLDER|impressora|printer|pasta') { return }
            W "--- $($_.FullName) ($($_.Length) bytes, alterado $($_.LastWriteTime))"
            # Somente pares pasta/impressora; nada mais do arquivo e mostrado.
            $pairs = [regex]::Matches($text, '"?(FOLDER|PRINTERNAME|pasta|impressora|printer(Name)?|folder)"?\s*[:=]\s*"([^"]{0,120})"', 'IgnoreCase')
            foreach ($m in $pairs) { if ($m.Groups[1].Value -notmatch $secret) { W ("    {0} = {1}" -f $m.Groups[1].Value, $m.Groups[3].Value) } }
            if ($_.Extension -eq '.json') {
                $obj = $text | ConvertFrom-Json
                if ($obj -and -not ($obj -is [array])) { W ("    chaves: {0}" -f (($obj.PSObject.Properties.Name) -join ', ')) }
            }
        }
}

W ""; W "=== 6. Ultimas linhas do log do WP Support Tools (impressao) ==="
$log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'WPSupportTools\Logs') -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1
if ($log) { Get-Content $log.FullName | Select-String 'printing\.|labels\.|TSPrint|Impressora' | Select-Object -Last 25 | ForEach-Object { W $_.Line } }

$lines | Set-Content -Path $out -Encoding UTF8
$lines | ForEach-Object { Write-Host $_ }
Write-Host ""; Write-Host "Salvo em: $out"
