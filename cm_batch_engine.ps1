param(
    [ValidateSet("Library", "Check", "Create", "Delete", "Connect")]
    [string]$Action = "Library",
    [string]$Customer = "",
    [string]$DnSpec = "",
    [string]$CtiSpec = "",
    [string]$CustomersJsonPath = "",
    [string]$CmExePath = "C:\GCTI\Configuration Manager\sce.exe",
    [string]$CmUser = "",
    [bool]$DoAgentLogin = $true,
    [bool]$DoAcdPosition = $true,
    [bool]$DoAgentDn = $true,
    [bool]$DoPlace = $true
)

$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $ScriptDir "cm_win32_lib.ps1")

if (-not $CustomersJsonPath) {
    $CustomersJsonPath = Join-Path $ScriptDir "cm_customers.json"
}

function Load-CmCustomers {
    param([string]$JsonPath = $CustomersJsonPath)
    if (-not (Test-Path $JsonPath)) { throw "고객사 매핑 파일을 찾을 수 없습니다: $JsonPath" }
    $raw = Get-Content -Path $JsonPath -Raw -Encoding UTF8
    return ($raw | ConvertFrom-Json)
}

function Expand-NumberSpec {
    <#
    .SYNOPSIS
    Parses inputs like "31001", "31001~31005", "31001-31005", or comma/newline separated values into ordered unique strings.
    #>
    param([string]$Spec)
    $list = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Spec)) { return @() }

    $tokens = $Spec -split "[,;\r\n\s\t]+" | Where-Object { $_ -and $_.Trim() -ne "" }
    foreach ($tok in $tokens) {
        $t = $tok.Trim()
        if ($t -match "^(\d+)[~\-](\d+)$") {
            $sStr = $Matches[1]
            $eStr = $Matches[2]
            $sNum = [int64]$sStr
            $eNum = [int64]$eStr
            if ($eNum -lt $sNum) { throw "범위 끝 번호가 시작 번호보다 작습니다: $t" }
            if (($eNum - $sNum + 1) -gt 500) { throw "한 번에 500건을 초과하는 범위는 허용되지 않습니다: $t" }
            $width = $sStr.Length
            for ($n = $sNum; $n -le $eNum; $n++) {
                $list.Add($n.ToString().PadLeft($width, '0'))
            }
        } elseif ($t -match "^\d+$") {
            $list.Add($t)
        } else {
            throw "올바른 숫자 또는 범위 형식이 아닙니다: '$t' (예: 31001 또는 31001~31010)"
        }
    }
    return @($list | Select-Object -Unique)
}

function Resolve-CustomerTreePaths {
    param($CustomerObj)
    $makePath = {
        param([string[]]$Base, $Sub)
        if ($null -eq $Sub) { return $null }
        $arr = @($Base)
        foreach ($s in @($Sub)) { $arr += [string]$s }
        return $arr
    }
    return [PSCustomObject]@{
        Name       = $CustomerObj.name
        AgentLogin = (& $makePath @("Configuration","Environment","Switches","TServerAlcatel","Agent Logins") $CustomerObj.agentLogin)
        AcdPos     = (& $makePath @("Configuration","Environment","Switches","TServerAlcatel","DNs","ACDPosition") $CustomerObj.acdPosition)
        AgentDn    = (& $makePath @("Configuration","Environment","Switches","TServerAlcatel","DNs","AGENT_DN") $CustomerObj.agentDn)
        Place      = (& $makePath @("Configuration","Environment","Places","AGENT") $CustomerObj.place)
        Note       = $CustomerObj.note
    }
}

function Verify-CustomerFoldersInCm {
    param(
        $Session,
        $Paths,
        [bool]$NeedCti,
        [bool]$NeedDn,
        [bool]$IncludeAgentLogin = $true,
        [bool]$IncludeAcdPos = $true,
        [bool]$IncludeAgentDn = $true,
        [bool]$IncludePlace = $true
    )
    $missing = @()
    $nodes = @{}

    $checks = @()
    if ($NeedCti -and $IncludeAgentLogin) {
        $checks += ,@("AgentLogin", $Paths.AgentLogin)
    }
    if ($NeedCti -and $IncludeAcdPos) {
        $checks += ,@("AcdPosition", $Paths.AcdPos)
    }
    if ($NeedDn -and $IncludeAgentDn) {
        $checks += ,@("AgentDN", $Paths.AgentDn)
    }
    if ($NeedDn -and $IncludePlace) {
        $checks += ,@("Place", $Paths.Place)
    }

    foreach ($chk in $checks) {
        $key = $chk[0]
        $pathArr = $chk[1]
        if ($null -eq $pathArr -or $pathArr.Count -eq 0) {
            $missing += "${key}: 매핑 정보 없음(null)"
            continue
        }
        $node = Select-CmTreePath $Session $pathArr
        if ($node -eq [IntPtr]::Zero) {
            $missing += "${key}: CM 폴더를 찾을 수 없음 ($($pathArr -join ' > '))"
        } else {
            $nodes[$key] = $node
        }
    }

    return [PSCustomObject]@{
        Ok      = ($missing.Count -eq 0)
        Missing = $missing
        Nodes   = $nodes
    }
}

function Invoke-CmBatchCheck {
    param(
        $Session,
        [string[]]$DnList,
        [string[]]$CtiList,
        [scriptblock]$Log = { param($m) [Console]::WriteLine($m) },
        [bool]$IncludeAgentLogin = $true,
        [bool]$IncludeAcdPos = $true,
        [bool]$IncludeAgentDn = $true,
        [bool]$IncludePlace = $true
    )
    $queries = @()
    foreach ($id in $CtiList) {
        if ($IncludeAgentLogin) { $queries += [PSCustomObject]@{ Category = "Agent Login"; TypeIndex = 3; Name = $id } }
        if ($IncludeAcdPos) { $queries += [PSCustomObject]@{ Category = "ACD Position (DN)"; TypeIndex = 14; Name = $id } }
    }
    foreach ($dn in $DnList) {
        if ($IncludeAgentDn) { $queries += [PSCustomObject]@{ Category = "Agent DN"; TypeIndex = 14; Name = $dn } }
        if ($IncludePlace) { $queries += [PSCustomObject]@{ Category = "Place"; TypeIndex = 27; Name = "P$dn" } }
    }

    & $Log "[내선/CTI ID 검색] Find Configuration Object 전역 검색 (총 $($queries.Count)개 항목)..."
    $findRes = Invoke-CmGlobalFind $Session $queries
    $existing = @($findRes | Where-Object { $_.Count -gt 0 })
    if ($existing.Count -eq 0) {
        & $Log "  [OK] 미사용 상태 (전체 CM 미등록)."
    } else {
        foreach ($ex in $existing) {
            & $Log "  [검색됨] $($ex.Category) '$($ex.Name)' -> $($ex.Count)건 ($($ex.Matches -join '; '))"
        }
    }

    return [PSCustomObject]@{
        FoldersOk     = $true
        MissingFolder = @()
        FindResults   = $findRes
        Duplicates    = $existing
    }
}

function Invoke-CmRangeAgentLogin {
    param($Session, [IntPtr]$FolderNode, $RangeObj)
    $curSel = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]9, [IntPtr]::Zero)
    if ($curSel -ne $FolderNode) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $FolderNode) | Out-Null
        Start-Sleep -Milliseconds 250
    }
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8020, [IntPtr]::Zero) | Out-Null
    $dlg = Wait-CmDialogOpen 5000
    if ($dlg -eq [IntPtr]::Zero) { return [PSCustomObject]@{ Ok = $false; Error = "Add a Range of Agent Logins 창 열기 실패" } }

    $cs = Get-CmDialogControls $dlg
    [SceWin32]::SetEditText($cs["Edit/1012"], [string]$RangeObj.Start)
    [SceWin32]::SetEditText($cs["Edit/1013"], [string]$RangeObj.Count)
    [SceWin32]::SetEditText($cs["Edit/1014"], [string]$RangeObj.Digits)
    [SceWin32]::PostMessage($cs["Button/1"], 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    return (Wait-CmOperationComplete 20000)
}

function Invoke-CmRangeDn {
    param($Session, [IntPtr]$FolderNode, $RangeObj, [int]$DnTypeComboIdx)
    $curSel = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]9, [IntPtr]::Zero)
    if ($curSel -ne $FolderNode) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $FolderNode) | Out-Null
        Start-Sleep -Milliseconds 250
    }
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8021, [IntPtr]::Zero) | Out-Null
    $dlg = Wait-CmDialogOpen 5000
    if ($dlg -eq [IntPtr]::Zero) { return [PSCustomObject]@{ Ok = $false; Error = "Add a Range of DNs 창 열기 실패" } }

    $cs = Get-CmDialogControls $dlg
    [SceWin32]::SetEditText($cs["Edit/1012"], [string]$RangeObj.Start)
    [SceWin32]::SetEditText($cs["Edit/1013"], [string]$RangeObj.Count)
    [SceWin32]::SetEditText($cs["Edit/1015"], [string]$RangeObj.Digits)
    [SceWin32]::SelectComboIndex($cs["ComboBox/1070"], $DnTypeComboIdx) # 1=ACD Position, 8=Extension
    [SceWin32]::SetEditText($cs["Edit/1016"], "1") # Switch-specific Type = 1
    [SceWin32]::PostMessage($cs["Button/1"], 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    return (Wait-CmOperationComplete 20000)
}

function Invoke-CmCreatePlace {
    param($Session, [IntPtr]$FolderNode, [string]$PlaceName)
    $curSel = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]9, [IntPtr]::Zero)
    if ($curSel -ne $FolderNode) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $FolderNode) | Out-Null
        Start-Sleep -Milliseconds 200
    }
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8002, [IntPtr]::Zero) | Out-Null
    $dlg = Wait-CmDialogOpen 5000
    if ($dlg -eq [IntPtr]::Zero) { return [PSCustomObject]@{ Ok = $false; Error = "New Place 창 열기 실패" } }

    $cs = Get-CmDialogControls $dlg
    [SceWin32]::SetEditText($cs["Edit/1001"], $PlaceName)
    [SceWin32]::PostMessage($cs["Button/1"], 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    return (Wait-CmOperationComplete 15000)
}

function Invoke-CmLinkDnShortcutToPlace {
    param(
        $Session,
        [IntPtr]$AgentDnNode,
        [hashtable]$DnListMap,
        [IntPtr]$PlaceNode,
        [string]$DnNumber
    )

    $curSel = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]9, [IntPtr]::Zero)
    if ($curSel -ne $AgentDnNode) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $AgentDnNode) | Out-Null
        Start-Sleep -Milliseconds 180
    }

    $idx = if ($DnListMap -and $DnListMap.ContainsKey($DnNumber)) { [int]$DnListMap[$DnNumber] } else { -1 }
    if ($idx -lt 0 -or ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $idx, 0) -ne $DnNumber)) {
        $idx = Find-CmListItemIndex $Session $DnNumber 0
    }
    if ($idx -lt 0) { return [PSCustomObject]@{ Ok = $false; Error = "AGENT_DN 폴더에서 DN '$DnNumber'를 찾지 못함" } }

    [SceWin32]::SelectListItem($Session.HProc, $Session.ListHwnd, $idx)
    [SceWin32]::PostMessage($Session.ListHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 90
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]57634, [IntPtr]::Zero) | Out-Null # Copy
    Start-Sleep -Milliseconds 120

    if ([System.Threading.Thread]::CurrentThread.ApartmentState -eq [System.Threading.ApartmentState]::STA) {
        try {
            $fmtOk = $false
            for ($w = 0; $w -lt 12; $w++) {
                $fmts = ([System.Windows.Forms.Clipboard]::GetDataObject()).GetFormats()
                if ($fmts -contains "SCE Object Data format") { $fmtOk = $true; break }
                Start-Sleep -Milliseconds 60
            }
            if (-not $fmtOk) {
                [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8007, [IntPtr]::Zero) | Out-Null
                Start-Sleep -Milliseconds 200
            }
        } catch {}
    }

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $PlaceNode) | Out-Null
    Start-Sleep -Milliseconds 180

    # Check if shortcut already exists inside Place
    if ((Find-CmListItemIndex $Session $DnNumber 0) -ge 0) {
        return [PSCustomObject]@{ Ok = $true; AlreadyExisted = $true }
    }

    [SceWin32]::PostMessage($Session.TreeHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 80
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]57637, [IntPtr]::Zero) | Out-Null # Paste

    for ($w = 0; $w -lt 25; $w++) {
        Start-Sleep -Milliseconds 120
        $waitRes = Wait-CmOperationComplete 2000
        if (-not $waitRes.Ok) { return $waitRes }
        if ((Find-CmListItemIndex $Session $DnNumber 0) -ge 0) {
            return [PSCustomObject]@{ Ok = $true; Error = $null }
        }
    }
    return [PSCustomObject]@{ Ok = $false; Error = "Place 'P$DnNumber'에 DN Shortcut '$DnNumber' 붙여넣기 검증 실패" }
}

function Invoke-CmBulkDeleteInFolder {
    param(
        $Session,
        [IntPtr]$FolderNode,
        [string]$CategoryLabel,
        [string]$StepPrefix,
        [string[]]$TargetNames,
        [scriptblock]$Log = { param($m) [Console]::WriteLine($m) },
        [string]$CustomerName = "",
        [string]$FolderPath = ""
    )
    $results = New-Object System.Collections.Generic.List[object]
    if (-not $TargetNames -or $TargetNames.Count -eq 0) { return $results }

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $FolderNode) | Out-Null
    Start-Sleep -Milliseconds 300

    $map = Get-CmListItemIndexMap $Session 0
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($name in $TargetNames) {
        if ($map.ContainsKey($name)) {
            $found.Add([PSCustomObject]@{ Name = $name; Index = [int]$map[$name] })
        } else {
            & $Log "  [건너뜀] $CategoryLabel '$name' -> 폴더 내 미존재"
            $results.Add([PSCustomObject]@{ Category = $CategoryLabel; Name = $name; Status = "Skipped"; Detail = "폴더 내 미존재" })
        }
    }

    if ($found.Count -eq 0) { return $results }

    [int[]]$indices = @($found | ForEach-Object { $_.Index })
    $previewNames = if ($found.Count -le 8) { ($found.Name -join ", ") } else { (($found.Name[0..7] -join ", ") + " 외 $($found.Count - 8)건") }
    & $Log "$StepPrefix 대상 $($found.Count)건 일괄 선택 삭제 중... ($previewNames)"

    [SceWin32]::SelectListItems($Session.HProc, $Session.ListHwnd, $indices)
    $selCount = [int][SceWin32]::SendMessage($Session.ListHwnd, 0x1032, [IntPtr]::Zero, [IntPtr]::Zero) # LVM_GETSELECTEDCOUNT
    if ($selCount -ne $indices.Count) {
        throw "$CategoryLabel 다중 선택 개수 불일치 (예상: $($indices.Count), 실제: $selCount)"
    }

    # Verify exact selected row indices match target list
    $expectedSet = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($ix in $indices) { [void]$expectedSet.Add($ix) }
    $cur = -1
    $verifiedCnt = 0
    while ($true) {
        $rawNxt = [SceWin32]::SendMessage($Session.ListHwnd, 0x100C, [IntPtr]$cur, [IntPtr]2).ToInt64() # LVNI_SELECTED
        if ($rawNxt -lt 0 -or $rawNxt -eq 0xFFFFFFFF -or $rawNxt -ge [int]::MaxValue) { break }
        $nxt = [int]$rawNxt
        if (-not $expectedSet.Contains($nxt)) {
            throw "$CategoryLabel 예기치 않은 행($nxt)이 선택되어 삭제를 중단했습니다."
        }
        $verifiedCnt++
        $cur = $nxt
    }
    if ($verifiedCnt -ne $indices.Count) {
        throw "$CategoryLabel 선택 행 검증 실패 (예상: $($indices.Count), 확인: $verifiedCnt)"
    }

    [SceWin32]::PostMessage($Session.ListHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 100
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8005, [IntPtr]::Zero) | Out-Null # Delete

    $confDlg = Wait-CmDialogOpen 5000
    if ($confDlg -eq [IntPtr]::Zero) {
        throw "$CategoryLabel 삭제 확인 창이 열리지 않았습니다."
    }
    $cs = Get-CmDialogControls $confDlg
    $yesBtn = $cs["Button/1"]
    if (-not $yesBtn) { $yesBtn = $cs["Button/6"] }
    if (-not $yesBtn) {
        [SceWin32]::PostMessage($confDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "$CategoryLabel 삭제 확인(&Yes) 버튼을 찾지 못했습니다."
    }
    [SceWin32]::PostMessage($yesBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 200

    $done = Wait-CmOperationComplete 60000
    if (-not $done.Ok) {
        throw "$CategoryLabel 일괄 삭제 오류: $($done.Error)"
    }

    Start-Sleep -Milliseconds 220
    $mapAfter = Get-CmListItemIndexMap $Session 0
    $remaining = @($found | Where-Object { $mapAfter.ContainsKey($_.Name) })
    if ($remaining.Count -gt 0) {
        $remNames = ($remaining | ForEach-Object { $_.Name }) -join ", "
        throw "$CategoryLabel 일괄 삭제 후 잔존 항목 발견: $remNames"
    }

    $auditType = switch ($CategoryLabel) {
        "Agent DN"     { "DN (Extension)" }
        "ACD Position" { "DN (ACD Position)" }
        default        { $CategoryLabel }
    }
    foreach ($item in $found) {
        $results.Add([PSCustomObject]@{ Category = $CategoryLabel; Name = $item.Name; Status = "Deleted"; Detail = $null })
        Write-CmAuditEvent -Action "DELETE" -Target "CM" -ObjectType $auditType -Name $item.Name -Customer $CustomerName -Path $FolderPath
    }
    & $Log "  [완료] $CategoryLabel $($found.Count)건 일괄 삭제 완료"
    return $results
}

function Invoke-CmBatchCreate {
    param(
        $Session,
        $CustomerObj,
        [string[]]$DnList,
        [string[]]$CtiList,
        [switch]$SkipDuplicateCheck,
        [scriptblock]$Log = { param($m) [Console]::WriteLine($m) },
        [bool]$IncludeAgentLogin = $true,
        [bool]$IncludeAcdPos = $true,
        [bool]$IncludeAgentDn = $true,
        [bool]$IncludePlace = $true
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $paths = Resolve-CustomerTreePaths $CustomerObj
    $needLogin = ($CtiList.Count -gt 0) -and $IncludeAgentLogin
    $needAcd   = ($CtiList.Count -gt 0) -and $IncludeAcdPos
    $needDnObj = ($DnList.Count -gt 0) -and $IncludeAgentDn
    $needPlace = ($DnList.Count -gt 0) -and $IncludePlace

    & $Log "[대상 폴더] 고객사 '$($CustomerObj.name)' 폴더 존재 여부 확인 중..."
    $folderCheck = Verify-CustomerFoldersInCm $Session $paths ($CtiList.Count -gt 0) ($DnList.Count -gt 0) $IncludeAgentLogin $IncludeAcdPos $IncludeAgentDn $IncludePlace
    if (-not $folderCheck.Ok) {
        throw ("대상 CM 폴더 검증 실패:`n - " + ($folderCheck.Missing -join "`n - "))
    }
    & $Log "  [OK] 대상 폴더 확인 완료."

    if (-not $SkipDuplicateCheck) {
        $chk = Invoke-CmBatchCheck -Session $Session -DnList $DnList -CtiList $CtiList -Log $Log -IncludeAgentLogin $IncludeAgentLogin -IncludeAcdPos $IncludeAcdPos -IncludeAgentDn $IncludeAgentDn -IncludePlace $IncludePlace
        if ($chk.Duplicates.Count -gt 0) {
            $dupDesc = ($chk.Duplicates | ForEach-Object { "$($_.Category): $($_.Name)" }) -join ", "
            throw "중복 객체가 발견되어 생성을 중단했습니다: $dupDesc"
        }
    }

    $results = New-Object System.Collections.Generic.List[object]
    $formatRangeDesc = {
        param($r)
        if ($r.Count -gt 1) {
            $endStr = ([int64]$r.Start + $r.Count - 1).ToString().PadLeft($r.Digits, '0')
            return "범위생성 $($r.Start)~$endStr ($($r.Count)건)"
        }
        return "단건 $($r.Start)"
    }

    $custName = [string]$CustomerObj.name
    $agPath  = if ($paths.AgentLogin) { ($paths.AgentLogin -join " > ") } else { "" }
    $acdPath = if ($paths.AcdPos)     { ($paths.AcdPos -join " > ") }     else { "" }
    $dnPath  = if ($paths.AgentDn)    { ($paths.AgentDn -join " > ") }    else { "" }
    $plPath  = if ($paths.Place)      { ($paths.Place -join " > ") }      else { "" }

    if ($needLogin -or $needAcd) {
        $ctiRanges = Group-ContiguousNumbers $CtiList
        if ($needLogin) {
            $nAg = $folderCheck.Nodes["AgentLogin"]
            foreach ($r in $ctiRanges) {
                $desc = & $formatRangeDesc $r
                & $Log "[1/4 Agent Login] $desc 중..."
                $res = Invoke-CmRangeAgentLogin $Session $nAg $r
                if (-not $res.Ok) { throw "Agent Login 생성 실패 ($desc): $($res.Error)" }
                & $Log "  [완료] Agent Login $desc"
                $sNum = [int64]$r.Start
                for ($ri = 0; $ri -lt $r.Count; $ri++) {
                    $itemId = ($sNum + $ri).ToString().PadLeft($r.Digits, '0')
                    Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Agent Login" -Name $itemId -Customer $custName -Path $agPath
                }
            }
        }

        if ($needAcd) {
            $nAcd = $folderCheck.Nodes["AcdPosition"]
            foreach ($r in $ctiRanges) {
                $desc = & $formatRangeDesc $r
                & $Log "[2/4 ACD Position] $desc 중..."
                $res = Invoke-CmRangeDn $Session $nAcd $r 1
                if (-not $res.Ok) { throw "ACD Position 생성 실패 ($desc): $($res.Error)" }
                & $Log "  [완료] ACD Position $desc"
                $sNum = [int64]$r.Start
                for ($ri = 0; $ri -lt $r.Count; $ri++) {
                    $itemId = ($sNum + $ri).ToString().PadLeft($r.Digits, '0')
                    Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "DN (ACD Position)" -Name $itemId -Customer $custName -Path $acdPath
                }
            }
        }
    }

    if ($needDnObj -or $needPlace) {
        $nDn = $folderCheck.Nodes["AgentDN"]
        if ($needDnObj) {
            $dnRanges = Group-ContiguousNumbers $DnList
            foreach ($r in $dnRanges) {
                $desc = & $formatRangeDesc $r
                & $Log "[3/4 Agent DN] $desc 중..."
                $res = Invoke-CmRangeDn $Session $nDn $r 8
                if (-not $res.Ok) { throw "Agent DN 생성 실패 ($desc): $($res.Error)" }
                & $Log "  [완료] Agent DN $desc"
                $sNum = [int64]$r.Start
                for ($ri = 0; $ri -lt $r.Count; $ri++) {
                    $itemId = ($sNum + $ri).ToString().PadLeft($r.Digits, '0')
                    Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "DN (Extension)" -Name $itemId -Customer $custName -Path $dnPath
                }
            }
        }

        if ($needPlace) {
            $nPl = $folderCheck.Nodes["Place"]
            [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $nPl) | Out-Null
            Start-Sleep -Milliseconds 220

            $createdPlaceDns = New-Object System.Collections.Generic.List[string]
            $emittedPlaceDns = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
            try {
                & $Log "[4/4 Place] $($DnList.Count)건 일괄 생성 중..."
                foreach ($dn in $DnList) {
                    $pName = "P$dn"
                    $resP = Invoke-CmCreatePlace $Session $nPl $pName
                    if (-not $resP.Ok) { throw "Place 생성 실패 ($pName): $($resP.Error)" }
                    & $Log "  [Place 생성] $pName"
                    $createdPlaceDns.Add($dn)
                }

                $placeNodeMap = Get-CmTreeChildMap $Session $nPl -Refresh
                $missingNode = @($DnList | Where-Object { -not $placeNodeMap.ContainsKey("P$_") })
                if ($missingNode.Count -gt 0) {
                    Start-Sleep -Milliseconds 250
                    $placeNodeMap = Get-CmTreeChildMap $Session $nPl -Refresh
                }

                if ($null -eq $nDn -or $nDn -eq [IntPtr]::Zero) {
                    if ($paths.AgentDn) {
                        $nDn = Select-CmTreePath $Session $paths.AgentDn
                    }
                }

                if ($nDn -and $nDn -ne [IntPtr]::Zero) {
                    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $nDn) | Out-Null
                    Start-Sleep -Milliseconds 250
                    $dnListMap = Get-CmListItemIndexMap $Session 0

                    & $Log "[4/4 DN Shortcut] $($DnList.Count)건 Place 연결 확인 중..."
                    foreach ($dn in $DnList) {
                        $pName = "P$dn"
                        if (-not $placeNodeMap.ContainsKey($pName)) {
                            throw "Place '$pName' 트리 노드를 찾지 못했습니다."
                        }
                        if ($dnListMap.ContainsKey($dn)) {
                            $resSc = Invoke-CmLinkDnShortcutToPlace $Session $nDn $dnListMap $placeNodeMap[$pName] $dn
                            if (-not $resSc.Ok) { throw "DN Shortcut 연결 실패 ($pName <- $dn): $($resSc.Error)" }
                            & $Log "  [연결 완료] $pName <- $dn"
                            $results.Add([PSCustomObject]@{ Type = "DN+Place"; Key = "$dn / $pName"; Status = "Created" })
                            if ($emittedPlaceDns.Add($dn)) {
                                Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Place" -Name $pName -Detail ("DN Shortcut=" + $dn) -Customer $custName -Path $plPath
                            }
                        } elseif (-not $needDnObj) {
                            & $Log "  [안내] AGENT_DN에 '$dn' 미존재 -> Place($pName) 단독 생성됨"
                            $results.Add([PSCustomObject]@{ Type = "Place"; Key = $pName; Status = "Created" })
                            if ($emittedPlaceDns.Add($dn)) {
                                Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Place" -Name $pName -Detail "단독 생성" -Customer $custName -Path $plPath
                            }
                        } else {
                            throw "AGENT_DN 폴더에서 DN '$dn'를 찾지 못함"
                        }
                    }
                } else {
                    foreach ($dn in $DnList) {
                        & $Log "  [안내] AGENT_DN 폴더 미선택/미존재 -> Place(P$dn) 단독 생성됨"
                        $results.Add([PSCustomObject]@{ Type = "Place"; Key = "P$dn"; Status = "Created" })
                        if ($emittedPlaceDns.Add($dn)) {
                            Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Place" -Name "P$dn" -Detail "단독 생성" -Customer $custName -Path $plPath
                        }
                    }
                }
            } finally {
                foreach ($remDn in $createdPlaceDns) {
                    if (-not $emittedPlaceDns.Contains($remDn)) {
                        Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Place" -Name "P$remDn" -Detail "Shortcut 미연결" -Customer $custName -Path $plPath
                    }
                }
            }
        }
    }

    if ($needLogin -or $needAcd) {
        foreach ($id in $CtiList) {
            $results.Add([PSCustomObject]@{ Type = "CTI_ID"; Key = $id; Status = "Created" })
        }
    }

    $sw.Stop()
    & $Log ("[전체 생성 완료] 소요 시간: {0:N1}초" -f $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        ElapsedSeconds = $sw.Elapsed.TotalSeconds
        Results        = $results
    }
}

function Build-CustomerFolderLookup {
    param($AllCustomers)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if (-not $AllCustomers) { return $map }
    foreach ($c in $AllCustomers) {
        $p = Resolve-CustomerTreePaths $c
        $pairs = @(
            @("Place", $p.Place),
            @("AgentDN", $p.AgentDn),
            @("ACDPos", $p.AcdPos),
            @("AgentLogin", $p.AgentLogin)
        )
        foreach ($pair in $pairs) {
            if ($pair[1]) {
                $k = $pair[0] + "|" + ($pair[1] -join " > ")
                if (-not $map.ContainsKey($k)) { $map[$k] = [string]$c.name }
            }
        }
    }
    return $map
}

function Invoke-CmBatchDelete {
    param(
        $Session,
        $CustomerObj = $null,
        [string[]]$DnList,
        [string[]]$CtiList,
        [scriptblock]$Log = { param($m) [Console]::WriteLine($m) },
        [bool]$IncludeAgentLogin = $true,
        [bool]$IncludeAcdPos = $true,
        [bool]$IncludeAgentDn = $true,
        [bool]$IncludePlace = $true,
        $AllCustomers = $null
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $needLogin = ($CtiList.Count -gt 0) -and $IncludeAgentLogin
    $needAcd   = ($CtiList.Count -gt 0) -and $IncludeAcdPos
    $needDnObj = ($DnList.Count -gt 0) -and $IncludeAgentDn
    $needPlace = ($DnList.Count -gt 0) -and $IncludePlace

    if (-not $AllCustomers) {
        try { $AllCustomers = Load-CmCustomers } catch { $AllCustomers = @() }
    }
    $folderCustLookup = Build-CustomerFolderLookup $AllCustomers

    # Build global Find queries regardless of selected customer
    $queries = New-Object System.Collections.Generic.List[object]
    if ($needPlace) {
        foreach ($dn in $DnList) {
            $queries.Add([PSCustomObject]@{ Kind = "Place"; Category = "Place"; TypeIndex = 27; Name = "P$dn"; ExpectedDnType = ""; PathCol = 1 })
        }
    }
    if ($needDnObj) {
        foreach ($dn in $DnList) {
            $queries.Add([PSCustomObject]@{ Kind = "AgentDN"; Category = "Agent DN"; TypeIndex = 14; Name = $dn; ExpectedDnType = "Extension"; PathCol = 4 })
        }
    }
    if ($needAcd) {
        foreach ($id in $CtiList) {
            $queries.Add([PSCustomObject]@{ Kind = "ACDPos"; Category = "ACD Position"; TypeIndex = 14; Name = $id; ExpectedDnType = "ACD Position"; PathCol = 4 })
        }
    }
    if ($needLogin) {
        foreach ($id in $CtiList) {
            $queries.Add([PSCustomObject]@{ Kind = "AgentLogin"; Category = "Agent Login"; TypeIndex = 3; Name = $id; ExpectedDnType = ""; PathCol = 2 })
        }
    }

    $results = New-Object System.Collections.Generic.List[object]
    if ($queries.Count -eq 0) {
        $sw.Stop()
        return [PSCustomObject]@{ ElapsedSeconds = $sw.Elapsed.TotalSeconds; Results = $results }
    }

    & $Log "[삭제 대상 탐색] 센터 무관 CM 전역 검색 중... (총 $($queries.Count)건)"
    $findRes = Invoke-CmGlobalFind $Session @($queries.ToArray())

    # Organize resolved folders per stage in strict reverse delete order:
    # 1. Place (deletes internal Shortcut automatically) -> 2. Agent DN -> 3. ACD Position -> 4. Agent Login
    $stageOrder = @(
        [PSCustomObject]@{ Kind = "Place";      Category = "Place";        Prefix = "[1/4 Place 일괄 삭제]" },
        [PSCustomObject]@{ Kind = "AgentDN";    Category = "Agent DN";     Prefix = "[2/4 Agent DN 일괄 삭제]" },
        [PSCustomObject]@{ Kind = "ACDPos";     Category = "ACD Position"; Prefix = "[3/4 ACD Position 일괄 삭제]" },
        [PSCustomObject]@{ Kind = "AgentLogin"; Category = "Agent Login";  Prefix = "[4/4 Agent Login 일괄 삭제]" }
    )

    foreach ($st in $stageOrder) {
        $byFolder = [ordered]@{}
        $missing = New-Object System.Collections.Generic.List[string]
        for ($qi = 0; $qi -lt $queries.Count; $qi++) {
            $q = $queries[$qi]
            if ($q.Kind -ne $st.Kind) { continue }
            $fr = $findRes[$qi]
            $matchedAny = $false
            if ($fr -and $fr.Rows) {
                foreach ($row in $fr.Rows) {
                    $cols = $row.Cols
                    if (-not $cols -or $cols.Length -le $q.PathCol) { continue }
                    if (-not [string]::Equals([string]$cols[0], [string]$q.Name, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
                    if ($q.ExpectedDnType -and -not [string]::Equals([string]$cols[1], [string]$q.ExpectedDnType, [System.StringComparison]::OrdinalIgnoreCase)) { continue }

                    $rawPath = ([string]$cols[$q.PathCol]).Trim('\')
                    if (-not $rawPath) { continue }
                    $parts = @($rawPath -split '\\' | Where-Object { $_ -and $_.Trim() -ne "" })
                    if ($parts.Count -lt 2) { continue }
                    $dispPath = ($parts -join " > ")
                    if (-not $byFolder.Contains($dispPath)) {
                        $byFolder[$dispPath] = [PSCustomObject]@{
                            DisplayPath = $dispPath
                            PathParts   = $parts
                            Names       = (New-Object System.Collections.Generic.List[string])
                            Seen        = (New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase))
                        }
                    }
                    if ($byFolder[$dispPath].Seen.Add([string]$cols[0])) {
                        $byFolder[$dispPath].Names.Add([string]$cols[0])
                    }
                    $matchedAny = $true
                }
            }
            if (-not $matchedAny) {
                $missing.Add($q.Name)
            }
        }

        foreach ($missName in $missing) {
            & $Log "  [건너뜀] $($st.Category) '$missName' -> CM 전체 미존재"
            $results.Add([PSCustomObject]@{ Category = $st.Category; Name = $missName; Status = "Skipped"; Detail = "CM 전체 미존재" })
        }

        foreach ($fKey in $byFolder.Keys) {
            $entry = $byFolder[$fKey]
            $folderNode = Select-CmTreePath $Session $entry.PathParts
            if ($folderNode -eq [IntPtr]::Zero) {
                throw "$($st.Category) 대상 폴더를 CM 트리에서 열 수 없습니다: $($entry.DisplayPath)"
            }
            $lookupKey = $st.Kind + "|" + $entry.DisplayPath
            $resolvedCust = if ($folderCustLookup.ContainsKey($lookupKey)) {
                $folderCustLookup[$lookupKey]
            } elseif ($CustomerObj) {
                [string]$CustomerObj.name
            } else {
                ""
            }
            $delRes = Invoke-CmBulkDeleteInFolder $Session $folderNode $st.Category $st.Prefix @($entry.Names.ToArray()) $Log $resolvedCust $entry.DisplayPath
            foreach ($item in $delRes) { $results.Add($item) }
        }
    }

    $sw.Stop()
    & $Log ("[전체 삭제 완료] 소요 시간: {0:N1}초" -f $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        ElapsedSeconds = $sw.Elapsed.TotalSeconds
        Results        = $results
    }
}

# CLI entry point if not dot-sourced in Library mode
if ($Action -ne "Library") {
    $passPlain = if ($env:CM_PASS_TEMP) { $env:CM_PASS_TEMP } else { "password" }
    $env:CM_PASS_TEMP = $null

    $session = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $session = Connect-CmSession -ExePath $CmExePath -UserName $CmUser -Password $passPlain
        if ($Action -eq "Connect") {
            # Quiet success; UI logs clean connection status
        } elseif ($Action -eq "Check") {
            $dnList = Expand-NumberSpec $DnSpec
            $ctiList = Expand-NumberSpec $CtiSpec
            if ($dnList.Count -eq 0 -and $ctiList.Count -eq 0) { throw "처리할 DN 또는 CTI ID 번호를 입력하세요." }

            $chkRes = Invoke-CmBatchCheck -Session $session -DnList $dnList -CtiList $ctiList -IncludeAgentLogin $DoAgentLogin -IncludeAcdPos $DoAcdPosition -IncludeAgentDn $DoAgentDn -IncludePlace $DoPlace
            $summaryPayload = [PSCustomObject]@{
                Customer      = ""
                FoldersOk     = $true
                MissingFolder = @()
                DnList        = @($dnList)
                CtiList       = @($ctiList)
                DoAgentDn     = [bool]$DoAgentDn
                DoPlace       = [bool]$DoPlace
                DoAgentLogin  = [bool]$DoAgentLogin
                DoAcdPos      = [bool]$DoAcdPosition
                FindResults   = @($chkRes.FindResults | ForEach-Object {
                    [PSCustomObject]@{
                        Category = [string]$_.Category
                        Name     = [string]$_.Name
                        Count    = [int]$_.Count
                        Matches  = @($_.Matches)
                    }
                })
            }
            [Console]::WriteLine("##CM_CHECK_JSON##" + ($summaryPayload | ConvertTo-Json -Compress -Depth 6))
        } elseif ($Action -eq "Delete") {
            $customers = @()
            try { $customers = Load-CmCustomers } catch { $customers = @() }
            $custObj = if ($Customer) { $customers | Where-Object { $_.name -eq $Customer } | Select-Object -First 1 } else { $null }

            $dnList = Expand-NumberSpec $DnSpec
            $ctiList = Expand-NumberSpec $CtiSpec
            if ($dnList.Count -eq 0 -and $ctiList.Count -eq 0) { throw "처리할 DN 또는 CTI ID 번호를 입력하세요." }

            Invoke-CmBatchDelete $session $custObj $dnList $ctiList -IncludeAgentLogin $DoAgentLogin -IncludeAcdPos $DoAcdPosition -IncludeAgentDn $DoAgentDn -IncludePlace $DoPlace -AllCustomers $customers | Out-Null
        } else {
            $customers = Load-CmCustomers
            $custObj = $customers | Where-Object { $_.name -eq $Customer } | Select-Object -First 1
            if (-not $custObj) { throw "고객사 '$Customer'를 cm_customers.json에서 찾을 수 없습니다." }

            $dnList = Expand-NumberSpec $DnSpec
            $ctiList = Expand-NumberSpec $CtiSpec
            if ($dnList.Count -eq 0 -and $ctiList.Count -eq 0) { throw "처리할 DN 또는 CTI ID 번호를 입력하세요." }

            if ($Action -eq "Create") {
                Invoke-CmBatchCreate $session $custObj $dnList $ctiList -IncludeAgentLogin $DoAgentLogin -IncludeAcdPos $DoAcdPosition -IncludeAgentDn $DoAgentDn -IncludePlace $DoPlace | Out-Null
            }
        }
    } catch {
        $detParts = New-Object System.Collections.Generic.List[string]
        if ($DnSpec)  { $detParts.Add("DN=[" + $DnSpec + "]") }
        if ($CtiSpec) { $detParts.Add("CTI=[" + $CtiSpec + "]") }
        Write-CmAuditEvent -Action "ERROR" -Target "CM" -Operation ("Batch:" + $Action) -Customer $Customer -Detail ($detParts -join " ") -Reason $_.Exception.Message
        throw
    } finally {
        if ($session) { Disconnect-CmSession $session }
    }
}
