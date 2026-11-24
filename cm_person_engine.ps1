param(
    [ValidateSet("Library", "FetchAvailableLogins", "CheckPersons", "CreatePersons", "FetchPersonsInFolder", "UpdatePersons", "DeletePersons")]
    [string]$Action = "Library",
    [string]$Customer = "",
    [string]$CustomersJsonPath = "",
    [string]$CmExePath = "C:\GCTI\Configuration Manager\sce.exe",
    [string]$CmUser = "",
    [bool]$DoGeneral = $true,
    [bool]$DoAgentLogin = $true
)

$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Preserve own parameters before dot-sourcing cm_batch_engine.ps1 (same scope would overwrite $Action/$Customer/etc.)
$_savedAction = $Action
$_savedCustomer = $Customer
$_savedCustomersJsonPath = $CustomersJsonPath
$_savedCmExePath = $CmExePath
$_savedCmUser = $CmUser
$_savedDoGeneral = $DoGeneral
$_savedDoAgentLogin = $DoAgentLogin

. (Join-Path $ScriptDir "cm_batch_engine.ps1") -Action Library

$Action = $_savedAction
$Customer = $_savedCustomer
$CustomersJsonPath = $_savedCustomersJsonPath
$CmExePath = $_savedCmExePath
$CmUser = $_savedCmUser
$DoGeneral = $_savedDoGeneral
$DoAgentLogin = $_savedDoAgentLogin

if (-not $CustomersJsonPath) {
    $CustomersJsonPath = Join-Path $ScriptDir "cm_customers.json"
}

if (-not ("ScePersonWin32" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class ScePersonWin32 {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ReleaseClipCursor(IntPtr lpRect);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
    [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    public static string GetTextWM(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(512);
        SendMessage(hWnd, 0x000D, (IntPtr)512, sb);
        return sb.ToString();
    }

    public static void SetComboEditText(IntPtr hDlg, IntPtr hCombo, IntPtr hEdit, string val) {
        SendMessage(hCombo, 0x014E, (IntPtr)(-1), IntPtr.Zero); // CB_SETCURSEL = -1 (clear active list selection)
        SendMessage(hEdit, 0x000C, IntPtr.Zero, val ?? "");
        int edCid = GetDlgCtrlID(hEdit);
        int cbCid = GetDlgCtrlID(hCombo);
        SendMessage(hCombo, 0x0111, (IntPtr)((0x0300 << 16) | (edCid & 0xFFFF)), hEdit); // EN_CHANGE
        SendMessage(hDlg, 0x0111, (IntPtr)((5 << 16) | (cbCid & 0xFFFF)), hCombo);       // CBN_EDITCHANGE
        SendMessage(hCombo, 0x014E, (IntPtr)(-1), IntPtr.Zero);
        SendMessage(hEdit, 0x000C, IntPtr.Zero, val ?? "");
    }

    public static int[] GetListItemRect(IntPtr hProc, IntPtr hList, int index, int code) {
        IntPtr mem = VirtualAllocEx(hProc, IntPtr.Zero, 64, 0x3000, 0x04);
        if (mem == IntPtr.Zero) return null;
        try {
            byte[] b = new byte[16];
            BitConverter.GetBytes(code).CopyTo(b, 0);
            IntPtr bw;
            WriteProcessMemory(hProc, mem, b, 16, out bw);
            IntPtr ok = SendMessage(hList, 0x100E, (IntPtr)index, mem); // LVM_GETITEMRECT
            if (ok == IntPtr.Zero) return null;
            ReadProcessMemory(hProc, mem, b, 16, out bw);
            int[] rc = new int[] {
                BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 4),
                BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12)
            };
            if (rc[2] <= rc[0] || rc[3] <= rc[1]) return null;
            return rc;
        } finally {
            VirtualFreeEx(hProc, mem, 0, 0x8000);
        }
    }

    public static bool DoubleClickListItem(IntPtr hProc, IntPtr hList, int index) {
        SendMessage(hList, 0x1013, (IntPtr)index, IntPtr.Zero); // LVM_ENSUREVISIBLE
        int[] rc = GetListItemRect(hProc, hList, index, 2); // LVIR_LABEL
        if (rc == null) rc = GetListItemRect(hProc, hList, index, 0); // LVIR_BOUNDS
        if (rc == null) return false;
        int cx = (rc[0] + rc[2]) / 2;
        int cy = (rc[1] + rc[3]) / 2;
        IntPtr lp = (IntPtr)(((cy & 0xFFFF) << 16) | (cx & 0xFFFF));
        PostMessage(hList, 0x0201, (IntPtr)1, lp); // WM_LBUTTONDOWN
        PostMessage(hList, 0x0202, IntPtr.Zero, lp); // WM_LBUTTONUP
        PostMessage(hList, 0x0203, (IntPtr)1, lp); // WM_LBUTTONDBLCLK
        PostMessage(hList, 0x0202, IntPtr.Zero, lp); // WM_LBUTTONUP
        return true;
    }

    public static void SendNmDblClk(IntPtr hProc, IntPtr hDlg, IntPtr hList, int index, int px, int py) {
        IntPtr mem = VirtualAllocEx(hProc, IntPtr.Zero, 64, 0x3000, 0x04);
        if (mem == IntPtr.Zero) return;
        try {
            int listId = GetDlgCtrlID(hList);
            byte[] buf = new byte[48];
            BitConverter.GetBytes((uint)hList.ToInt64()).CopyTo(buf, 0);
            BitConverter.GetBytes((uint)listId).CopyTo(buf, 4);
            BitConverter.GetBytes(-3).CopyTo(buf, 8); // NM_DBLCLK
            BitConverter.GetBytes(index).CopyTo(buf, 12);
            BitConverter.GetBytes(0).CopyTo(buf, 16);
            BitConverter.GetBytes((uint)3).CopyTo(buf, 20);
            BitConverter.GetBytes(px).CopyTo(buf, 32);
            BitConverter.GetBytes(py).CopyTo(buf, 36);
            IntPtr bw;
            WriteProcessMemory(hProc, mem, buf, 48, out bw);
            SendMessage(hDlg, 0x004E, (IntPtr)listId, mem); // WM_NOTIFY synchronous
        } finally {
            VirtualFreeEx(hProc, mem, 0, 0x8000);
        }
    }

    public static string DoubleClickBrowseItem(IntPtr hProc, IntPtr hDlg, IntPtr hList, int index, IntPtr hStatic, string prevStatic, int timeoutMs) {
        if (IsIconic(hDlg)) ShowWindow(hDlg, 9);
        SendMessage(hList, 0x1013, (IntPtr)index, IntPtr.Zero); // LVM_ENSUREVISIBLE
        System.Threading.Thread.Sleep(25);
        int[] rc = null;
        RECT crc;
        GetClientRect(hList, out crc);
        for (int r = 0; r < 6; r++) {
            rc = GetListItemRect(hProc, hList, index, 2);
            if (rc == null) rc = GetListItemRect(hProc, hList, index, 0);
            if (rc != null && rc[3] > rc[1] && rc[2] > rc[0] && rc[1] >= 0 && (crc.Bottom <= 0 || rc[3] <= crc.Bottom)) break;
            SendMessage(hList, 0x1013, (IntPtr)index, IntPtr.Zero);
            System.Threading.Thread.Sleep(30);
        }
        if (rc == null) return "ITEM_RECT_FAIL";

        POINT pt = new POINT();
        pt.X = (rc[0] + rc[2]) / 2;
        pt.Y = (rc[1] + rc[3]) / 2;
        if (pt.X < rc[0] + 1) pt.X = rc[0] + 1;
        if (pt.X > rc[2] - 1) pt.X = rc[2] - 1;
        if (pt.Y < rc[1] + 1) pt.Y = rc[1] + 1;
        if (pt.Y > rc[3] - 1) pt.Y = rc[3] - 1;
        if (crc.Right > 6 && crc.Bottom > 6) {
            if (pt.X < 2) pt.X = 2;
            if (pt.X > crc.Right - 3) pt.X = crc.Right - 3;
            if (pt.Y < 2) pt.Y = 2;
            if (pt.Y > crc.Bottom - 3) pt.Y = crc.Bottom - 3;
        }
        POINT scr = pt;
        if (!ClientToScreen(hList, ref scr)) return "CLIENT_TO_SCREEN_FAIL";

        uint pid;
        uint targetTid = GetWindowThreadProcessId(hList, out pid);
        uint curTid = GetCurrentThreadId();
        bool attached = AttachThreadInput(curTid, targetTid, true);
        try {
            SetForegroundWindow(hDlg);
            SetFocus(hList);
        } finally {
            if (attached) AttachThreadInput(curTid, targetTid, false);
        }

        POINT oldPt;
        bool hadCursor = GetCursorPos(out oldPt);
        if (!SetCursorPos(scr.X, scr.Y)) return "LOCKED_SCREEN";
        POINT nowPt;
        if (GetCursorPos(out nowPt)) {
            if (Math.Abs(nowPt.X - scr.X) > 6 || Math.Abs(nowPt.Y - scr.Y) > 6) {
                if (hadCursor) SetCursorPos(oldPt.X, oldPt.Y);
                return "LOCKED_SCREEN";
            }
        }

        RECT clipRc = new RECT();
        clipRc.Left = scr.X;
        clipRc.Top = scr.Y;
        clipRc.Right = scr.X + 1;
        clipRc.Bottom = scr.Y + 1;
        ClipCursor(ref clipRc);
        try {
            SetCursorPos(scr.X, scr.Y);
            System.Threading.Thread.Sleep(15);
            SendNmDblClk(hProc, hDlg, hList, index, pt.X, pt.Y);
            if (GetTextWM(hStatic) != prevStatic) return "OK";

            IntPtr lp = (IntPtr)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
            int elapsed = 0;
            bool sentPostFirst = false;
            bool sentNmRetry = false;
            bool sentPostRetry = false;
            while (elapsed < timeoutMs) {
                System.Threading.Thread.Sleep(25);
                elapsed += 25;
                if (GetTextWM(hStatic) != prevStatic) return "OK";

                if (!sentPostFirst && elapsed >= 75) {
                    sentPostFirst = true;
                    SetCursorPos(scr.X, scr.Y);
                    PostMessage(hList, 0x0201, (IntPtr)1, lp);
                    PostMessage(hList, 0x0202, IntPtr.Zero, lp);
                    PostMessage(hList, 0x0203, (IntPtr)1, lp);
                    PostMessage(hList, 0x0202, IntPtr.Zero, lp);
                }
                if (!sentNmRetry && elapsed >= 350) {
                    sentNmRetry = true;
                    SetCursorPos(scr.X, scr.Y);
                    SendNmDblClk(hProc, hDlg, hList, index, pt.X, pt.Y);
                    if (GetTextWM(hStatic) != prevStatic) return "OK";
                }
                if (!sentPostRetry && elapsed >= 700) {
                    sentPostRetry = true;
                    SetCursorPos(scr.X, scr.Y);
                    PostMessage(hList, 0x0201, (IntPtr)1, lp);
                    PostMessage(hList, 0x0202, IntPtr.Zero, lp);
                    PostMessage(hList, 0x0203, (IntPtr)1, lp);
                    PostMessage(hList, 0x0202, IntPtr.Zero, lp);
                }
            }
            return (GetTextWM(hStatic) != prevStatic) ? "OK" : "TIMEOUT";
        } finally {
            ReleaseClipCursor(IntPtr.Zero);
            if (hadCursor) SetCursorPos(oldPt.X, oldPt.Y);
        }
    }
}
"@
}

function Close-AllSceModals {
    for ($pass = 0; $pass -lt 4; $pass++) {
        $dlgs = @(Get-SceDialogs)
        if ($dlgs.Count -eq 0) { break }
        foreach ($d in $dlgs) {
            [ScePersonWin32]::PostMessage($d, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }
        Start-Sleep -Milliseconds 70
    }
}

function Try-FindTreeNodePath {
    param($Session, [string[]]$Segments)
    try {
        return Select-CmTreePath $Session $Segments
    } catch {
        return [IntPtr]::Zero
    }
}

function Resolve-PersonsFolderNode {
    param($Session, $CustomerObj)
    $overrides = @{
        "하이프라자_온라인몰" = @("24층", "하이프라자온라인몰")
        "LG하우시스_신유통"   = @("25층", "LG하우시스_신유통")
        "버킷스토어"          = @("LB휴넷18층", "버킷스토어")
        "한국교육과정평가원"  = @("26층", "한국교육과정평가원")
        "LG하우시스_알루미늄" = @("충정로", "LX하우시스_알루미늄")
        "LG통합ID"            = @("마곡LG사이언스파크", "LGID")
    }
    $cName = [string]$CustomerObj.name
    $base = @("Configuration", "Environment", "Persons")

    $cands = New-Object System.Collections.Generic.List[object]
    if ($cName -match "__TEST__") { $cands.Add(@("TEST")) }
    if ($overrides.ContainsKey($cName)) { $cands.Add($overrides[$cName]) }
    if ($CustomerObj.place) { $cands.Add(@($CustomerObj.place | ForEach-Object { [string]$_ })) }
    if ($CustomerObj.agentLogin) { $cands.Add(@($CustomerObj.agentLogin | ForEach-Object { [string]$_ })) }
    if ($CustomerObj.floor -and $cName) { $cands.Add(@([string]$CustomerObj.floor, $cName)) }

    foreach ($c in $cands) {
        $full = $base + @($c)
        $h = Try-FindTreeNodePath $Session $full
        if ($h -ne [IntPtr]::Zero) {
            return [PSCustomObject]@{ Node = $h; DisplayPath = ($full -join " > ") }
        }
    }

    # Fuzzy scan under Persons root (2 levels)
    $pRoot = Try-FindTreeNodePath $Session $base
    if ($pRoot -ne [IntPtr]::Zero) {
        $normTarget = ($cName -replace "[_\s\(\)테스트전용폴더]", "").ToLowerInvariant()
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x1102, [IntPtr]2, $pRoot) | Out-Null
        Start-Sleep -Milliseconds 50
        $lvl1 = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]4, $pRoot)
        while ($lvl1 -ne [IntPtr]::Zero) {
            $t1 = [SceWin32]::GetTreeItemText32($Session.HProc, $Session.TreeHwnd, $lvl1)
            $n1 = ($t1 -replace "[_\s]", "").ToLowerInvariant()
            if ($n1 -eq $normTarget) {
                [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $lvl1) | Out-Null
                return [PSCustomObject]@{ Node = $lvl1; DisplayPath = (($base + @($t1)) -join " > ") }
            }
            [SceWin32]::SendMessage($Session.TreeHwnd, 0x1102, [IntPtr]2, $lvl1) | Out-Null
            Start-Sleep -Milliseconds 35
            $lvl2 = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]4, $lvl1)
            while ($lvl2 -ne [IntPtr]::Zero) {
                $t2 = [SceWin32]::GetTreeItemText32($Session.HProc, $Session.TreeHwnd, $lvl2)
                $n2 = ($t2 -replace "[_\s]", "").ToLowerInvariant()
                if ($n2 -eq $normTarget) {
                    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $lvl2) | Out-Null
                    return [PSCustomObject]@{ Node = $lvl2; DisplayPath = (($base + @($t1, $t2)) -join " > ") }
                }
                $lvl2 = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]1, $lvl2)
            }
            $lvl1 = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]1, $lvl1)
        }
    }
    return $null
}

function Get-ChildEditOfCombo {
    param([IntPtr]$Combo)
    $script:__childEditHwnd = [IntPtr]::Zero
    [SceWin32]::EnumChildWindows($Combo, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        if ([SceWin32]::GetClass($c) -eq "Edit") { $script:__childEditHwnd = $c; return $false }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:__childEditHwnd
}

function Find-ChildCtrl {
    param([IntPtr]$Parent, [string]$ClassName, [int]$CtrlId, [bool]$VisibleOnly = $false)
    $script:__foundChild = [IntPtr]::Zero
    [SceWin32]::EnumChildWindows($Parent, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        if ($VisibleOnly -and -not [SceWin32]::IsWindowVisible($c)) { return $true }
        if ($CtrlId -ne -999 -and [ScePersonWin32]::GetDlgCtrlID($c) -ne $CtrlId) { return $true }
        if ($ClassName -and [SceWin32]::GetClass($c) -ne $ClassName) { return $true }
        $script:__foundChild = $c
        return $false
    }, [IntPtr]::Zero) | Out-Null
    return $script:__foundChild
}

function Open-PersonPropertiesDialog {
    param($Session, [int]$RowIndex, [string]$ExpectedUserName, [int]$TimeoutMs = 3000)
    $curIdx = $RowIndex
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        Close-AllSceModals
        if ($attempt -gt 0 -and $ExpectedUserName) {
            $reIdx = Find-CmListItemIndex $Session $ExpectedUserName 0
            if ($reIdx -ge 0) { $curIdx = $reIdx }
        }
        [SceWin32]::SelectListItem($Session.HProc, $Session.ListHwnd, $curIdx)
        [ScePersonWin32]::PostMessage($Session.ListHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 40
        [void][ScePersonWin32]::DoubleClickListItem($Session.HProc, $Session.ListHwnd, $curIdx)
        $pd = Wait-CmDialogOpen $TimeoutMs
        if ($pd -eq [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 120
            continue
        }
        if ($ExpectedUserName) {
            $genPage = Find-ChildCtrl $pd "#32770" 133 $false
            if ($genPage -ne [IntPtr]::Zero) {
                $cbUser = Find-ChildCtrl $genPage "ComboBox" 1039 $false
                if ($cbUser -ne [IntPtr]::Zero) {
                    $actualUser = ([SceWin32]::GetTextWM($cbUser)).Trim()
                    if ($actualUser -and -not [string]::Equals($actualUser, $ExpectedUserName, [System.StringComparison]::OrdinalIgnoreCase)) {
                        [ScePersonWin32]::PostMessage($pd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                        Start-Sleep -Milliseconds 120
                        continue
                    }
                }
            }
        }
        return $pd
    }
    return [IntPtr]::Zero
}

function Read-PersonAgentLogins {
    param($Session, [int]$RowIndex, [string]$UserName)
    $logins = New-Object System.Collections.Generic.List[string]
    $pd = Open-PersonPropertiesDialog $Session $RowIndex $UserName 2800
    if ($pd -eq [IntPtr]::Zero) { return @() }
    try {
        $tab = Find-ChildCtrl $pd "SysTabControl32" -999 $false
        if ($tab -ne [IntPtr]::Zero) {
            for ($t = 0; $t -lt 3; $t++) {
                [ScePersonWin32]::SendMessage($tab, 0x1330, [IntPtr]1, [IntPtr]::Zero) | Out-Null # TCM_SETCURFOCUS
                Start-Sleep -Milliseconds 35
                $curSel = [int][ScePersonWin32]::SendMessage($tab, 0x130B, [IntPtr]::Zero, [IntPtr]::Zero) # TCM_GETCURSEL
                if ($curSel -eq 1) { break }
            }
            $lv24 = [IntPtr]::Zero
            for ($w = 0; $w -lt 25; $w++) {
                $lv24 = Find-ChildCtrl $pd "SysListView32" 1024 $true
                if ($lv24 -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 30
            }
            if ($lv24 -ne [IntPtr]::Zero) {
                $prevCnt = -1
                $cnt = 0
                for ($sw = 0; $sw -lt 6; $sw++) {
                    Start-Sleep -Milliseconds $(if ($sw -eq 0) { 60 } else { 45 })
                    $cnt = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                    if ($cnt -eq $prevCnt -and ($cnt -gt 0 -or $sw -ge 2)) { break }
                    $prevCnt = $cnt
                }
                for ($i = 0; $i -lt $cnt; $i++) {
                    $lid = ([SceWin32]::GetListItemText32($Session.HProc, $lv24, $i, 0)).Trim()
                    if ($lid) { $logins.Add($lid) }
                }
            }
        }
    } finally {
        [ScePersonWin32]::PostMessage($pd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        for ($w = 0; $w -lt 30; $w++) {
            if ((Get-SceDialogs).Count -eq 0) { break }
            Start-Sleep -Milliseconds 25
        }
        Start-Sleep -Milliseconds 40
    }
    return @($logins.ToArray())
}

function Invoke-CmFetchAvailableLogins {
    param($Session, $CustomerObj, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceModals
    $paths = Resolve-CustomerTreePaths $CustomerObj
    if (-not $paths.AgentLogin) {
        throw "고객사 '$($CustomerObj.name)'의 Agent Login 폴더 경로가 설정되어 있지 않습니다."
    }

    & $Log "[1/2] CM Agent Logins 목록 조회 중... ($($paths.AgentLogin -join ' > '))"
    $alNode = Select-CmTreePath $Session $paths.AgentLogin
    if ($alNode -eq [IntPtr]::Zero) {
        throw "Agent Logins CM 폴더를 찾을 수 없습니다: $($paths.AgentLogin -join ' > ')"
    }
    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null # Refresh
    Start-Sleep -Milliseconds 260
    $alRows = [int][ScePersonWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
    $rawLogins = New-Object System.Collections.Generic.List[object]
    $seenIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    for ($i = 0; $i -lt $alRows; $i++) {
        $lid = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 0)).Trim()
        $sw  = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 1)).Trim()
        if ($lid -and $seenIds.Add($lid)) {
            $rawLogins.Add([PSCustomObject]@{ LoginId = $lid; SwitchName = $sw })
        }
    }
    & $Log ("  -> Agent Login {0}건 로드 완료" -f $rawLogins.Count)

    $usedMap = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $personFolderPath = ""
    $pResolved = Resolve-PersonsFolderNode $Session $CustomerObj
    if ($null -eq $pResolved) {
        & $Log "[경고] CM Persons 폴더에서 '$($CustomerObj.name)' 폴더를 찾지 못했습니다. 사용 중 여부는 미확인으로 표시됩니다."
    } else {
        $personFolderPath = $pResolved.DisplayPath
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
        Start-Sleep -Milliseconds 120
        [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null # Refresh
        Start-Sleep -Milliseconds 260
        $pCount = [int][ScePersonWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
        $agentRows = New-Object System.Collections.Generic.List[object]
        for ($pi = 0; $pi -lt $pCount; $pi++) {
            $u  = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $pi, 0)).Trim()
            $ag = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $pi, 4)).Trim()
            if ($u -and ([string]::Equals($ag, "True", [System.StringComparison]::OrdinalIgnoreCase) -or $ag -eq "1" -or [string]::Equals($ag, "Yes", [System.StringComparison]::OrdinalIgnoreCase))) {
                $agentRows.Add([PSCustomObject]@{ Index = $pi; UserName = $u })
            }
        }
        & $Log ("[2/2] Persons 폴더 스캔 중... ({0} / 대상 Agent {1}건)" -f $personFolderPath, $agentRows.Count)
        $done = 0
        foreach ($ar in $agentRows) {
            $done++
            $assigned = Read-PersonAgentLogins $Session $ar.Index $ar.UserName
            foreach ($aid in $assigned) {
                if (-not $usedMap.ContainsKey($aid)) {
                    $usedMap[$aid] = $ar.UserName
                } else {
                    $usedMap[$aid] = "$($usedMap[$aid]), $($ar.UserName)"
                }
            }
            if (($done % 5 -eq 0) -or ($done -eq $agentRows.Count)) {
                & $Log ("  -> 진행: {0}/{1}명 확인 ({2})" -f $done, $agentRows.Count, $ar.UserName)
            }
        }
    }

    $items = New-Object System.Collections.Generic.List[object]
    $availCnt = 0
    foreach ($entry in $rawLogins) {
        $isUsed = $usedMap.ContainsKey($entry.LoginId)
        $owner = if ($isUsed) { [string]$usedMap[$entry.LoginId] } else { "" }
        if (-not $isUsed) { $availCnt++ }
        $items.Add([PSCustomObject]@{
            loginId     = $entry.LoginId
            switchName  = $entry.SwitchName
            isAvailable = (-not $isUsed)
            usedBy      = $owner
        })
    }

    & $Log ("[조회 완료] 전체 {0}건 중 사용 가능 {1}건 / 사용 중 {2}건" -f $items.Count, $availCnt, ($items.Count - $availCnt))
    return [PSCustomObject]@{
        customer         = [string]$CustomerObj.name
        personFolderPath = $personFolderPath
        totalCount       = $items.Count
        availableCount   = $availCnt
        items            = $items.ToArray()
    }
}

function Get-GlobalPersonIndex {
    param($Session, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    Close-AllSceModals

    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32831, [IntPtr]::Zero) | Out-Null
    $findDlg = Wait-CmDialogOpen 5000
    if ($findDlg -eq [IntPtr]::Zero) { throw "CM Find 대화상자를 열 수 없습니다." }

    $byEmp = @{}
    $byUser = @{}
    $rowCount = 0
    try {
        $ctrls = Get-CmDialogControls $findDlg
        $cbType = $ctrls["ComboBox/7103"]
        $edName = $ctrls["Edit/1001"]
        $btnFind = $ctrls["Button/7001"]
        $lvRes = $ctrls["SysListView32/2"]

        for ($w = 0; $w -lt 100; $w++) {
            if ([SceWin32]::IsWindowEnabled($btnFind)) { break }
            Start-Sleep -Milliseconds 40
        }
        [SceWin32]::SelectComboIndex($cbType, 26) # Person
        [SceWin32]::SendMessage($edName, 0x000C, [IntPtr]::Zero, "") | Out-Null
        [SceWin32]::PostMessage($btnFind, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

        for ($s = 0; $s -lt 8; $s++) {
            Start-Sleep -Milliseconds 35
            if (-not [SceWin32]::IsWindowEnabled($btnFind)) { break }
        }
        for ($w = 0; $w -lt 250; $w++) {
            if ([SceWin32]::IsWindowEnabled($btnFind)) { break }
            Start-Sleep -Milliseconds 40
        }
        Start-Sleep -Milliseconds 30

        $rowCount = [int][SceWin32]::SendMessage($lvRes, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
        for ($i = 0; $i -lt $rowCount; $i++) {
            $u = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, 0)).Trim()
            $f = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, 1)).Trim()
            $l = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, 2)).Trim()
            $e = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, 3)).Trim()
            $p = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, 5)).Trim()
            $info = [PSCustomObject]@{ userName = $u; firstName = $f; lastName = $l; employeeId = $e; folderPath = $p }
            if ($u) {
                $uk = $u.ToLowerInvariant()
                if (-not $byUser.ContainsKey($uk)) { $byUser[$uk] = New-Object System.Collections.Generic.List[object] }
                $byUser[$uk].Add($info)
            }
            if ($e) {
                $ek = $e.ToLowerInvariant()
                if (-not $byEmp.ContainsKey($ek)) { $byEmp[$ek] = New-Object System.Collections.Generic.List[object] }
                $byEmp[$ek].Add($info)
            }
        }
    } finally {
        [SceWin32]::PostMessage($findDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        for ($w = 0; $w -lt 30; $w++) {
            if (-not [SceWin32]::IsWindow($findDlg) -or -not [SceWin32]::IsWindowVisible($findDlg)) { break }
            Start-Sleep -Milliseconds 50
        }
        Wait-CmOperationComplete 2000 | Out-Null
    }
    return [PSCustomObject]@{
        TotalGlobal = $rowCount
        ByEmp       = $byEmp
        ByUser      = $byUser
    }
}

function Invoke-CmCheckPersons {
    param($Session, [object[]]$Rows, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    & $Log ("[중복 체크] CM 전체 Person 목록 조회 중... (대상 {0}건)" -f $Rows.Count)
    $idx = Get-GlobalPersonIndex $Session $Log
    & $Log ("  -> CM 전체 Person {0}건 인덱싱 완료" -f $idx.TotalGlobal)

    $dupEmpIds = New-Object System.Collections.Generic.List[string]
    $dupUserNames = New-Object System.Collections.Generic.List[string]
    $empMatches = New-Object System.Collections.Generic.List[object]
    $userMatches = New-Object System.Collections.Generic.List[object]
    $cleanEmpIds = New-Object System.Collections.Generic.List[string]

    foreach ($row in $Rows) {
        $eid = ([string]$row.EmployeeId).Trim()
        $un  = ([string]$row.UserName).Trim()
        $ek = $eid.ToLowerInvariant()
        $uk = $un.ToLowerInvariant()

        if ($eid -and $idx.ByEmp.ContainsKey($ek)) {
            $dupEmpIds.Add($eid)
            foreach ($m in $idx.ByEmp[$ek]) {
                $empMatches.Add([PSCustomObject]@{
                    inputId    = $eid
                    userName   = $m.userName
                    employeeId = $m.employeeId
                    folderPath = $m.folderPath
                })
            }
        } elseif ($eid) {
            $cleanEmpIds.Add($eid)
        }

        if ($un -and $idx.ByUser.ContainsKey($uk)) {
            $dupUserNames.Add($un)
            foreach ($m in $idx.ByUser[$uk]) {
                $userMatches.Add([PSCustomObject]@{
                    inputId    = $un
                    userName   = $m.userName
                    employeeId = $m.employeeId
                    folderPath = $m.folderPath
                })
            }
        }
    }

    & $Log ("[중복 체크 완료] Employee ID 중복 {0}건 / User Name 중복 {1}건 / 사용 가능 {2}건" -f $dupEmpIds.Count, $dupUserNames.Count, $cleanEmpIds.Count)
    return [PSCustomObject]@{
        totalChecked      = $Rows.Count
        globalPersonCount = $idx.TotalGlobal
        dupEmpIds         = @($dupEmpIds | Select-Object -Unique)
        dupUserNames      = @($dupUserNames | Select-Object -Unique)
        cleanEmpIds       = @($cleanEmpIds | Select-Object -Unique)
        empMatches        = $empMatches.ToArray()
        userMatches       = $userMatches.ToArray()
    }
}

function Invoke-SelectAgentLoginInBrowse {
    param(
        $Session,
        [IntPtr]$ParentNewDlg,
        [IntPtr]$AddDlg,
        [string]$LoginId,
        [string[]]$AgentLoginRelPath
    )
    $btnBrowse = Find-ChildCtrl $AddDlg "Button" 1057 $false
    $cbLogin   = Find-ChildCtrl $AddDlg "ComboBox" 1055 $false
    $addOk     = Find-ChildCtrl $AddDlg "Button" 1 $false
    if ($btnBrowse -eq [IntPtr]::Zero -or $cbLogin -eq [IntPtr]::Zero -or $addOk -eq [IntPtr]::Zero) {
        throw "Add ID 창 컨트롤(Browse/Login/OK)을 찾지 못했습니다."
    }

    [ScePersonWin32]::PostMessage($btnBrowse, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $bd = [IntPtr]::Zero
    for ($w = 0; $w -lt 30; $w++) {
        Start-Sleep -Milliseconds 100
        $bd = Get-SceDialogs | Where-Object { $_ -ne $ParentNewDlg -and $_ -ne $AddDlg } | Select-Object -First 1
        if ($bd) { break }
    }
    if ($bd -eq [IntPtr]::Zero -or -not $bd) { throw "Browse 창이 열리지 않았습니다." }

    $lvBr = Find-ChildCtrl $bd "SysListView32" 1063 $false
    $stBr = Find-ChildCtrl $bd "Static" 1153 $false
    $brOk = Find-ChildCtrl $bd "Button" 1 $false
    $tbBr = Find-ChildCtrl $bd "ToolbarWindow32" 59392 $false

    $waitBrowseListStable = {
        $lastCnt = -1
        for ($w = 0; $w -lt 25; $w++) {
            $curCnt = [int][ScePersonWin32]::SendMessage($lvBr, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
            if ($curCnt -gt 0 -and $curCnt -eq $lastCnt) { break }
            $lastCnt = $curCnt
            Start-Sleep -Milliseconds 60
        }
    }
    & $waitBrowseListStable

    $navSegments = @("TServerAlcatel", "Agent Logins")
    if ($AgentLoginRelPath) {
        foreach ($s in $AgentLoginRelPath) { $navSegments += [string]$s }
    }

    # Ensure Browse starts at Switches level containing TServerAlcatel
    if ($tbBr -ne [IntPtr]::Zero) {
        for ($up = 0; $up -lt 4; $up++) {
            $curSt = [SceWin32]::GetTextWM($stBr)
            if ($curSt -match "/Switches/'?$") { break }
            $foundRootSeg = $false
            $cnt0 = [int][ScePersonWin32]::SendMessage($lvBr, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
            for ($i = 0; $i -lt $cnt0; $i++) {
                if (([SceWin32]::GetListItemText32($Session.HProc, $lvBr, $i, 0)).Trim() -eq $navSegments[0]) { $foundRootSeg = $true; break }
            }
            if ($foundRootSeg) { break }
            [ScePersonWin32]::PostMessage($bd, 0x0111, [IntPtr]32809, $tbBr) | Out-Null # Up One Level
            for ($uw = 0; $uw -lt 20; $uw++) {
                Start-Sleep -Milliseconds 60
                if (([SceWin32]::GetTextWM($stBr)) -ne $curSt) { break }
            }
            & $waitBrowseListStable
        }
    }

    foreach ($seg in $navSegments) {
        $enteredSeg = $false
        $lastErrDetail = ""
        for ($navAttempt = 0; $navAttempt -lt 3; $navAttempt++) {
            $prevStatic = [SceWin32]::GetTextWM($stBr)
            $targetIdx = -1
            for ($findTry = 0; $findTry -lt 8; $findTry++) {
                $cnt = [int][ScePersonWin32]::SendMessage($lvBr, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                for ($i = 0; $i -lt $cnt; $i++) {
                    $txt = ([SceWin32]::GetListItemText32($Session.HProc, $lvBr, $i, 0)).Trim()
                    if ($txt -eq $seg) { $targetIdx = $i; break }
                }
                if ($targetIdx -ge 0) { break }
                Start-Sleep -Milliseconds 60
            }
            if ($targetIdx -lt 0) {
                [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                throw "Browse 경로 '$seg' 폴더를 찾을 수 없습니다. (위치: $prevStatic)"
            }

            [SceWin32]::SelectListItem($Session.HProc, $lvBr, $targetIdx)
            Start-Sleep -Milliseconds 30
            $dblRes = [ScePersonWin32]::DoubleClickBrowseItem($Session.HProc, $bd, $lvBr, $targetIdx, $stBr, $prevStatic, 1800)
            if ($dblRes -eq "LOCKED_SCREEN") {
                [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                throw "Windows 화면이 잠겨 있어 CM Browse 폴더를 더블클릭할 수 없습니다. 화면 잠금을 해제한 뒤 다시 시도하거나 'Agent Login 적용하기' 체크를 해제해 주세요."
            }

            $nowStatic = [SceWin32]::GetTextWM($stBr)
            $segPattern = '/' + [regex]::Escape($seg) + "/'?$"
            if ($dblRes -eq "OK" -and ($nowStatic -match $segPattern)) {
                $enteredSeg = $true
                break
            }

            if ($nowStatic -ne $prevStatic) {
                $lastErrDetail = "잘못된 Browse 폴더 진입 ($nowStatic != $seg)"
                if ($tbBr -ne [IntPtr]::Zero) {
                    [ScePersonWin32]::PostMessage($bd, 0x0111, [IntPtr]32809, $tbBr) | Out-Null # Up One Level
                    for ($uw = 0; $uw -lt 25; $uw++) {
                        Start-Sleep -Milliseconds 60
                        if (([SceWin32]::GetTextWM($stBr)) -eq $prevStatic) { break }
                    }
                    & $waitBrowseListStable
                }
            } elseif ($dblRes -eq "TIMEOUT") {
                $lastErrDetail = "Browse 폴더 '$seg' 진입 실패 (화면 잠금/포커스 차단 여부 확인)"
                Start-Sleep -Milliseconds 80
            } else {
                $lastErrDetail = "Browse 폴더 이동 실패 ($dblRes): $seg"
                Start-Sleep -Milliseconds 80
            }
        }
        if (-not $enteredSeg) {
            [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            throw $(if ($lastErrDetail) { $lastErrDetail } else { "Browse 폴더 '$seg' 진입 실패" })
        }
        & $waitBrowseListStable
    }

    # Select exact Agent Login inside folder
    $loginIdx = -1
    for ($findTry = 0; $findTry -lt 10; $findTry++) {
        $leafCnt = [int][ScePersonWin32]::SendMessage($lvBr, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
        for ($i = 0; $i -lt $leafCnt; $i++) {
            $txt = ([SceWin32]::GetListItemText32($Session.HProc, $lvBr, $i, 0)).Trim()
            if ($txt -eq $LoginId) { $loginIdx = $i; break }
        }
        if ($loginIdx -ge 0) { break }
        Start-Sleep -Milliseconds 60
    }
    if ($loginIdx -lt 0) {
        [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "Browse 목록에서 Agent Login '$LoginId'를 찾을 수 없습니다."
    }

    [ScePersonWin32]::SendMessage($lvBr, 0x1013, [IntPtr]$loginIdx, [IntPtr]::Zero) | Out-Null # LVM_ENSUREVISIBLE
    [SceWin32]::SelectListItem($Session.HProc, $lvBr, $loginIdx)
    for ($w = 0; $w -lt 20; $w++) {
        if ([ScePersonWin32]::IsWindowEnabled($brOk)) { break }
        if ($w -eq 5 -or $w -eq 12) {
            [SceWin32]::SelectListItem($Session.HProc, $lvBr, $loginIdx)
        }
        Start-Sleep -Milliseconds 50
    }
    if (-not [ScePersonWin32]::IsWindowEnabled($brOk)) {
        [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "Browse에서 Agent Login '$LoginId' 선택 후 OK 버튼이 활성화되지 않았습니다."
    }
    [ScePersonWin32]::PostMessage($brOk, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    for ($w = 0; $w -lt 30; $w++) {
        Start-Sleep -Milliseconds 80
        $extra = @(Get-SceDialogs | Where-Object { $_ -ne $ParentNewDlg -and $_ -ne $AddDlg -and $_ -ne $bd })
        if ($extra.Count -gt 0) {
            $errMsg = Get-CmDialogStaticText $extra[0]
            foreach ($ex in $extra) { [ScePersonWin32]::PostMessage($ex, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
            [ScePersonWin32]::PostMessage($bd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            throw "Browse 선택 오류: $errMsg"
        }
        if (-not [SceWin32]::IsWindowVisible($bd)) { break }
    }

    $chosen = ""
    for ($cw = 0; $cw -lt 15; $cw++) {
        Start-Sleep -Milliseconds 60
        $chosen = ([SceWin32]::GetTextWM($cbLogin)).Trim()
        if ($chosen -eq $LoginId) { break }
    }
    if ($chosen -ne $LoginId) {
        [ScePersonWin32]::PostMessage($AddDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "Agent Login 선택 확인 실패 (예상: $LoginId, 실제: $chosen)"
    }
    [ScePersonWin32]::PostMessage($addOk, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $addClosed = $false
    for ($w = 0; $w -lt 40; $w++) {
        Start-Sleep -Milliseconds 80
        $extraAdd = @(Get-SceDialogs | Where-Object { $_ -ne $ParentNewDlg -and $_ -ne $AddDlg })
        if ($extraAdd.Count -gt 0) {
            $errMsg = Get-CmDialogStaticText $extraAdd[0]
            foreach ($ex in $extraAdd) { [ScePersonWin32]::PostMessage($ex, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
            [ScePersonWin32]::PostMessage($AddDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            throw "Agent Login 추가 오류 ($LoginId): $errMsg"
        }
        if (-not [SceWin32]::IsWindowVisible($AddDlg)) {
            $addClosed = $true
            break
        }
    }
    if (-not $addClosed) {
        [ScePersonWin32]::PostMessage($AddDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        throw "New Agent Login Info 대화상자가 닫히지 않았습니다 ($LoginId)"
    }
}

function Invoke-SavePersonDialog {
    param($Session, [IntPtr]$DlgHwnd)
    $okBtn = Find-ChildCtrl $DlgHwnd "Button" 1 $false
    if ($okBtn -eq [IntPtr]::Zero) { throw "Person 저장 OK 버튼을 찾을 수 없습니다." }
    [ScePersonWin32]::PostMessage($okBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

    $errorCaptured = ""
    $clickedConfirm = New-Object 'System.Collections.Generic.HashSet[long]'
    for ($w = 0; $w -lt 60; $w++) {
        Start-Sleep -Milliseconds 100
        $dlgs = @(Get-SceDialogs)
        if ($dlgs.Count -eq 0) { return }
        foreach ($d in $dlgs) {
            if ($d -eq $DlgHwnd) { continue }
            $msg = Get-CmDialogStaticText $d
            $cs = Get-CmDialogControls $d
            if ($msg -match "Access Groups") {
                if ($clickedConfirm.Add($d.ToInt64())) {
                    $yesBtn = $cs["Button/6"]
                    if (-not $yesBtn) { $yesBtn = $cs["Button/1"] }
                    if ($yesBtn) { [ScePersonWin32]::PostMessage($yesBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                }
            } else {
                $errorCaptured = $msg
                $dismiss = $cs["Button/1"]; if (-not $dismiss) { $dismiss = $cs["Button/2"] }
                if ($dismiss) { [ScePersonWin32]::PostMessage($dismiss, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                else { [ScePersonWin32]::PostMessage($d, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                [ScePersonWin32]::PostMessage($DlgHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                for ($cw = 0; $cw -lt 20; $cw++) {
                    Start-Sleep -Milliseconds 60
                    if ((Get-SceDialogs).Count -eq 0) { break }
                }
                throw "CM 저장 오류: $errorCaptured"
            }
        }
    }
    Close-AllSceModals
    throw "CM Person 저장 응답 시간 초과"
}

function Get-AgentInfoTabControls {
    param([IntPtr]$PersonDlg)
    $tab = Find-ChildCtrl $PersonDlg "SysTabControl32" -999 $false
    if ($tab -eq [IntPtr]::Zero) { throw "Person 대화상자 탭 컨트롤을 찾지 못했습니다." }
    for ($t = 0; $t -lt 3; $t++) {
        [ScePersonWin32]::SendMessage($tab, 0x1330, [IntPtr]1, [IntPtr]::Zero) | Out-Null # TCM_SETCURFOCUS -> Agent Info
        Start-Sleep -Milliseconds 35
        $curSel = [int][ScePersonWin32]::SendMessage($tab, 0x130B, [IntPtr]::Zero, [IntPtr]::Zero)
        if ($curSel -eq 1) { break }
    }
    $lv24 = [IntPtr]::Zero
    $addBtn = [IntPtr]::Zero
    $delBtn = [IntPtr]::Zero
    for ($w = 0; $w -lt 25; $w++) {
        if ($lv24 -eq [IntPtr]::Zero) { $lv24 = Find-ChildCtrl $PersonDlg "SysListView32" 1024 $true }
        if ($addBtn -eq [IntPtr]::Zero) { $addBtn = Find-ChildCtrl $PersonDlg "Button" 1027 $true }
        if ($delBtn -eq [IntPtr]::Zero) { $delBtn = Find-ChildCtrl $PersonDlg "Button" 1028 $true }
        if ($lv24 -ne [IntPtr]::Zero -and $addBtn -ne [IntPtr]::Zero -and $delBtn -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 35
    }
    if ($lv24 -eq [IntPtr]::Zero -or $delBtn -eq [IntPtr]::Zero) {
        throw "Agent Info 탭의 Login IDs 목록 또는 Delete ID 버튼을 찾을 수 없습니다."
    }
    $prevCnt = -1
    for ($sw = 0; $sw -lt 5; $sw++) {
        Start-Sleep -Milliseconds $(if ($sw -eq 0) { 55 } else { 40 })
        $curCnt = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
        if ($curCnt -eq $prevCnt -and ($curCnt -gt 0 -or $sw -ge 2)) { break }
        $prevCnt = $curCnt
    }
    return [PSCustomObject]@{ ListHwnd = $lv24; AddBtn = $addBtn; DelBtn = $delBtn }
}

function Invoke-DeleteLoginRowInDialog {
    param($Session, [IntPtr]$PersonDlg, [IntPtr]$Lv24, [IntPtr]$DelBtn, [int]$RowIndex)
    $prevCnt = [int][ScePersonWin32]::SendMessage($Lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
    if ($RowIndex -lt 0 -or $RowIndex -ge $prevCnt) { return $false }

    [SceWin32]::SelectListItem($Session.HProc, $Lv24, $RowIndex)
    Start-Sleep -Milliseconds 60
    [ScePersonWin32]::PostMessage($DelBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

    for ($w = 0; $w -lt 25; $w++) {
        Start-Sleep -Milliseconds 60
        $extra = @(Get-SceDialogs | Where-Object { $_ -ne $PersonDlg })
        foreach ($sd in $extra) {
            $scs = Get-CmDialogControls $sd
            $yesBtn = $scs["Button/6"]
            if (-not $yesBtn) { $yesBtn = $scs["Button/1"] }
            if ($yesBtn) {
                [ScePersonWin32]::PostMessage($yesBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            }
        }
        $nowCnt = [int][ScePersonWin32]::SendMessage($Lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
        if ($nowCnt -lt $prevCnt) { return $true }
    }
    throw "Agent Info > Delete ID 실행 후 Login 항목이 제거되지 않았습니다."
}

function Parse-AgentLoginTokens {
    param([string]$RawText)
    $list = New-Object System.Collections.Generic.List[string]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if ([string]::IsNullOrWhiteSpace($RawText)) { return $list.ToArray() }
    foreach ($part in ($RawText -split '[,;\s]+')) {
        $p = $part.Trim()
        if ($p -and $seen.Add($p)) { $list.Add($p) }
    }
    return $list.ToArray()
}

function Find-PersonRowIndexInActiveFolder {
    param($Session, [string]$UserName, [string]$EmployeeId)
    $idx = -1
    if ($UserName) {
        $idx = Find-CmListItemIndex $Session $UserName 0
    }
    if ($idx -lt 0 -and $EmployeeId) {
        $idx = Find-CmListItemIndex $Session $EmployeeId 3
    }
    return $idx
}

function Invoke-CmCreatePersons {
    param(
        $Session,
        $CustomerObj,
        [object[]]$Rows,
        [bool]$CreateGeneral = $true,
        [bool]$ApplyAgentLogin = $true,
        [scriptblock]$Log = { param($m) [Console]::WriteLine($m) }
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Close-AllSceModals

    $pResolved = Resolve-PersonsFolderNode $Session $CustomerObj
    if ($null -eq $pResolved) {
        throw "CM Persons 폴더에서 '$($CustomerObj.name)' 고객사 폴더를 찾지 못했습니다."
    }
    & $Log ("[대상 폴더] {0} (대상 {1}건)" -f $pResolved.DisplayPath, $Rows.Count)

    $idx = Get-GlobalPersonIndex $Session $Log
    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
    Start-Sleep -Milliseconds 120
    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null # Refresh
    Start-Sleep -Milliseconds 220

    $createdEmpIds = New-Object System.Collections.Generic.List[string]
    $createdItems = New-Object System.Collections.Generic.List[object]
    $skippedList = New-Object System.Collections.Generic.List[object]
    $failedList = New-Object System.Collections.Generic.List[object]

    $agentLoginRel = if ($CustomerObj.agentLogin) { @($CustomerObj.agentLogin | ForEach-Object { [string]$_ }) } else { @() }

    foreach ($row in $Rows) {
        $un = ([string]$row.UserName).Trim()
        $fn = ([string]$row.FirstName).Trim()
        $ln = ([string]$row.LastName).Trim()
        $eid = ([string]$row.EmployeeId).Trim()
        $aid = if ($null -ne $row.AgentLogin) { ([string]$row.AgentLogin).Trim() } else { "" }
        $wantIsAgent = if ($null -ne $row.PSObject.Properties['IsAgent']) { [bool]$row.IsAgent } else { $true }
        if ($ApplyAgentLogin -and $aid) { $wantIsAgent = $true }

        if (-not $un -or -not $fn -or -not $ln -or -not $eid) {
            $failedList.Add([PSCustomObject]@{ employeeId = $eid; userName = $un; reason = "필수 필드 누락" })
            & $Log ("  [실패] {0} -> 필수 필드 누락" -f $eid)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $un -EmpId $eid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Create" -Reason "필수 필드 누락"
            continue
        }

        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
        [ScePersonWin32]::PostMessage($Session.TreeHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 100

        if ($CreateGeneral) {
            if ($idx.ByEmp.ContainsKey($eid.ToLowerInvariant()) -or $idx.ByUser.ContainsKey($un.ToLowerInvariant())) {
                $why = if ($idx.ByUser.ContainsKey($un.ToLowerInvariant())) { "User Name 중복($un)" } else { "Employee ID 중복($eid)" }
                $skippedList.Add([PSCustomObject]@{ employeeId = $eid; userName = $un; reason = $why })
                & $Log ("  [건너뜀] {0} ({1}) -> {2}" -f $un, $eid, $why)
                continue
            }

            try {
                [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8002, [IntPtr]::Zero) | Out-Null
                $nd = Wait-CmDialogOpen 3500
                if ($nd -eq [IntPtr]::Zero) { throw "New Person 대화상자가 열리지 않았습니다." }

                $tab = Find-ChildCtrl $nd "SysTabControl32" -999 $false
                $genPage = Find-ChildCtrl $nd "#32770" 133 $false
                if ($genPage -eq [IntPtr]::Zero) { throw "New Person General 탭을 찾을 수 없습니다." }

                $genCs = Get-CmDialogControls $genPage
                [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1036"], (Get-ChildEditOfCombo $genCs["ComboBox/1036"]), $fn)
                [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1037"], (Get-ChildEditOfCombo $genCs["ComboBox/1037"]), $ln)
                [SceWin32]::SetEditText($genCs["Edit/1005"], $eid)
                [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1039"], (Get-ChildEditOfCombo $genCs["ComboBox/1039"]), $un)

                # Leave Password blank (default), set Is Agent checkbox per row (notify parent via WM_COMMAND)
                if ($genCs.ContainsKey("Button/1040")) {
                    $chk = [int][ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)
                    $targetChk = if ($wantIsAgent) { 1 } else { 0 }
                    if ($chk -ne $targetChk) {
                        [ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F1, [IntPtr]$targetChk, [IntPtr]::Zero) | Out-Null
                        [ScePersonWin32]::SendMessage($genPage, 0x0111, [IntPtr]1040, $genCs["Button/1040"]) | Out-Null
                    }
                }

                $loginLinked = $false
                if ($ApplyAgentLogin -and $aid) {
                    [ScePersonWin32]::SendMessage($tab, 0x1330, [IntPtr]1, [IntPtr]::Zero) | Out-Null
                    $addBtn = [IntPtr]::Zero
                    for ($w = 0; $w -lt 12; $w++) {
                        Start-Sleep -Milliseconds 35
                        $addBtn = Find-ChildCtrl $nd "Button" 1027 $true
                        if ($addBtn -ne [IntPtr]::Zero) { break }
                    }
                    if ($addBtn -eq [IntPtr]::Zero) { throw "Agent Info > Add ID 버튼을 찾지 못했습니다." }
                    [ScePersonWin32]::PostMessage($addBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                    $ad = [IntPtr]::Zero
                    for ($w = 0; $w -lt 25; $w++) {
                        Start-Sleep -Milliseconds 80
                        $ad = Get-SceDialogs | Where-Object { $_ -ne $nd } | Select-Object -First 1
                        if ($ad) { break }
                    }
                    if (-not $ad -or $ad -eq [IntPtr]::Zero) { throw "New Agent Login Info 창이 열리지 않았습니다." }
                    Invoke-SelectAgentLoginInBrowse $Session $nd $ad $aid $agentLoginRel
                    $loginLinked = $true
                }

                Invoke-SavePersonDialog $Session $nd

                # Verify created in folder ListView (wait for ConfigServer notification / trigger Refresh if needed)
                $vIdx = -1
                for ($vw = 0; $vw -lt 40; $vw++) {
                    if ($vw -eq 3 -or $vw -eq 12 -or $vw -eq 25) {
                        [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null # Refresh
                    }
                    Start-Sleep -Milliseconds 100
                    $vIdx = Find-CmListItemIndex $Session $un 0
                    if ($vIdx -ge 0) { break }
                }
                if ($vIdx -lt 0) { throw "저장 후 폴더 목록에서 '$un'을 확인할 수 없습니다." }

                $createdEmpIds.Add($eid)
                $createdItems.Add([PSCustomObject]@{ userName = $un; employeeId = $eid; agentLogin = $(if ($loginLinked) { $aid } else { "" }) })
                $idx.ByUser[$un.ToLowerInvariant()] = New-Object System.Collections.Generic.List[object]
                $idx.ByEmp[$eid.ToLowerInvariant()] = New-Object System.Collections.Generic.List[object]
                $loginTag = if ($loginLinked) { " + Agent Login: $aid" } elseif ($ApplyAgentLogin -and -not $aid) { " (Agent Login 미지정)" } else { "" }
                & $Log ("  [생성 완료] User='{0}', First='{1}', Last='{2}', EmpID='{3}'{4}" -f $un, $fn, $ln, $eid, $loginTag)
                $auditLogins = if ($loginLinked) { @($aid) } else { @() }
                Write-CmAuditEvent -Action "CREATE" -Target "CM" -ObjectType "Person" -Name $un -EmpId $eid -FirstName $fn -LastName $ln -IsAgent ([string]$wantIsAgent) -LinkedLogins $auditLogins -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath
            } catch {
                Close-AllSceModals
                $msg = $_.Exception.Message
                $failedList.Add([PSCustomObject]@{ employeeId = $eid; userName = $un; reason = $msg })
                & $Log ("  [실패] {0} ({1}) -> {2}" -f $un, $eid, $msg)
                Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $un -EmpId $eid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Create" -Reason $msg
            }
        } elseif ($ApplyAgentLogin -and $aid) {
            # General unchecked + Agent Login checked: attach Login to existing Person in folder
            try {
                $existIdx = Find-CmListItemIndex $Session $un 0
                if ($existIdx -lt 0) { throw "폴더 내에 기존 Person '$un'이 존재하지 않습니다." }
                $pd = Open-PersonPropertiesDialog $Session $existIdx $un 3000
                if ($pd -eq [IntPtr]::Zero) { throw "기존 Person 속성 창을 열 수 없습니다: $un" }
                $genPage = Find-ChildCtrl $pd "#32770" 133 $false
                if ($genPage -ne [IntPtr]::Zero) {
                    $genCs = Get-CmDialogControls $genPage
                    if ($genCs.ContainsKey("Button/1040")) {
                        $chk = [int][ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)
                        if ($chk -eq 0) {
                            [ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F1, [IntPtr]1, [IntPtr]::Zero) | Out-Null
                            [ScePersonWin32]::SendMessage($genPage, 0x0111, [IntPtr]1040, $genCs["Button/1040"]) | Out-Null
                        }
                    }
                }
                $agCtrls = Get-AgentInfoTabControls $pd
                $addBtn = $agCtrls.AddBtn
                if ($addBtn -eq [IntPtr]::Zero) { throw "Agent Info > Add ID 버튼을 찾지 못했습니다." }
                [ScePersonWin32]::PostMessage($addBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                $ad = [IntPtr]::Zero
                for ($w = 0; $w -lt 25; $w++) {
                    Start-Sleep -Milliseconds 80
                    $ad = Get-SceDialogs | Where-Object { $_ -ne $pd } | Select-Object -First 1
                    if ($ad) { break }
                }
                if (-not $ad -or $ad -eq [IntPtr]::Zero) { throw "New Agent Login Info 창이 열리지 않았습니다." }
                Invoke-SelectAgentLoginInBrowse $Session $pd $ad $aid $agentLoginRel
                Invoke-SavePersonDialog $Session $pd
                $createdEmpIds.Add($eid)
                $createdItems.Add([PSCustomObject]@{ userName = $un; employeeId = $eid; agentLogin = $aid })
                & $Log ("  [Login 연결 완료] User='{0}' + Agent Login: {1}" -f $un, $aid)
                Write-CmAuditEvent -Action "LINK" -Target "CM" -ObjectType "Person" -Name $un -EmpId $eid -LinkedLogins @($aid) -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath
            } catch {
                Close-AllSceModals
                $msg = $_.Exception.Message
                $failedList.Add([PSCustomObject]@{ employeeId = $eid; userName = $un; reason = $msg })
                & $Log ("  [실패] {0} -> {1}" -f $un, $msg)
                Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $un -EmpId $eid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "LinkAgentLogin" -Reason $msg
            }
        }
    }

    $sw.Stop()
    & $Log ("[Person 작업 완료] 성공 {0}건 / 건너뜀 {1}건 / 실패 {2}건 ({3:N1}초)" -f $createdEmpIds.Count, $skippedList.Count, $failedList.Count, $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        customer       = [string]$CustomerObj.name
        folderPath     = $pResolved.DisplayPath
        elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        createdEmpIds  = $createdEmpIds.ToArray()
        createdItems   = $createdItems.ToArray()
        skipped        = $skippedList.ToArray()
        failed         = $failedList.ToArray()
    }
}

function Invoke-CmFetchPersonsInFolder {
    param($Session, $CustomerObj, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Close-AllSceModals

    $pResolved = Resolve-PersonsFolderNode $Session $CustomerObj
    if ($null -eq $pResolved) {
        throw "CM Persons 폴더에서 '$($CustomerObj.name)' 고객사 폴더를 찾지 못했습니다."
    }

    & $Log ("[Person 목록 조회] {0}" -f $pResolved.DisplayPath)
    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
    Start-Sleep -Milliseconds 120
    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250

    $rowCount = [int][ScePersonWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
    $rawRows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $rowCount; $i++) {
        $un = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 0)).Trim()
        if (-not $un) { continue }
        $fn = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 1)).Trim()
        $ln = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 2)).Trim()
        $eid = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 3)).Trim()
        $agStr = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, 4)).Trim()
        $isAg = [string]::Equals($agStr, "True", [System.StringComparison]::OrdinalIgnoreCase)
        $rawRows.Add([PSCustomObject]@{
            Index      = $i
            UserName   = $un
            FirstName  = $fn
            LastName   = $ln
            EmployeeId = $eid
            IsAgent    = $isAg
        })
    }

    $agentTargets = @($rawRows | Where-Object { $_.IsAgent })
    & $Log ("  -> 폴더 내 Person {0}명 로드 (Agent 계정 {1}명 Agent Login 확인 중...)" -f $rawRows.Count, $agentTargets.Count)

    $loginByUser = @{}
    $done = 0
    foreach ($ar in $agentTargets) {
        $done++
        $assigned = Read-PersonAgentLogins $Session $ar.Index $ar.UserName
        $loginByUser[$ar.UserName] = @($assigned)
        if (($done % 5 -eq 0) -or ($done -eq $agentTargets.Count)) {
            & $Log ("  -> 진행: {0}/{1}명 Login 확인 ({2})" -f $done, $agentTargets.Count, $ar.UserName)
        }
    }

    $items = New-Object System.Collections.Generic.List[object]
    foreach ($r in $rawRows) {
        $lArr = if ($loginByUser.ContainsKey($r.UserName)) { @($loginByUser[$r.UserName]) } else { @() }
        $items.Add([PSCustomObject]@{
            userName    = $r.UserName
            firstName   = $r.FirstName
            lastName    = $r.LastName
            employeeId  = $r.EmployeeId
            isAgent     = [bool]$r.IsAgent
            agentLogin  = ($lArr -join ", ")
            agentLogins = @($lArr)
        })
    }

    $sw.Stop()
    & $Log ("[Person 목록 조회 완료] 총 {0}명 조회 ({1:N1}초)" -f $items.Count, $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        customer       = [string]$CustomerObj.name
        folderPath     = $pResolved.DisplayPath
        elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        totalCount     = $items.Count
        items          = $items.ToArray()
    }
}

function Invoke-CmUpdatePersons {
    param($Session, $CustomerObj, [object[]]$Rows, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Close-AllSceModals

    $pResolved = Resolve-PersonsFolderNode $Session $CustomerObj
    if ($null -eq $pResolved) {
        throw "CM Persons 폴더에서 '$($CustomerObj.name)' 고객사 폴더를 찾지 못했습니다."
    }
    & $Log ("[Person 수정] {0} (대상 {1}건)" -f $pResolved.DisplayPath, $Rows.Count)

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
    Start-Sleep -Milliseconds 120
    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 220

    $agentLoginRel = if ($CustomerObj.agentLogin) { @($CustomerObj.agentLogin | ForEach-Object { [string]$_ }) } else { @() }
    $updatedItems = New-Object System.Collections.Generic.List[object]
    $failedList = New-Object System.Collections.Generic.List[object]

    foreach ($row in $Rows) {
        $origUn = ([string]$row.OriginalUserName).Trim()
        $origEid = ([string]$row.OriginalEmployeeId).Trim()
        $newUn = ([string]$row.UserName).Trim()
        $newFn = ([string]$row.FirstName).Trim()
        $newLn = ([string]$row.LastName).Trim()
        $newEid = ([string]$row.EmployeeId).Trim()
        $doGen = [bool]$row.UpdateGeneral
        $doLogin = [bool]$row.UpdateAgentLogin
        [string[]]$desiredLogins = @(Parse-AgentLoginTokens ([string]$row.AgentLogin))
        $wantAgent = ([bool]$row.IsAgent) -or ($doLogin -and $desiredLogins.Count -gt 0)

        if (-not $origUn) { $origUn = $newUn }
        if (-not $newUn) {
            $failedList.Add([PSCustomObject]@{ originalUserName = $origUn; employeeId = $newEid; userName = $newUn; reason = "User Name이 비어 있습니다." })
            & $Log ("  [실패] {0} -> User Name 누락" -f $origUn)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $origUn -EmpId $newEid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Update" -Reason "User Name이 비어 있습니다."
            continue
        }

        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
        Start-Sleep -Milliseconds 100
        $idx = Find-PersonRowIndexInActiveFolder $Session $origUn $origEid
        if ($idx -lt 0) {
            [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 250
            $idx = Find-PersonRowIndexInActiveFolder $Session $origUn $origEid
        }
        if ($idx -lt 0) {
            $failedList.Add([PSCustomObject]@{ originalUserName = $origUn; employeeId = $newEid; userName = $newUn; reason = "CM 폴더에서 기존 Person을 찾을 수 없습니다." })
            & $Log ("  [실패] {0} ({1}) -> CM 폴더 미존재" -f $origUn, $origEid)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $origUn -EmpId $newEid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Update" -Reason "CM 폴더에서 기존 Person을 찾을 수 없습니다."
            continue
        }

        $removed = New-Object System.Collections.Generic.List[string]
        $added = New-Object System.Collections.Generic.List[string]
        try {
            $pd = Open-PersonPropertiesDialog $Session $idx $origUn 3500
            if ($pd -eq [IntPtr]::Zero) { throw "Person 속성 창을 열 수 없습니다: $origUn" }

            # General Tab (Never touch Password 1010/1011!)
            $genPage = Find-ChildCtrl $pd "#32770" 133 $false
            if ($genPage -eq [IntPtr]::Zero) { throw "Person General 탭을 찾을 수 없습니다." }
            $genCs = Get-CmDialogControls $genPage

            if ($doGen) {
                $curFn = ([SceWin32]::GetTextWM($genCs["ComboBox/1036"])).Trim()
                $curLn = ([SceWin32]::GetTextWM($genCs["ComboBox/1037"])).Trim()
                $curEid = ([SceWin32]::GetTextWM($genCs["Edit/1005"])).Trim()
                $curUn = ([SceWin32]::GetTextWM($genCs["ComboBox/1039"])).Trim()

                if ($curFn -ne $newFn) { [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1036"], (Get-ChildEditOfCombo $genCs["ComboBox/1036"]), $newFn) }
                if ($curLn -ne $newLn) { [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1037"], (Get-ChildEditOfCombo $genCs["ComboBox/1037"]), $newLn) }
                if ($curEid -ne $newEid) { [SceWin32]::SetEditText($genCs["Edit/1005"], $newEid) }
                if ($curUn -ne $newUn) { [ScePersonWin32]::SetComboEditText($genPage, $genCs["ComboBox/1039"], (Get-ChildEditOfCombo $genCs["ComboBox/1039"]), $newUn) }

                if ($genCs.ContainsKey("Button/1040")) {
                    $chkNow = [int][ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)
                    $chkWant = if ($wantAgent) { 1 } else { 0 }
                    if ($chkNow -ne $chkWant) {
                        [ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F1, [IntPtr]$chkWant, [IntPtr]::Zero) | Out-Null
                        [ScePersonWin32]::SendMessage($genPage, 0x0111, [IntPtr]1040, $genCs["Button/1040"]) | Out-Null
                    }
                }
            } elseif ($doLogin -and $desiredLogins.Count -gt 0 -and $genCs.ContainsKey("Button/1040")) {
                $chkNow = [int][ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)
                if ($chkNow -eq 0) {
                    [ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F1, [IntPtr]1, [IntPtr]::Zero) | Out-Null
                    [ScePersonWin32]::SendMessage($genPage, 0x0111, [IntPtr]1040, $genCs["Button/1040"]) | Out-Null
                }
            }

            # Agent Info Tab (only when Agent Login changed)
            $loginDeltaLog = ""
            if ($doLogin) {
                $agCtrls = Get-AgentInfoTabControls $pd
                $lv24 = $agCtrls.ListHwnd
                $addBtn = $agCtrls.AddBtn
                $delBtn = $agCtrls.DelBtn

                $curLogins = New-Object System.Collections.Generic.List[string]
                $cnt24 = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                for ($li = 0; $li -lt $cnt24; $li++) {
                    $lid = ([SceWin32]::GetListItemText32($Session.HProc, $lv24, $li, 0)).Trim()
                    if ($lid) { $curLogins.Add($lid) }
                }

                $desiredSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                foreach ($dl in $desiredLogins) { [void]$desiredSet.Add($dl) }
                $curSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                foreach ($cl in $curLogins) { [void]$curSet.Add($cl) }

                # Delete removed Agent Logins
                foreach ($cl in $curLogins) {
                    if (-not $desiredSet.Contains($cl)) {
                        $nowCnt = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                        $delRowIdx = -1
                        for ($ri = 0; $ri -lt $nowCnt; $ri++) {
                            $txt = ([SceWin32]::GetListItemText32($Session.HProc, $lv24, $ri, 0)).Trim()
                            if ([string]::Equals($txt, $cl, [System.StringComparison]::OrdinalIgnoreCase)) { $delRowIdx = $ri; break }
                        }
                        if ($delRowIdx -ge 0) {
                            [void](Invoke-DeleteLoginRowInDialog $Session $pd $lv24 $delBtn $delRowIdx)
                            $removed.Add($cl)
                        }
                    }
                }

                # Add new Agent Logins
                foreach ($dl in $desiredLogins) {
                    if (-not $curSet.Contains($dl)) {
                        [ScePersonWin32]::PostMessage($addBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                        $ad = [IntPtr]::Zero
                        for ($w = 0; $w -lt 25; $w++) {
                            Start-Sleep -Milliseconds 80
                            $ad = Get-SceDialogs | Where-Object { $_ -ne $pd } | Select-Object -First 1
                            if ($ad) { break }
                        }
                        if (-not $ad -or $ad -eq [IntPtr]::Zero) { throw "Agent Info > New Agent Login Info 창이 열리지 않았습니다." }
                        Invoke-SelectAgentLoginInBrowse $Session $pd $ad $dl $agentLoginRel
                        $added.Add($dl)
                    }
                }

                if ($removed.Count -gt 0) { $loginDeltaLog += (" -Login[" + ($removed -join ",") + "]") }
                if ($added.Count -gt 0)   { $loginDeltaLog += (" +Login[" + ($added -join ",") + "]") }
            }

            Invoke-SavePersonDialog $Session $pd

            # Verify saved in folder ListView
            $vIdx = -1
            for ($vw = 0; $vw -lt 20; $vw++) {
                if ($vw -eq 2 -or $vw -eq 8) {
                    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
                }
                Start-Sleep -Milliseconds 100
                $vIdx = Find-CmListItemIndex $Session $newUn 0
                if ($vIdx -ge 0) { break }
            }
            if ($vIdx -lt 0) { throw "저장 후 폴더 목록에서 '$newUn'을 확인할 수 없습니다." }

            $updatedItems.Add([PSCustomObject]@{
                originalUserName = $origUn
                userName         = $newUn
                firstName        = $newFn
                lastName         = $newLn
                employeeId       = $newEid
                isAgent          = [bool]$wantAgent
                agentLogin       = ($desiredLogins -join ", ")
            })
            & $Log ("  [수정 완료] User='{0}' -> User='{1}', First='{2}', Last='{3}', EmpID='{4}'{5}" -f $origUn, $newUn, $newFn, $newLn, $newEid, $loginDeltaLog)
            $updDetail = if ($origUn -ne $newUn) { "이전User=$origUn" } else { "" }
            Write-CmAuditEvent -Action "UPDATE" -Target "CM" -ObjectType "Person" -Name $newUn -EmpId $newEid -FirstName $newFn -LastName $newLn -IsAgent ([string]([bool]$wantAgent)) -LinkedLogins @($added.ToArray()) -UnlinkedLogins @($removed.ToArray()) -Detail $updDetail -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath
        } catch {
            Close-AllSceModals
            $msg = $_.Exception.Message
            $failedList.Add([PSCustomObject]@{ originalUserName = $origUn; employeeId = $newEid; userName = $newUn; reason = $msg })
            & $Log ("  [실패] {0} ({1}) -> {2}" -f $origUn, $newEid, $msg)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $origUn -EmpId $newEid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Update" -Reason $msg
        }
    }

    $sw.Stop()
    & $Log ("[Person 수정 완료] 성공 {0}건 / 실패 {1}건 ({2:N1}초)" -f $updatedItems.Count, $failedList.Count, $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        customer       = [string]$CustomerObj.name
        folderPath     = $pResolved.DisplayPath
        elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        updatedItems   = $updatedItems.ToArray()
        failed         = $failedList.ToArray()
    }
}

function Invoke-CmDeletePersons {
    param($Session, $CustomerObj, [object[]]$Rows, [scriptblock]$Log = { param($m) [Console]::WriteLine($m) })
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Close-AllSceModals

    $pResolved = Resolve-PersonsFolderNode $Session $CustomerObj
    if ($null -eq $pResolved) {
        throw "CM Persons 폴더에서 '$($CustomerObj.name)' 고객사 폴더를 찾지 못했습니다."
    }
    & $Log ("[Person 삭제] {0} (대상 {1}건)" -f $pResolved.DisplayPath, $Rows.Count)

    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
    Start-Sleep -Milliseconds 120
    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 220

    $deletedItems = New-Object System.Collections.Generic.List[object]
    $skippedList = New-Object System.Collections.Generic.List[object]
    $failedList = New-Object System.Collections.Generic.List[object]

    foreach ($row in $Rows) {
        $origUn = ([string]$row.OriginalUserName).Trim()
        if (-not $origUn) { $origUn = ([string]$row.UserName).Trim() }
        $origEid = ([string]$row.OriginalEmployeeId).Trim()
        if (-not $origEid) { $origEid = ([string]$row.EmployeeId).Trim() }

        [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
        Start-Sleep -Milliseconds 100
        $idx = Find-PersonRowIndexInActiveFolder $Session $origUn $origEid
        if ($idx -lt 0) {
            [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 250
            $idx = Find-PersonRowIndexInActiveFolder $Session $origUn $origEid
        }
        if ($idx -lt 0) {
            $skippedList.Add([PSCustomObject]@{ originalUserName = $origUn; employeeId = $origEid; userName = $origUn; reason = "폴더 내 미존재" })
            & $Log ("  [건너뜀] '{0}' ({1}) -> 폴더 내 미존재" -f $origUn, $origEid)
            continue
        }

        $actualCmUser = ([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $idx, 0)).Trim()
        $isAgentRow = [string]::Equals(([SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $idx, 4)).Trim(), "True", [System.StringComparison]::OrdinalIgnoreCase)
        try {
            # Step 1: If Agent account, open Person Properties > Agent Info and Delete ID + Apply(OK) if any Agent Login exists
            $unlinked = New-Object System.Collections.Generic.List[string]
            $unlinkedSaved = $false
            if ($isAgentRow) {
                $pd = Open-PersonPropertiesDialog $Session $idx $actualCmUser 3500
                if ($pd -eq [IntPtr]::Zero) { throw "Person 속성 창을 열 수 없습니다: $actualCmUser" }

                $hasAgentTab = $true
                $genPage = Find-ChildCtrl $pd "#32770" 133 $false
                if ($genPage -ne [IntPtr]::Zero) {
                    $genCs = Get-CmDialogControls $genPage
                    if ($genCs.ContainsKey("Button/1040")) {
                        $chkAg = [int][ScePersonWin32]::SendMessage($genCs["Button/1040"], 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)
                        if ($chkAg -eq 0) { $hasAgentTab = $false }
                    }
                }

                if ($hasAgentTab) {
                    $agCtrls = Get-AgentInfoTabControls $pd
                    $lv24 = $agCtrls.ListHwnd
                    $delBtn = $agCtrls.DelBtn
                    $loginCnt = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)

                    if ($loginCnt -gt 0) {
                        while ($true) {
                            $curCnt = [int][ScePersonWin32]::SendMessage($lv24, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
                            if ($curCnt -le 0) { break }
                            $lid = ([SceWin32]::GetListItemText32($Session.HProc, $lv24, 0, 0)).Trim()
                            [void](Invoke-DeleteLoginRowInDialog $Session $pd $lv24 $delBtn 0)
                            if ($lid) { $unlinked.Add($lid) }
                        }
                        & $Log ("  [Login 연결 해제] '{0}' -> Agent Login [{1}] Delete ID 후 적용(OK)..." -f $actualCmUser, ($unlinked -join ", "))
                        Invoke-SavePersonDialog $Session $pd
                        $unlinkedSaved = $true
                        Wait-CmOperationComplete 5000 | Out-Null
                        [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
                        Start-Sleep -Milliseconds 180
                    } else {
                        [ScePersonWin32]::PostMessage($pd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                        for ($w = 0; $w -lt 30; $w++) {
                            if ((Get-SceDialogs).Count -eq 0) { break }
                            Start-Sleep -Milliseconds 30
                        }
                    }
                } else {
                    [ScePersonWin32]::PostMessage($pd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                    for ($w = 0; $w -lt 30; $w++) {
                        if ((Get-SceDialogs).Count -eq 0) { break }
                        Start-Sleep -Milliseconds 30
                    }
                }
            }

            # Step 2: Re-locate & strictly select ONLY this Person row, then delete via 8005
            [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $pResolved.Node) | Out-Null
            Start-Sleep -Milliseconds 100
            $delIdx = Find-CmListItemIndex $Session $actualCmUser 0
            if ($delIdx -lt 0) { throw "Agent Login 해제 후 목록에서 Person '$actualCmUser'을 다시 찾을 수 없습니다." }

            [SceWin32]::SelectListItem($Session.HProc, $Session.ListHwnd, $delIdx)
            $selCnt = [int][ScePersonWin32]::SendMessage($Session.ListHwnd, 0x1032, [IntPtr]::Zero, [IntPtr]::Zero)
            $selRow = [int]([ScePersonWin32]::SendMessage($Session.ListHwnd, 0x100C, [IntPtr](-1), [IntPtr]2).ToInt64())
            if ($selCnt -ne 1 -or $selRow -ne $delIdx) {
                throw "삭제 행 선택 검증 실패 (대상 인덱스: $delIdx, 실제 선택: $selRow / 개수: $selCnt)"
            }

            [SceWin32]::PostMessage($Session.ListHwnd, 0x0007, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 90
            [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]8005, [IntPtr]::Zero) | Out-Null # Delete

            $confDlg = Wait-CmDialogOpen 5000
            if ($confDlg -eq [IntPtr]::Zero) { throw "CM 삭제 확인 창이 열리지 않았습니다: $actualCmUser" }
            $dcs = Get-CmDialogControls $confDlg
            $yesBtn = $dcs["Button/1"]
            if (-not $yesBtn) { $yesBtn = $dcs["Button/6"] }
            if (-not $yesBtn) {
                [ScePersonWin32]::PostMessage($confDlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                throw "CM 삭제 확인(Yes) 버튼을 찾지 못했습니다."
            }
            [ScePersonWin32]::PostMessage($yesBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 180

            $done = Wait-CmOperationComplete 30000
            if (-not $done.Ok) { throw "CM Person 삭제 오류: $($done.Error)" }

            # Verify removed
            $stillThere = $true
            for ($vw = 0; $vw -lt 15; $vw++) {
                if ($vw -eq 3 -or $vw -eq 8) {
                    [ScePersonWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32851, [IntPtr]::Zero) | Out-Null
                }
                Start-Sleep -Milliseconds 100
                if ((Find-CmListItemIndex $Session $actualCmUser 0) -lt 0) {
                    $stillThere = $false
                    break
                }
            }
            if ($stillThere) { throw "삭제 명령 후에도 목록에 '$actualCmUser'이 남아 있습니다." }

            $deletedItems.Add([PSCustomObject]@{
                originalUserName = $origUn
                userName         = $actualCmUser
                employeeId       = $origEid
                unlinkedLogins   = @($unlinked.ToArray())
            })
            $unlinkNote = if ($unlinked.Count -gt 0) { " (Login 연결 해제: $($unlinked -join ', '))" } else { "" }
            & $Log ("  [삭제 완료] User='{0}', EmpID='{1}'{2}" -f $actualCmUser, $origEid, $unlinkNote)
            Write-CmAuditEvent -Action "DELETE" -Target "CM" -ObjectType "Person" -Name $actualCmUser -EmpId $origEid -UnlinkedLogins @($unlinked.ToArray()) -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath
        } catch {
            Close-AllSceModals
            $msg = $_.Exception.Message
            if ($unlinkedSaved -and $unlinked.Count -gt 0) {
                Write-CmAuditEvent -Action "UNLINK" -Target "CM" -ObjectType "Person" -Name $actualCmUser -EmpId $origEid -UnlinkedLogins @($unlinked.ToArray()) -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath
            }
            $failedList.Add([PSCustomObject]@{ originalUserName = $origUn; employeeId = $origEid; userName = $actualCmUser; reason = $msg })
            & $Log ("  [실패] {0} ({1}) -> {2}" -f $actualCmUser, $origEid, $msg)
            Write-CmAuditEvent -Action "ERROR" -Target "CM" -ObjectType "Person" -Name $actualCmUser -EmpId $origEid -Customer ([string]$CustomerObj.name) -Path $pResolved.DisplayPath -Operation "Delete" -Reason $msg
        }
    }

    $sw.Stop()
    & $Log ("[Person 삭제 완료] 삭제 {0}건 / 건너뜀 {1}건 / 실패 {2}건 ({3:N1}초)" -f $deletedItems.Count, $skippedList.Count, $failedList.Count, $sw.Elapsed.TotalSeconds)
    return [PSCustomObject]@{
        customer       = [string]$CustomerObj.name
        folderPath     = $pResolved.DisplayPath
        elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        deletedItems   = $deletedItems.ToArray()
        skipped        = $skippedList.ToArray()
        failed         = $failedList.ToArray()
    }
}

if ($Action -ne "Library") {
    $passPlain = if ($env:CM_PASS_TEMP) { $env:CM_PASS_TEMP } else { "password" }
    $env:CM_PASS_TEMP = $null

    $session = $null
    try {
        $payloadRows = @()
        $payloadPath = $env:CM_PERSON_PAYLOAD_PATH
        if ($Action -in @("CheckPersons", "CreatePersons", "UpdatePersons", "DeletePersons")) {
            if (-not $payloadPath -or -not (Test-Path $payloadPath)) {
                throw "Person 페이로드 파일을 찾을 수 없습니다."
            }
            $rawJson = Get-Content -Path $payloadPath -Raw -Encoding UTF8
            $parsed = $rawJson | ConvertFrom-Json
            if ($parsed) { $payloadRows = @($parsed) }
            if ($payloadRows.Count -eq 0) { throw "처리할 Person 대상 목록이 비어 있습니다." }
        }

        Add-Type -AssemblyName System.Windows.Forms
        $session = Connect-CmSession -ExePath $CmExePath -UserName $CmUser -Password $passPlain

        $customers = Load-CmCustomers $CustomersJsonPath
        $custObj = $null
        if ($Customer) {
            $custObj = $customers | Where-Object { $_.name -eq $Customer } | Select-Object -First 1
            if (-not $custObj) { throw "고객사 '$Customer'를 cm_customers.json에서 찾을 수 없습니다." }
        }

        switch ($Action) {
            "FetchAvailableLogins" {
                if (-not $custObj) { throw "고객사를 선택하세요." }
                $res = Invoke-CmFetchAvailableLogins $session $custObj
                [Console]::WriteLine("##PERSON_LOGINS_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
            "CheckPersons" {
                $res = Invoke-CmCheckPersons $session $payloadRows
                [Console]::WriteLine("##PERSON_CHECK_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
            "CreatePersons" {
                if (-not $custObj) { throw "고객사를 선택하세요." }
                $res = Invoke-CmCreatePersons $session $custObj $payloadRows -CreateGeneral $DoGeneral -ApplyAgentLogin $DoAgentLogin
                [Console]::WriteLine("##PERSON_CREATE_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
            "FetchPersonsInFolder" {
                if (-not $custObj) { throw "고객사를 선택하세요." }
                $res = Invoke-CmFetchPersonsInFolder $session $custObj
                [Console]::WriteLine("##PERSON_LIST_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
            "UpdatePersons" {
                if (-not $custObj) { throw "고객사를 선택하세요." }
                $res = Invoke-CmUpdatePersons $session $custObj $payloadRows
                [Console]::WriteLine("##PERSON_UPDATE_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
            "DeletePersons" {
                if (-not $custObj) { throw "고객사를 선택하세요." }
                $res = Invoke-CmDeletePersons $session $custObj $payloadRows
                [Console]::WriteLine("##PERSON_DELETE_JSON##" + ($res | ConvertTo-Json -Compress -Depth 6))
            }
        }
    } catch {
        Write-CmAuditEvent -Action "ERROR" -Target "CM" -Operation ("Person:" + $Action) -Customer $Customer -Reason $_.Exception.Message
        throw
    } finally {
        if ($session) { Disconnect-CmSession $session }
    }
}
