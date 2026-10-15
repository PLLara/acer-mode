# Fase 0: confirma, no PHN16-72, as escritas de efeito do teclado e de ventoinha.
# Toda escrita tem volta: no fim, iluminação estática do modo atual e ventoinhas em Auto.
# Uso (elevado): pwsh -File tools\verify-writes.ps1 -Out tools\verify-writes-<data>.txt
param([string]$Out = "$PSScriptRoot\verify-writes-$(Get-Date -Format yyyyMMdd-HHmmss).txt")

$log = New-Object System.Collections.Generic.List[string]
function L($s) { $line = "$(Get-Date -Format HH:mm:ss.fff) $s"; $log.Add($line); Write-Host $line }

$o = Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction
function Call($m, $inp) { (Invoke-CimMethod -InputObject $o -MethodName $m -Arguments @{ gmInput = $inp }).gmOutput }
function Backlight($effect, $speed, $bright, $dir, $r, $g, $b) {
    [byte[]]$p = @($effect, $speed, $bright, 0, $dir, $r, $g, $b, 3, 1, 0, 0, 0, 0, 0, 0)
    Call SetGamingKBBacklight $p
}
function Sensors {
    $v = @{}
    foreach ($s in @{cpu=0x01; cpuRpm=0x02; gpuRpm=0x06; gpu=0x0A}.GetEnumerator()) {
        $out = Call GetGamingSysInfo ([uint32](0x01 -bor ($s.Value -shl 8)))
        $v[$s.Key] = ($out -shr 8) -band 0xFFFF
    }
    "CPU $($v.cpu)C GPU $($v.gpu)C rpm $($v.cpuRpm)/$($v.gpuRpm)"
}
function FanBehavior { '0x{0:X}' -f (Call GetGamingFanBehavior ([uint32]0x09)) }

try {
    $mode = ((Call GetGamingMiscSetting ([uint32]0x0B)) -shr 8) -band 0xFF
    L "modo atual: $mode"

    $names = 'Static','Breathing','Neon','Wave','Shifting','Zoom','Meteor','Twinkling'
    for ($e = 0; $e -le 7; $e++) {
        $st = Backlight $e 5 100 1 0x28 0x78 0xFF
        L ("efeito {0} ({1}): status {2}" -f $e, $names[$e], $st)
        Start-Sleep -Seconds 3
    }

    L "ventoinhas antes: $(FanBehavior) | $(Sensors)"
    $st = Call SetGamingFanBehavior ([uint64]0x00820009); L "Max: status $st, leitura $(FanBehavior)"
    Start-Sleep -Seconds 5; L "Max depois de 5 s: $(Sensors)"

    $s1 = Call SetGamingFanSpeed ([uint64]0x3C01); $s2 = Call SetGamingFanSpeed ([uint64]0x3C04)
    $st = Call SetGamingFanBehavior ([uint64]0x00C30009)
    L "Custom 60/60: speed $s1/$s2, behavior $st, leitura $(FanBehavior), % $('0x{0:X}' -f (Call GetGamingFanSpeed ([uint32]0x01)))/$('0x{0:X}' -f (Call GetGamingFanSpeed ([uint32]0x04)))"
    Start-Sleep -Seconds 5; L "Custom depois de 5 s: $(Sensors)"
}
catch { L "ERRO $($_.Exception.Message)" }
finally {
    try { $st = Call SetGamingFanBehavior ([uint64]0x00410009); L "Auto: status $st, leitura $(FanBehavior)" } catch { L "ERRO ao voltar ao Auto: $($_.Exception.Message)" }
    try {
        $c = switch ($mode) { 5 {255,45,10,100} 4 {150,70,255,100} 0 {0,190,255,45} 6 {40,220,90,70} default {40,120,255,100} }
        for ($z = 0; $z -lt 4; $z++) { [void](Call SetGamingRgbKb ([uint64]((1 -shl $z) -bor ($c[0] -shl 8) -bor ($c[1] -shl 16) -bor ([uint64]$c[2] -shl 24)))) }
        $st = Backlight 0 0 $c[3] 0 $c[0] $c[1] $c[2]
        L "iluminação restaurada: status $st"
    } catch { L "ERRO ao restaurar iluminação: $($_.Exception.Message)" }
    $log | Set-Content -Path $Out -Encoding UTF8
}
