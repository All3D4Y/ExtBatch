param(
    [ValidateSet("Library", "CheckCenter", "CreateCenter", "CheckCenterOptions", "ApplyCenterOptions", "DeleteCenterArtifacts")]
    [string]$Action = "Library",
    [string]$Customer = "",
    [string]$CustomersJsonPath = "",
    [string]$CmExePath = "C:\GCTI\Configuration Manager\sce.exe",
    [string]$CmUser = ""
)

$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Preserve own parameters before dot-sourcing cm_batch_engine.ps1
$_savedAction = $Action
$_savedCustomer = $Customer
$_savedCustomersJsonPath = $CustomersJsonPath
$_savedCmExePath = $CmExePath
$_savedCmUser = $CmUser

. (Join-Path $ScriptDir "cm_batch_engine.ps1") -Action Library

$Action = $_savedAction
$Customer = $_savedCustomer
$CustomersJsonPath = $_savedCustomersJsonPath
$CmExePath = $_savedCmExePath
$CmUser = $_savedCmUser

if (-not $CustomersJsonPath) {
    $CustomersJsonPath = Join-Path $ScriptDir "cm_customers.json"
}

function Close-AllSceCenterModals {
    for ($round = 0; $round -lt 4; $round++) {
        $dlgs = @(Get-SceDialogs)
        if ($dlgs.Count -eq 0) { break }
        foreach ($d in $dlgs) {
            [SceWin32]::PostMessage($d, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null # WM_CLOSE
        }
        Start-Sleep -Milliseconds 120
    }
}

function Get-CenterCmTargetSpecs {
    <#
    .SYNOPSIS
    Central table of CM targets under Configuration\Environment for center provisioning.
    Flip UsesLocation to $false in future when location/floor folder layer is retired.
    #>
    return @(
        [PSCustomObject]@{ Key = "Persons";     Label = "Persons 폴더";                 Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Persons") },
        [PSCustomObject]@{ Key = "PlaceAgent";  Label = "Places (AGENT) 폴더";          Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Places","AGENT") },
        [PSCustomObject]@{ Key = "AgentLogin";  Label = "Agent Logins 폴더";            Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Switches","TServerAlcatel","Agent Logins") },
        [PSCustomObject]@{ Key = "AcdPos";      Label = "ACDPosition 폴더";             Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Switches","TServerAlcatel","DNs","ACDPosition") },
        [PSCustomObject]@{ Key = "AgentDn";     Label = "AGENT_DN 폴더";                Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Switches","TServerAlcatel","DNs","AGENT_DN") },
        [PSCustomObject]@{ Key = "Rsi";         Label = "RSI 폴더";                     Kind = "Folder";     UsesLocation = $true;  Condition = "Always"; Base = @("Configuration","Environment","Switches","TServerAlcatel","DNs","RSI") },
        [PSCustomObject]@{ Key = "AgentGroups"; Label = "Agent Groups 폴더";            Kind = "Folder";     UsesLocation = $false; Condition = "Always"; Base = @("Configuration","Environment","Agent Groups") },
        [PSCustomObject]@{ Key = "DnGroups";    Label = "DN Groups 폴더";               Kind = "Folder";     UsesLocation = $false; Condition = "Always"; Base = @("Configuration","Environment","DN Groups") },
        [PSCustomObject]@{ Key = "Scripts";     Label = "Scripts (유세스파트너스) 폴더";  Kind = "Folder";     UsesLocation = $false; Condition = "Always"; Base = @("Configuration","Environment","Scripts","유세스파트너스") },
        [PSCustomObject]@{ Key = "VQueue";      Label = "VQueue 폴더";                  Kind = "Folder";     UsesLocation = $false; Condition = "VQueue"; Base = @("Configuration","Environment","Switches","TServerAlcatel","DNs","VQueue") },
        [PSCustomObject]@{ Key = "PlaceGroups"; Label = "Place Group 오브젝트";         Kind = "PlaceGroup"; UsesLocation = $false; Condition = "IVR";    Base = @("Configuration","Environment","Place Groups") }
    )
}

function Resolve-TargetFullSegments {
    param($Spec, [string]$CenterName, [string]$Location)
    $locTrim = if ($Location) { $Location.Trim() } else { "" }
    $segs = New-Object System.Collections.Generic.List[string]
    foreach ($b in $Spec.Base) { $segs.Add([string]$b) }
    if ($Spec.UsesLocation -and $locTrim -and ($locTrim -ne "(위치 없음)") -and ($locTrim -ne "-")) {
        $segs.Add($locTrim)
    }
    $segs.Add($CenterName.Trim())
    return @($segs.ToArray())
}

function Find-NameEditControlRecursive {
    param([IntPtr]$DlgHwnd)
    $cs = Get-CmDialogControls $DlgHwnd
    if ($cs.ContainsKey("Edit/1001")) { return $cs["Edit/1001"] }

    $script:__foundEdit1001 = [IntPtr]::Zero
    $script:__firstVisibleEdit = [IntPtr]::Zero
    [SceWin32]::EnumChildWindows($DlgHwnd, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        if (-not [SceWin32]::IsWindowVisible($c)) { return $true }
        if ([SceWin32]::GetClass($c) -eq "Edit") {
            if ($script:__firstVisibleEdit -eq [IntPtr]::Zero) {
                $script:__firstVisibleEdit = $c
            }
            if ([SceWin32]::GetDlgCtrlID($c) -eq 1001) {
                $script:__foundEdit1001 = $c
                return $false
            }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null

    if ($script:__foundEdit1001 -ne [IntPtr]::Zero) { return $script:__foundEdit1001 }
    return $script:__firstVisibleEdit
}

function Test-CmNodeChildExists {
    param($Session, [IntPtr]$ParentNode, [string]$Name, [string]$Kind = "Folder")
    if ($ParentNode -eq [IntPtr]::Zero -or [string]::IsNullOrWhiteSpace($Name)) { return $false }

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $ParentNode) | Out-Null
    Start-Sleep -Milliseconds 140
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null # Refresh
    Start-Sleep -Milliseconds 180

    if ($Kind -eq "Folder") {
        $tn = Find-CmTreeChild $Session $ParentNode $Name
        if ($tn -ne [IntPtr]::Zero) { return $true }
    }
    $li = Find-CmListItemIndex $Session $Name 0
    return ($li -ge 0)
}

function New-CmItemInParent {
    param(
        $Session,
        [IntPtr]$ParentNode,
        [string]$ItemName,
        [ValidateSet("Folder", "PlaceGroup")]
        [string]$Kind = "Folder"
    )
    if ($ParentNode -eq [IntPtr]::Zero) { throw "상위 CM 노드 핸들이 유효하지 않습니다." }

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $ParentNode) | Out-Null
    [SceWin32]::PostMessage($Session.TreeHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 180

    $cmdId = if ($Kind -eq "PlaceGroup") { 8002 } else { 8040 }
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]$cmdId, [IntPtr]::Zero) | Out-Null

    $dlg = Wait-CmDialogOpen 5000
    if ($dlg -eq [IntPtr]::Zero) {
        $label = if ($Kind -eq "PlaceGroup") { "New Place Group (8002)" } else { "New Folder (8040)" }
        throw "$label 대화상자가 열리지 않았습니다."
    }

    Start-Sleep -Milliseconds 100
    $edName = Find-NameEditControlRecursive $dlg
    if ($edName -eq [IntPtr]::Zero) {
        [SceWin32]::PostMessage($dlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "대화상자에서 이름 입력란(Edit)을 찾을 수 없습니다."
    }

    $topCs = Get-CmDialogControls $dlg
    $okBtn = $topCs["Button/1"]
    if (-not $okBtn) {
        [SceWin32]::PostMessage($dlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "대화상자에서 OK 버튼을 찾을 수 없습니다."
    }

    [SceWin32]::SetEditText($edName, $ItemName)
    Start-Sleep -Milliseconds 60
    [SceWin32]::PostMessage($okBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 160

    $done = Wait-CmOperationComplete 15000
    if (-not $done.Ok) {
        throw $done.Error
    }

    # Verify item appeared
    for ($v = 0; $v -lt 6; $v++) {
        if (Test-CmNodeChildExists $Session $ParentNode $ItemName $Kind) {
            return $true
        }
        Start-Sleep -Milliseconds 150
    }
    throw "생성 명령 후 '$ItemName' 확인에 실패했습니다."
}

function Remove-CmItemInParent {
    param(
        $Session,
        [IntPtr]$ParentNode,
        [string]$ItemName,
        [ValidateSet("Folder", "PlaceGroup")]
        [string]$Kind = "Folder"
    )
    if ($ParentNode -eq [IntPtr]::Zero) { throw "상위 CM 노드 핸들이 유효하지 않습니다." }

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $ParentNode) | Out-Null
    Start-Sleep -Milliseconds 160
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 220

    $idx = Find-CmListItemIndex $Session $ItemName 0
    $useTree = $false
    $targetTree = [IntPtr]::Zero
    if ($idx -lt 0 -and $Kind -eq "Folder") {
        $targetTree = Find-CmTreeChild $Session $ParentNode $ItemName
        if ($targetTree -ne [IntPtr]::Zero) { $useTree = $true }
    }
    if ($idx -lt 0 -and -not $useTree) {
        return $false
    }

    if ($useTree) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $targetTree) | Out-Null
        [SceWin32]::PostMessage($Session.TreeHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    } else {
        [SceWin32]::PostMessage($Session.ListHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        [SceWin32]::SelectListItem($Session.HProc, $Session.ListHwnd, $idx)
    }
    Start-Sleep -Milliseconds 120
    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8005, [IntPtr]::Zero) | Out-Null # Delete

    $confDlg = Wait-CmDialogOpen 5000
    if ($confDlg -eq [IntPtr]::Zero) {
        throw "CM 삭제 확인 창이 열리지 않았습니다: $ItemName"
    }
    $cs = Get-CmDialogControls $confDlg
    $yesBtn = $cs["Button/1"]
    if (-not $yesBtn) { $yesBtn = $cs["Button/6"] }
    if (-not $yesBtn) {
        [SceWin32]::PostMessage($confDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "CM 삭제 확인(Yes) 버튼을 찾을 수 없습니다."
    }
    [SceWin32]::PostMessage($yesBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 200

    $done = Wait-CmOperationComplete 20000
    if (-not $done.Ok) {
        throw $done.Error
    }

    for ($v = 0; $v -lt 6; $v++) {
        if (-not (Test-CmNodeChildExists $Session $ParentNode $ItemName $Kind)) {
            return $true
        }
        Start-Sleep -Milliseconds 150
    }
    throw "삭제 후에도 '$ItemName' 항목이 남아 있습니다."
}

function Invoke-CmCheckCenter {
    param($Session, $Payload, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceCenterModals
    $centerName = ([string]$Payload.CenterName).Trim()
    $location = if ($Payload.Location) { ([string]$Payload.Location).Trim() } else { "" }
    $useVQueue = [bool]$Payload.UseVQueue
    $useIvr = [bool]$Payload.UseIvr
    $useLoc = ($location -and $location -ne "(위치 없음)" -and $location -ne "-")

    & $Log "[사전 점검] 센터='$centerName', 위치='$(if($useLoc){$location}else{'없음'})', VQueue=$useVQueue, IVR=$useIvr"
    $specs = Get-CenterCmTargetSpecs
    $items = New-Object System.Collections.Generic.List[object]

    foreach ($sp in $specs) {
        $enabled = switch ($sp.Condition) {
            "VQueue" { $useVQueue }
            "IVR"    { $useIvr }
            default  { $true }
        }
        if (-not $enabled) { continue }

        $full = Resolve-TargetFullSegments $sp $centerName $location
        $dispPath = ($full -join " > ")
        $baseNode = Select-CmTreePath $Session $sp.Base
        if ($baseNode -eq [IntPtr]::Zero) {
            $items.Add([PSCustomObject]@{
                key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                status = "ParentMissing"; detail = "CM 기본 상위 경로 없음: $($sp.Base -join ' > ')"
            })
            & $Log "  [오류] $($sp.Label): 기본 경로 없음 ($($sp.Base -join ' > '))"
            continue
        }

        $parentNode = $baseNode
        $locWillCreate = $false
        if ($sp.UsesLocation -and $useLoc) {
            $locNode = Find-CmTreeChild $Session $baseNode $location
            if ($locNode -eq [IntPtr]::Zero) {
                $locWillCreate = $true
                $parentNode = [IntPtr]::Zero
            } else {
                $parentNode = $locNode
            }
        }

        if ($locWillCreate) {
            $items.Add([PSCustomObject]@{
                key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                status = "WillCreateWithLocation"; detail = "위치 폴더 '$location' 포함 신규 생성 예정"
            })
            & $Log "  [생성 예정] $dispPath (위치 폴더 '$location' 함께 생성)"
        } elseif (Test-CmNodeChildExists $Session $parentNode $centerName $sp.Kind) {
            $items.Add([PSCustomObject]@{
                key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                status = "Exists"; detail = "CM에 이미 존재 (생성 시 건너뜀)"
            })
            & $Log "  [이미 존재] $dispPath"
        } else {
            $items.Add([PSCustomObject]@{
                key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                status = "WillCreate"; detail = "신규 생성 가능"
            })
            & $Log "  [생성 가능] $dispPath"
        }
    }

    $res = [PSCustomObject]@{
        centerName = $centerName
        location   = $location
        useVQueue  = $useVQueue
        useIvr     = $useIvr
        items      = $items.ToArray()
    }
    return $res
}

function Invoke-CmCreateCenter {
    param($Session, $Payload, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Close-AllSceCenterModals

    $centerName = ([string]$Payload.CenterName).Trim()
    $location = if ($Payload.Location) { ([string]$Payload.Location).Trim() } else { "" }
    $useVQueue = [bool]$Payload.UseVQueue
    $useIvr = [bool]$Payload.UseIvr
    $useLoc = ($location -and $location -ne "(위치 없음)" -and $location -ne "-")

    if (-not $centerName) { throw "센터 이름을 입력하세요." }

    & $Log "[센터 CM 생성 시작] 센터='$centerName' | 위치='$(if($useLoc){$location}else{'없음'})' | VQueue=$useVQueue | IVR=$useIvr"
    $specs = Get-CenterCmTargetSpecs
    $results = New-Object System.Collections.Generic.List[object]
    $createdCount = 0
    $skippedCount = 0
    $failedCount = 0

    foreach ($sp in $specs) {
        $enabled = switch ($sp.Condition) {
            "VQueue" { $useVQueue }
            "IVR"    { $useIvr }
            default  { $true }
        }
        if (-not $enabled) { continue }

        $full = Resolve-TargetFullSegments $sp $centerName $location
        $dispPath = ($full -join " > ")
        $objType = if ($sp.Kind -eq "PlaceGroup") { "Place Group" } else { "Folder" }

        try {
            $baseNode = Select-CmTreePath $Session $sp.Base
            if ($baseNode -eq [IntPtr]::Zero) {
                throw "CM 기본 상위 경로를 찾을 수 없습니다: $($sp.Base -join ' > ')"
            }

            $parentNode = $baseNode
            if ($sp.UsesLocation -and $useLoc) {
                $locNode = Find-CmTreeChild $Session $baseNode $location
                if ($locNode -eq [IntPtr]::Zero) {
                    & $Log ("  [위치 폴더 생성] {0} > {1}" -f ($sp.Base -join " > "), $location)
                    [void](New-CmItemInParent $Session $baseNode $location "Folder")
                    $locPath = (($sp.Base + @($location)) -join " > ")
                    Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Folder" -Name $location -Customer $centerName -Path $locPath -Detail "상위 위치 폴더 생성"
                    $locNode = Select-CmTreePath $Session ($sp.Base + @($location))
                    if ($locNode -eq [IntPtr]::Zero) {
                        throw "위치 폴더 '$location' 생성 후 재선택 실패"
                    }
                }
                $parentNode = $locNode
            }

            if (Test-CmNodeChildExists $Session $parentNode $centerName $sp.Kind) {
                $skippedCount++
                $results.Add([PSCustomObject]@{
                    key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                    status = "Skipped"; detail = "이미 존재함"
                })
                & $Log ("  [건너뜀] {0} -> 이미 존재 ({1})" -f $sp.Label, $dispPath)
            } else {
                [void](New-CmItemInParent $Session $parentNode $centerName $sp.Kind)
                $createdCount++
                $results.Add([PSCustomObject]@{
                    key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                    status = "Created"; detail = "생성 완료"
                })
                & $Log ("  [생성 완료] {0} -> {1}" -f $sp.Label, $dispPath)
                Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType $objType -Name $centerName -Customer $centerName -Path $dispPath
            }
        } catch {
            Close-AllSceCenterModals
            $failedCount++
            $errMsg = $_.Exception.Message
            $results.Add([PSCustomObject]@{
                key = $sp.Key; label = $sp.Label; kind = $sp.Kind; path = $dispPath
                status = "Failed"; detail = $errMsg
            })
            & $Log ("  [실패] {0} ({1}) -> {2}" -f $sp.Label, $dispPath, $errMsg)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType $objType -Name $centerName -Customer $centerName -Path $dispPath -Operation "CreateCenter" -Reason $errMsg
        }
    }

    $sw.Stop()
    & $Log ("[센터 CM 생성 완료] 생성 {0}건 / 건너뜀 {1}건 / 실패 {2}건 ({3:N1}초)" -f $createdCount, $skippedCount, $failedCount, $sw.Elapsed.TotalSeconds)

    return [PSCustomObject]@{
        centerName     = $centerName
        location       = $location
        useVQueue      = $useVQueue
        useIvr         = $useIvr
        createdCount   = $createdCount
        skippedCount   = $skippedCount
        failedCount    = $failedCount
        elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        items          = $results.ToArray()
    }
}

function Get-CenterOptionTargetDefs {
    return @(
        [PSCustomObject]@{ Key = "VQueue";      Label = "VQueue 폴더";          Kind = "Folder";     ObjType = "Folder";      Base = @("Configuration","Environment","Switches","TServerAlcatel","DNs","VQueue") },
        [PSCustomObject]@{ Key = "PlaceGroups"; Label = "Place Group 오브젝트"; Kind = "PlaceGroup"; ObjType = "Place Group"; Base = @("Configuration","Environment","Place Groups") }
    )
}

function Invoke-CmCheckCenterOptions {
    param($Session, [string]$CenterName, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceCenterModals
    $cname = $CenterName.Trim()
    if (-not $cname) { throw "센터 이름이 비어 있습니다." }

    & $Log "[CM 옵션 상태 조회] '$cname' (VQueue / Place Group) 확인 중..."
    $defs = Get-CenterOptionTargetDefs
    $vqueueExists = $false
    $ivrExists = $false
    $vqPath = ""
    $ivrPath = ""

    foreach ($d in $defs) {
        $baseNode = Select-CmTreePath $Session $d.Base
        $fullPath = (($d.Base + @($cname)) -join " > ")
        $exists = $false
        if ($baseNode -ne [IntPtr]::Zero) {
            $exists = Test-CmNodeChildExists $Session $baseNode $cname $d.Kind
        }
        if ($d.Key -eq "VQueue") {
            $vqueueExists = $exists
            $vqPath = $fullPath
        } else {
            $ivrExists = $exists
            $ivrPath = $fullPath
        }
        & $Log ("  -> {0}: {1} ({2})" -f $d.Label, $(if ($exists) { "CM 존재" } else { "CM 미생성" }), $fullPath)
    }

    return [PSCustomObject]@{
        centerName   = $cname
        vqueueExists = $vqueueExists
        ivrExists    = $ivrExists
        vqueuePath   = $vqPath
        ivrPath      = $ivrPath
    }
}

function Invoke-CmApplyCenterOptions {
    param($Session, $Payload, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceCenterModals
    $cname = ([string]$Payload.CenterName).Trim()
    $applyVq = [bool]$Payload.ApplyVQueue
    $wantVq = [bool]$Payload.WantVQueue
    $applyIvr = [bool]$Payload.ApplyIvr
    $wantIvr = [bool]$Payload.WantIvr
    if (-not $cname) { throw "센터 이름이 비어 있습니다." }

    $defs = Get-CenterOptionTargetDefs
    $results = New-Object System.Collections.Generic.List[object]
    $failedCount = 0

    foreach ($d in $defs) {
        $doApply = if ($d.Key -eq "VQueue") { $applyVq } else { $applyIvr }
        $want = if ($d.Key -eq "VQueue") { $wantVq } else { $wantIvr }
        $fullPath = (($d.Base + @($cname)) -join " > ")
        $baseNode = Select-CmTreePath $Session $d.Base
        $exists = if ($baseNode -ne [IntPtr]::Zero) { Test-CmNodeChildExists $Session $baseNode $cname $d.Kind } else { $false }

        if (-not $doApply) {
            $results.Add([PSCustomObject]@{
                key = $d.Key; label = $d.Label; action = "Unchanged"; existsNow = $exists; path = $fullPath; detail = "변경 대상 제외 (기존 상태 유지)"
            })
            continue
        }

        if ($baseNode -eq [IntPtr]::Zero) {
            $failedCount++
            $err = "CM 기본 상위 경로 없음: $($d.Base -join ' > ')"
            $results.Add([PSCustomObject]@{
                key = $d.Key; label = $d.Label; action = "Failed"; existsNow = $exists; path = $fullPath; detail = $err
            })
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType $d.ObjType -Name $cname -Customer $cname -Path $fullPath -Operation "ApplyCenterOption" -Reason $err
            continue
        }

        try {
            if ($want) {
                if ($exists) {
                    $results.Add([PSCustomObject]@{
                        key = $d.Key; label = $d.Label; action = "Skipped"; existsNow = $true; path = $fullPath; detail = "이미 존재함"
                    })
                    & $Log ("  [유지] {0} 이미 존재 ({1})" -f $d.Label, $fullPath)
                } else {
                    [void](New-CmItemInParent $Session $baseNode $cname $d.Kind)
                    $results.Add([PSCustomObject]@{
                        key = $d.Key; label = $d.Label; action = "Created"; existsNow = $true; path = $fullPath; detail = "생성 완료"
                    })
                    & $Log ("  [생성 완료] {0} -> {1}" -f $d.Label, $fullPath)
                    Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType $d.ObjType -Name $cname -Customer $cname -Path $fullPath
                }
            } else {
                if (-not $exists) {
                    $results.Add([PSCustomObject]@{
                        key = $d.Key; label = $d.Label; action = "Skipped"; existsNow = $false; path = $fullPath; detail = "이미 미존재"
                    })
                    & $Log ("  [유지] {0} 이미 미존재 ({1})" -f $d.Label, $fullPath)
                } else {
                    [void](Remove-CmItemInParent $Session $baseNode $cname $d.Kind)
                    $results.Add([PSCustomObject]@{
                        key = $d.Key; label = $d.Label; action = "Deleted"; existsNow = $false; path = $fullPath; detail = "삭제 완료"
                    })
                    & $Log ("  [삭제 완료] {0} -> {1}" -f $d.Label, $fullPath)
                    Write-CmAuditEvent -Action "DELETE" -Target "CM" -ObjectType $d.ObjType -Name $cname -Customer $cname -Path $fullPath
                }
            }
        } catch {
            Close-AllSceCenterModals
            $failedCount++
            $errMsg = $_.Exception.Message
            $nowEx = Test-CmNodeChildExists $Session $baseNode $cname $d.Kind
            $results.Add([PSCustomObject]@{
                key = $d.Key; label = $d.Label; action = "Failed"; existsNow = $nowEx; path = $fullPath; detail = $errMsg
            })
            & $Log ("  [실패] {0} -> {1}" -f $d.Label, $errMsg)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType $d.ObjType -Name $cname -Customer $cname -Path $fullPath -Operation "ApplyCenterOption" -Reason $errMsg
        }
    }

    # Read final ground truth
    $finalVq = $false
    $finalIvr = $false
    foreach ($d in $defs) {
        $bn = Select-CmTreePath $Session $d.Base
        $ex = if ($bn -ne [IntPtr]::Zero) { Test-CmNodeChildExists $Session $bn $cname $d.Kind } else { $false }
        if ($d.Key -eq "VQueue") { $finalVq = $ex } else { $finalIvr = $ex }
    }

    return [PSCustomObject]@{
        centerName  = $cname
        vqueueExists = $finalVq
        ivrExists   = $finalIvr
        failedCount = $failedCount
        items       = $results.ToArray()
    }
}

function Invoke-CmDeleteCenterArtifacts {
    param($Session, $Payload, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceCenterModals
    $cname = ([string]$Payload.CenterName).Trim()
    $location = if ($Payload.Location) { ([string]$Payload.Location).Trim() } else { "" }
    $useLoc = ($location -and $location -ne "(위치 없음)" -and $location -ne "-")
    $deleteCreatedLoc = [bool]$Payload.DeleteLocationIfSpecified
    if (-not $cname) { throw "센터 이름이 비어 있습니다." }

    & $Log "[센터 CM 항목 삭제] '$cname' 정리 시작..."
    $specs = Get-CenterCmTargetSpecs
    $deleted = 0
    $failed = 0

    foreach ($sp in $specs) {
        $objType = if ($sp.Kind -eq "PlaceGroup") { "Place Group" } else { "Folder" }
        try {
            $baseNode = Select-CmTreePath $Session $sp.Base
            if ($baseNode -eq [IntPtr]::Zero) { continue }
            $parentNode = $baseNode
            if ($sp.UsesLocation -and $useLoc) {
                $parentNode = Find-CmTreeChild $Session $baseNode $location
                if ($parentNode -eq [IntPtr]::Zero) { continue }
            }
            if (Test-CmNodeChildExists $Session $parentNode $cname $sp.Kind) {
                $full = ((Resolve-TargetFullSegments $sp $cname $location) -join " > ")
                if (Remove-CmItemInParent $Session $parentNode $cname $sp.Kind) {
                    $deleted++
                    & $Log "  [삭제 완료] $($sp.Label) ($full)"
                    Write-CmAuditEvent -Action "DELETE" -Target "CM" -ObjectType $objType -Name $cname -Customer $cname -Path $full
                }
            }
            if ($deleteCreatedLoc -and $sp.UsesLocation -and $useLoc) {
                if (Test-CmNodeChildExists $Session $baseNode $location "Folder") {
                    # Only remove empty test location folder when explicitly requested
                    $locFull = (($sp.Base + @($location)) -join " > ")
                    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $parentNode) | Out-Null
                    Start-Sleep -Milliseconds 140
                    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
                    Start-Sleep -Milliseconds 180
                    $childCnt = [int][SceWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                    if ($childCnt -eq 0) {
                        if (Remove-CmItemInParent $Session $baseNode $location "Folder") {
                            $deleted++
                            & $Log "  [빈 위치 폴더 삭제] $locFull"
                            Write-CmAuditEvent -Action "DELETE" -Target "CM" -ObjectType "Folder" -Name $location -Customer $cname -Path $locFull
                        }
                    }
                }
            }
        } catch {
            Close-AllSceCenterModals
            $failed++
            & $Log "  [삭제 실패] $($sp.Label) -> $($_.Exception.Message)"
        }
    }

    return [PSCustomObject]@{ centerName = $cname; deletedCount = $deleted; failedCount = $failed }
}

if ($Action -ne "Library") {
    $passPlain = if ($env:CM_PASS_TEMP) { $env:CM_PASS_TEMP } else { "password" }
    $env:CM_PASS_TEMP = $null

    $payloadObj = $null
    $payloadPath = $env:CM_CENTER_PAYLOAD_PATH
    if ($payloadPath -and (Test-Path $payloadPath)) {
        $payloadRaw = [System.IO.File]::ReadAllText($payloadPath, [System.Text.Encoding]::UTF8)
        if ($payloadRaw) {
            $payloadObj = $payloadRaw | ConvertFrom-Json
        }
    }

    $session = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $session = Connect-CmSession -ExePath $CmExePath -UserName $CmUser -Password $passPlain

        if ($Action -eq "CheckCenter") {
            if (-not $payloadObj) { throw "CheckCenter 페이로드가 비어 있습니다." }
            $res = Invoke-CmCheckCenter $session $payloadObj
            [Console]::WriteLine("##CENTER_CHECK_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
        } elseif ($Action -eq "CreateCenter") {
            if (-not $payloadObj) { throw "CreateCenter 페이로드가 비어 있습니다." }
            $res = Invoke-CmCreateCenter $session $payloadObj
            [Console]::WriteLine("##CENTER_CREATE_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            if ($res.failedCount -gt 0 -and $res.createdCount -eq 0 -and $res.skippedCount -eq 0) {
                throw "모든 CM 항목 생성에 실패했습니다."
            }
        } elseif ($Action -eq "CheckCenterOptions") {
            $cname = if ($payloadObj -and $payloadObj.CenterName) { [string]$payloadObj.CenterName } else { $Customer }
            $res = Invoke-CmCheckCenterOptions $session $cname
            [Console]::WriteLine("##CENTER_STATUS_JSON##" + ($res | ConvertTo-Json -Compress -Depth 5))
        } elseif ($Action -eq "ApplyCenterOptions") {
            if (-not $payloadObj) { throw "ApplyCenterOptions 페이로드가 비어 있습니다." }
            $res = Invoke-CmApplyCenterOptions $session $payloadObj
            [Console]::WriteLine("##CENTER_APPLY_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
        } elseif ($Action -eq "DeleteCenterArtifacts") {
            if (-not $payloadObj) { throw "DeleteCenterArtifacts 페이로드가 비어 있습니다." }
            $res = Invoke-CmDeleteCenterArtifacts $session $payloadObj
            [Console]::WriteLine("##CENTER_DELETE_JSON##" + ($res | ConvertTo-Json -Compress -Depth 5))
        }
    } catch {
        Write-CmAuditEvent -Action "ERROR" -Target "CM" -Operation ("Center:" + $Action) -Customer $Customer -Reason $_.Exception.Message
        throw
    } finally {
        if ($session) { Disconnect-CmSession $session }
    }
}
