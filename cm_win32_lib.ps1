# Genesys Configuration Manager (sce.exe) Win32 Automation Library
# Designed for STA PowerShell 5.1 host (or non-STA fallback without throwing on Clipboard check)

if (-not ("SceWin32" -as [type])) {
    Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class SceWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hwndCtl);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
    [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);

    public static string GetText(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, 512);
        return sb.ToString();
    }

    public static string GetTextWM(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(512);
        SendMessage(hWnd, 0x000D, (IntPtr)512, sb);
        return sb.ToString();
    }

    public static string GetClass(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(hWnd, sb, 256);
        return sb.ToString();
    }

    public static string GetTreeItemText32(IntPtr hProcess, IntPtr hTree, IntPtr hItem) {
        IntPtr mem = VirtualAllocEx(hProcess, IntPtr.Zero, 1024, 0x1000 | 0x2000, 0x04);
        if (mem == IntPtr.Zero) return "";
        try {
            IntPtr textBuf = IntPtr.Add(mem, 64);
            byte[] tvItem = new byte[40];
            BitConverter.GetBytes((uint)0x0011).CopyTo(tvItem, 0);
            BitConverter.GetBytes((uint)hItem.ToInt64()).CopyTo(tvItem, 4);
            BitConverter.GetBytes((uint)textBuf.ToInt64()).CopyTo(tvItem, 16);
            BitConverter.GetBytes((int)256).CopyTo(tvItem, 20);
            IntPtr written;
            WriteProcessMemory(hProcess, mem, tvItem, 40, out written);
            SendMessage(hTree, 0x113E, IntPtr.Zero, mem);
            byte[] strBytes = new byte[512];
            IntPtr read;
            ReadProcessMemory(hProcess, textBuf, strBytes, 512, out read);
            string s = Encoding.Unicode.GetString(strBytes);
            int nullIdx = s.IndexOf('\0');
            return nullIdx >= 0 ? s.Substring(0, nullIdx) : s;
        } finally {
            VirtualFreeEx(hProcess, mem, 0, 0x8000);
        }
    }

    public static string GetListItemText32(IntPtr hProcess, IntPtr hList, int itemIndex, int subItemIndex) {
        IntPtr mem = VirtualAllocEx(hProcess, IntPtr.Zero, 1024, 0x1000 | 0x2000, 0x04);
        if (mem == IntPtr.Zero) return "";
        try {
            IntPtr textBuf = IntPtr.Add(mem, 128);
            byte[] lvItem = new byte[64];
            BitConverter.GetBytes((uint)0x0001).CopyTo(lvItem, 0);
            BitConverter.GetBytes(itemIndex).CopyTo(lvItem, 4);
            BitConverter.GetBytes(subItemIndex).CopyTo(lvItem, 8);
            BitConverter.GetBytes((uint)textBuf.ToInt64()).CopyTo(lvItem, 20);
            BitConverter.GetBytes((int)256).CopyTo(lvItem, 24);
            IntPtr written;
            WriteProcessMemory(hProcess, mem, lvItem, 64, out written);
            SendMessage(hList, 0x1073, (IntPtr)itemIndex, mem);
            byte[] strBytes = new byte[512];
            IntPtr read;
            ReadProcessMemory(hProcess, textBuf, strBytes, 512, out read);
            string s = Encoding.Unicode.GetString(strBytes);
            int nullIdx = s.IndexOf('\0');
            return nullIdx >= 0 ? s.Substring(0, nullIdx) : s;
        } finally {
            VirtualFreeEx(hProcess, mem, 0, 0x8000);
        }
    }

    public static void SelectListItem(IntPtr hProcess, IntPtr hList, int idx) {
        IntPtr mem = VirtualAllocEx(hProcess, IntPtr.Zero, 256, 0x1000 | 0x2000, 0x04);
        if (mem == IntPtr.Zero) return;
        try {
            byte[] lvClear = new byte[64];
            BitConverter.GetBytes((uint)0x0008).CopyTo(lvClear, 0);
            BitConverter.GetBytes(-1).CopyTo(lvClear, 4);
            BitConverter.GetBytes((uint)0x0000).CopyTo(lvClear, 12);
            BitConverter.GetBytes((uint)0x0003).CopyTo(lvClear, 16);
            IntPtr written;
            WriteProcessMemory(hProcess, mem, lvClear, 64, out written);
            SendMessage(hList, 0x102B, (IntPtr)(-1), mem);
            if (idx >= 0) {
                byte[] lvItem = new byte[64];
                BitConverter.GetBytes((uint)0x0008).CopyTo(lvItem, 0);
                BitConverter.GetBytes(idx).CopyTo(lvItem, 4);
                BitConverter.GetBytes((uint)0x0003).CopyTo(lvItem, 12);
                BitConverter.GetBytes((uint)0x0003).CopyTo(lvItem, 16);
                WriteProcessMemory(hProcess, mem, lvItem, 64, out written);
                SendMessage(hList, 0x102B, (IntPtr)idx, mem);
                SendMessage(hList, 0x1013, (IntPtr)idx, IntPtr.Zero);
            }
        } finally {
            VirtualFreeEx(hProcess, mem, 0, 0x8000);
        }
    }

    public static void SelectListItems(IntPtr hProcess, IntPtr hList, int[] indices) {
        IntPtr mem = VirtualAllocEx(hProcess, IntPtr.Zero, 256, 0x1000 | 0x2000, 0x04);
        if (mem == IntPtr.Zero) return;
        try {
            byte[] lvClear = new byte[64];
            BitConverter.GetBytes((uint)0x0008).CopyTo(lvClear, 0);
            BitConverter.GetBytes(-1).CopyTo(lvClear, 4);
            BitConverter.GetBytes((uint)0x0000).CopyTo(lvClear, 12);
            BitConverter.GetBytes((uint)0x0003).CopyTo(lvClear, 16);
            IntPtr written;
            WriteProcessMemory(hProcess, mem, lvClear, 64, out written);
            SendMessage(hList, 0x102B, (IntPtr)(-1), mem);

            if (indices != null) {
                for (int k = 0; k < indices.Length; k++) {
                    int idx = indices[k];
                    if (idx < 0) continue;
                    byte[] lvItem = new byte[64];
                    BitConverter.GetBytes((uint)0x0008).CopyTo(lvItem, 0);
                    BitConverter.GetBytes(idx).CopyTo(lvItem, 4);
                    uint st = (k == 0) ? (uint)0x0003 : (uint)0x0002;
                    BitConverter.GetBytes((uint)0x0003).CopyTo(lvItem, 12);
                    BitConverter.GetBytes(st).CopyTo(lvItem, 16);
                    WriteProcessMemory(hProcess, mem, lvItem, 64, out written);
                    SendMessage(hList, 0x102B, (IntPtr)idx, mem);
                }
                if (indices.Length > 0 && indices[0] >= 0) {
                    SendMessage(hList, 0x1013, (IntPtr)indices[0], IntPtr.Zero);
                }
            }
        } finally {
            VirtualFreeEx(hProcess, mem, 0, 0x8000);
        }
    }

    public static void SelectComboIndex(IntPtr hCombo, int idx) {
        SendMessage(hCombo, 0x014E, (IntPtr)idx, IntPtr.Zero);
        IntPtr parent = GetParent(hCombo);
        int cid = GetDlgCtrlID(hCombo);
        SendMessage(parent, 0x0111, (IntPtr)((1 << 16) | (cid & 0xFFFF)), hCombo);
    }

    public static void SetEditText(IntPtr hEdit, string val) {
        SendMessage(hEdit, 0x000C, IntPtr.Zero, val);
        IntPtr parent = GetParent(hEdit);
        int cid = GetDlgCtrlID(hEdit);
        SendMessage(parent, 0x0111, (IntPtr)((0x0300 << 16) | (cid & 0xFFFF)), hEdit);
    }
}
"@
}

function Get-SceDialogs {
    $procs = Get-Process -Name "sce" -ErrorAction SilentlyContinue
    if (-not $procs) { return @() }
    $pids = $procs | Select-Object -ExpandProperty Id
    $script:dlgs = New-Object System.Collections.Generic.List[IntPtr]
    [SceWin32]::EnumWindows([SceWin32+EnumWindowsProc]{
        param($hWnd, $lParam)
        $wpid = 0
        [SceWin32]::GetWindowThreadProcessId($hWnd, [ref]$wpid) | Out-Null
        if ($pids -contains $wpid -and [SceWin32]::IsWindowVisible($hWnd) -and [SceWin32]::GetClass($hWnd) -eq "#32770") {
            $script:dlgs.Add($hWnd)
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:dlgs
}

function Get-SceMainWindow {
    $procs = Get-Process -Name "sce" -ErrorAction SilentlyContinue
    if (-not $procs) { return [IntPtr]::Zero }
    $pids = $procs | Select-Object -ExpandProperty Id
    $script:mh = [IntPtr]::Zero
    [SceWin32]::EnumWindows([SceWin32+EnumWindowsProc]{
        param($hWnd, $lParam)
        $wpid = 0
        [SceWin32]::GetWindowThreadProcessId($hWnd, [ref]$wpid) | Out-Null
        if ($pids -contains $wpid -and [SceWin32]::IsWindowVisible($hWnd) -and ([SceWin32]::GetText($hWnd) -like "Configuration Manager -*")) {
            $script:mh = $hWnd
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:mh
}

function Connect-CmSession {
    param(
        [string]$ExePath = "C:\GCTI\Configuration Manager\sce.exe",
        [string]$UserName = "",
        [string]$Password = "password"
    )
    if ([string]::IsNullOrWhiteSpace($Password)) { $Password = "password" }
    $mainHwnd = Get-SceMainWindow
    if ($mainHwnd -eq [IntPtr]::Zero) {
        $existingDlgs = Get-SceDialogs
        if ($existingDlgs.Count -eq 0) {
            if (-not (Test-Path $ExePath)) { throw "sce.exe를 찾을 수 없습니다: $ExePath" }
            Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path -Parent $ExePath) | Out-Null
        }
        for ($wait = 0; $wait -lt 30; $wait++) {
            $mainHwnd = Get-SceMainWindow
            if ($mainHwnd -ne [IntPtr]::Zero) { break }
            $script:pwHwnd = [IntPtr]::Zero
            $script:okHwnd = [IntPtr]::Zero
            $script:editCtrls = New-Object System.Collections.Generic.List[object]
            foreach ($d in (Get-SceDialogs)) {
                [SceWin32]::EnumChildWindows($d, [SceWin32+EnumWindowsProc]{
                    param($c, $l)
                    $cls = [SceWin32]::GetClass($c)
                    $st = [SceWin32]::GetWindowLong($c, -16)
                    $cid = [SceWin32]::GetDlgCtrlID($c)
                    if ($cls -eq "Edit" -and [SceWin32]::IsWindowVisible($c)) {
                        $rc = New-Object SceWin32+RECT
                        [SceWin32]::GetWindowRect($c, [ref]$rc) | Out-Null
                        $isPw = (($st -band 0x0020) -ne 0)
                        if ($isPw) { $script:pwHwnd = $c }
                        $script:editCtrls.Add([PSCustomObject]@{ Hwnd = $c; Top = $rc.Top; IsPw = $isPw })
                    }
                    if ($cls -eq "Button" -and $cid -eq 1) { $script:okHwnd = $c }
                    return $true
                }, [IntPtr]::Zero) | Out-Null
            }
            if ($script:pwHwnd -ne [IntPtr]::Zero) {
                if (-not [string]::IsNullOrWhiteSpace($UserName)) {
                    $pwEntry = $script:editCtrls | Where-Object { $_.Hwnd -eq $script:pwHwnd } | Select-Object -First 1
                    if ($pwEntry) {
                        $userEdit = $script:editCtrls | Where-Object { (-not $_.IsPw) -and $_.Top -lt $pwEntry.Top } | Sort-Object Top -Descending | Select-Object -First 1
                        if ($userEdit) {
                            [SceWin32]::SendMessage($userEdit.Hwnd, 0x000C, [IntPtr]::Zero, $UserName) | Out-Null
                            Start-Sleep -Milliseconds 100
                        }
                    }
                }
                [SceWin32]::SendMessage($script:pwHwnd, 0x000C, [IntPtr]::Zero, $Password) | Out-Null
                Start-Sleep -Milliseconds 200
                if ($script:okHwnd -ne [IntPtr]::Zero) {
                    [SceWin32]::PostMessage($script:okHwnd, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                }
                Start-Sleep -Seconds 3
            } else {
                Start-Sleep -Milliseconds 500
            }
        }
    }
    if ($mainHwnd -eq [IntPtr]::Zero) { throw "Configuration Manager 로그인 또는 창 연결에 실패했습니다." }

    $script:tHwnd = [IntPtr]::Zero
    $script:lHwnd = [IntPtr]::Zero
    [SceWin32]::EnumChildWindows($mainHwnd, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        $cls = [SceWin32]::GetClass($c)
        if ($cls -eq "SysTreeView32" -and [SceWin32]::IsWindowVisible($c)) { $script:tHwnd = $c }
        if ($cls -eq "SysListView32" -and [SceWin32]::IsWindowVisible($c)) { $script:lHwnd = $c }
        return $true
    }, [IntPtr]::Zero) | Out-Null

    if ($script:tHwnd -eq [IntPtr]::Zero -or $script:lHwnd -eq [IntPtr]::Zero) {
        throw "Configuration Manager 트리/리스트 컨트롤을 찾지 못했습니다."
    }

    $scePid = 0
    [SceWin32]::GetWindowThreadProcessId($mainHwnd, [ref]$scePid) | Out-Null
    $hProc = [SceWin32]::OpenProcess(0x0438, $false, $scePid)
    if ($hProc -eq [IntPtr]::Zero) { throw "sce.exe 프로세스 핸들을 열 수 없습니다." }

    return [PSCustomObject]@{
        MainHwnd = $mainHwnd
        TreeHwnd = $script:tHwnd
        ListHwnd = $script:lHwnd
        HProc    = $hProc
        Pid      = $scePid
    }
}

function Disconnect-CmSession {
    param($Session)
    if ($Session -and $Session.HProc -ne [IntPtr]::Zero) {
        [SceWin32]::CloseHandle($Session.HProc) | Out-Null
        $Session.HProc = [IntPtr]::Zero
    }
}

function Get-CmDialogControls {
    param([IntPtr]$DlgHwnd)
    $script:map = @{}
    [SceWin32]::EnumChildWindows($DlgHwnd, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        if ([SceWin32]::IsWindowVisible($c)) {
            $cls = [SceWin32]::GetClass($c)
            $cid = [SceWin32]::GetDlgCtrlID($c)
            $script:map["$cls/$cid"] = $c
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:map
}

function Get-CmDialogStaticText {
    param([IntPtr]$DlgHwnd)
    $script:msgs = New-Object System.Collections.Generic.List[string]
    [SceWin32]::EnumChildWindows($DlgHwnd, [SceWin32+EnumWindowsProc]{
        param($c, $l)
        if ([SceWin32]::IsWindowVisible($c) -and [SceWin32]::GetClass($c) -eq "Static") {
            $t = ([SceWin32]::GetTextWM($c)).Trim()
            if ($t) { $script:msgs.Add($t) }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return ($script:msgs -join " ")
}

function Wait-CmDialogOpen {
    param([int]$TimeoutMs = 6000)
    $steps = [Math]::Ceiling($TimeoutMs / 100)
    for ($i = 0; $i -lt $steps; $i++) {
        $d = Get-SceDialogs | Select-Object -First 1
        if ($d) { return $d }
        Start-Sleep -Milliseconds 100
    }
    return [IntPtr]::Zero
}

function Wait-CmOperationComplete {
    param([int]$TimeoutMs = 15000)
    $steps = [Math]::Ceiling($TimeoutMs / 150)
    for ($i = 0; $i -lt $steps; $i++) {
        $dlgs = Get-SceDialogs
        if ($dlgs.Count -eq 0) {
            return [PSCustomObject]@{ Ok = $true; Error = $null }
        }
        foreach ($d in $dlgs) {
            $title = [SceWin32]::GetText($d)
            if ($title -like "Deleting Objects*") { continue }
            $msg = Get-CmDialogStaticText $d
            if ($title -match "Error|Warning|Configuration Manager|Confirm|Message" -or $msg -match "already exists|Cannot|failed|error|오류") {
                # Dismiss modal alert cleanly via OK/Yes/Cancel
                $ctrls = Get-CmDialogControls $d
                $dismissBtn = $ctrls["Button/1"]
                if (-not $dismissBtn) { $dismissBtn = $ctrls["Button/2"] }
                if ($dismissBtn) { [SceWin32]::PostMessage($dismissBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                Start-Sleep -Milliseconds 300
                return [PSCustomObject]@{ Ok = $false; Error = "[$title] $msg" }
            }
        }
        Start-Sleep -Milliseconds 150
    }
    return [PSCustomObject]@{ Ok = $false; Error = "CM 작업 응답 대기 시간 초과" }
}

function Find-CmTreeChild {
    param($Session, [IntPtr]$ParentItem, [string]$ExactName)
    if ($ParentItem -eq [IntPtr]::Zero) {
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]::Zero, [IntPtr]::Zero)
    } else {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x1102, [IntPtr]2, $ParentItem) | Out-Null
        Start-Sleep -Milliseconds 140
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]4, $ParentItem)
    }
    while ($cur -ne [IntPtr]::Zero) {
        $txt = [SceWin32]::GetTreeItemText32($Session.HProc, $Session.TreeHwnd, $cur)
        if ($txt -eq $ExactName) { return $cur }
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]1, $cur)
    }
    return [IntPtr]::Zero
}

function Select-CmTreePath {
    param($Session, [string[]]$PathParts)
    $cur = [IntPtr]::Zero
    foreach ($part in $PathParts) {
        $cur = Find-CmTreeChild $Session $cur $part
        if ($cur -eq [IntPtr]::Zero) { return [IntPtr]::Zero }
    }
    [SceWin32]::SendMessage($Session.TreeHwnd, 0x110B, [IntPtr]9, $cur) | Out-Null
    Start-Sleep -Milliseconds 380
    return $cur
}

function Find-CmListItemIndex {
    param($Session, [string]$ExactName, [int]$Column = 0)
    $cnt = [int][SceWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
    for ($i = 0; $i -lt $cnt; $i++) {
        $txt = [SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, $Column)
        if ($txt -eq $ExactName) { return $i }
    }
    return -1
}

function Get-CmListItemIndexMap {
    param($Session, [int]$Column = 0)
    $map = @{}
    $cnt = [int][SceWin32]::SendMessage($Session.ListHwnd, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
    for ($i = 0; $i -lt $cnt; $i++) {
        $txt = [SceWin32]::GetListItemText32($Session.HProc, $Session.ListHwnd, $i, $Column)
        if ($txt -and -not $map.ContainsKey($txt)) {
            $map[$txt] = $i
        }
    }
    return $map
}

function Get-CmTreeChildMap {
    param($Session, [IntPtr]$ParentItem, [switch]$Refresh)
    if ($Refresh -and $ParentItem -ne [IntPtr]::Zero) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x1102, [IntPtr]1, $ParentItem) | Out-Null # TVE_COLLAPSE
        Start-Sleep -Milliseconds 100
    }
    if ($ParentItem -ne [IntPtr]::Zero) {
        [SceWin32]::SendMessage($Session.TreeHwnd, 0x1102, [IntPtr]2, $ParentItem) | Out-Null # TVE_EXPAND
        Start-Sleep -Milliseconds 140
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]4, $ParentItem)
    } else {
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    $map = @{}
    while ($cur -ne [IntPtr]::Zero) {
        $txt = [SceWin32]::GetTreeItemText32($Session.HProc, $Session.TreeHwnd, $cur)
        if ($txt -and -not $map.ContainsKey($txt)) {
            $map[$txt] = $cur
        }
        $cur = [SceWin32]::SendMessage($Session.TreeHwnd, 0x110A, [IntPtr]1, $cur)
    }
    return $map
}

function Invoke-CmGlobalFind {
    <#
    .SYNOPSIS
    Uses CM Find Configuration Objects (ID 32831) to check existence globally.
    TypeIndex: 3 = Agent Login, 14 = DN, 27 = Place
    #>
    param($Session, [array]$Queries)

    [SceWin32]::PostMessage($Session.MainHwnd, 0x0111, [IntPtr]32831, [IntPtr]::Zero) | Out-Null
    $findDlg = Wait-CmDialogOpen 5000
    if ($findDlg -eq [IntPtr]::Zero) { throw "Find 다이얼로그를 열 수 없습니다." }

    $ctrls = Get-CmDialogControls $findDlg
    $cbType = $ctrls["ComboBox/7103"]
    $edName = $ctrls["Edit/1001"]
    $btnFind = $ctrls["Button/7001"]
    $lvRes = $ctrls["SysListView32/2"]

    $results = @()
    try {
        foreach ($q in $Queries) {
            # Ensure Find Now button is ready before starting next search
            for ($w = 0; $w -lt 100; $w++) {
                if ([SceWin32]::IsWindowEnabled($btnFind)) { break }
                Start-Sleep -Milliseconds 40
            }
            [SceWin32]::SelectComboIndex($cbType, [int]$q.TypeIndex)
            [SceWin32]::SendMessage($edName, 0x000C, [IntPtr]::Zero, [string]$q.Name) | Out-Null
            [SceWin32]::PostMessage($btnFind, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

            # Wait briefly for Find Now to disable (search started), then wait until re-enabled (search finished)
            for ($s = 0; $s -lt 8; $s++) {
                Start-Sleep -Milliseconds 35
                if (-not [SceWin32]::IsWindowEnabled($btnFind)) { break }
            }
            for ($w = 0; $w -lt 250; $w++) {
                if ([SceWin32]::IsWindowEnabled($btnFind)) { break }
                Start-Sleep -Milliseconds 40
            }
            Start-Sleep -Milliseconds 30

            $hHdr = [SceWin32]::SendMessage($lvRes, 0x101F, [IntPtr]::Zero, [IntPtr]::Zero)
            $colCnt = if ($hHdr -ne [IntPtr]::Zero) { [int][SceWin32]::SendMessage($hHdr, 0x1200, [IntPtr]::Zero, [IntPtr]::Zero) } else { 0 }
            if ($colCnt -le 0) { $colCnt = 6 }

            $rc = [int][SceWin32]::SendMessage($lvRes, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
            $matches = @()
            $rows = New-Object System.Collections.Generic.List[object]
            for ($i = 0; $i -lt $rc; $i++) {
                $rowCols = @()
                $rawCols = New-Object string[] $colCnt
                for ($col = 0; $col -lt $colCnt; $col++) {
                    $cv = ([SceWin32]::GetListItemText32($Session.HProc, $lvRes, $i, $col)).Trim()
                    $rawCols[$col] = $cv
                    if ($cv -ne "") { $rowCols += $cv }
                }
                $matches += ($rowCols -join "|")
                $rows.Add([PSCustomObject]@{ Cols = $rawCols })
            }
            $results += [PSCustomObject]@{
                Category = $q.Category
                Name     = $q.Name
                Count    = $rc
                Matches  = $matches
                Rows     = $rows.ToArray()
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
    return $results
}

function Group-ContiguousNumbers {
    param([string[]]$Numbers)
    $clean = $Numbers | Where-Object { $_ -and $_.Trim() -ne "" } | ForEach-Object { $_.Trim() } | Select-Object -Unique
    if (-not $clean -or $clean.Count -eq 0) { return @() }

    $parsed = $clean | ForEach-Object {
        [PSCustomObject]@{ Raw = $_; Num = [int64]$_; Digits = $_.Length }
    } | Sort-Object Num, Digits

    $groups = @()
    $curStart = $parsed[0]
    $curCount = 1
    $prevNum = $parsed[0].Num
    for ($i = 1; $i -lt $parsed.Count; $i++) {
        $item = $parsed[$i]
        if ($item.Num -eq ($prevNum + 1) -and $item.Digits -eq $curStart.Digits) {
            $curCount++
        } else {
            $groups += [PSCustomObject]@{ Start = $curStart.Raw; Count = $curCount; Digits = $curStart.Digits }
            $curStart = $item
            $curCount = 1
        }
        $prevNum = $item.Num
    }
    $groups += [PSCustomObject]@{ Start = $curStart.Raw; Count = $curCount; Digits = $curStart.Digits }
    return $groups
}

function Write-CmAuditEvent {
    param(
        [string]$Action,
        [string]$Target = "CM",
        [string]$ObjectType = "",
        [string]$Name = "",
        [string]$Customer = "",
        [string]$Path = "",
        [string]$Detail = "",
        [string]$EmpId = "",
        [string]$FirstName = "",
        [string]$LastName = "",
        [string]$IsAgent = "",
        [string[]]$LinkedLogins = @(),
        [string[]]$UnlinkedLogins = @(),
        [string]$Operation = "",
        [string]$Reason = ""
    )
    $evt = [ordered]@{
        action         = $Action
        target         = $Target
        objectType     = $ObjectType
        name           = $Name
        customer       = $Customer
        path           = $Path
        detail         = $Detail
        empId          = $EmpId
        firstName      = $FirstName
        lastName       = $LastName
        isAgent        = $IsAgent
        linkedLogins   = @($LinkedLogins | Where-Object { $_ })
        unlinkedLogins = @($UnlinkedLogins | Where-Object { $_ })
        operation      = $Operation
        reason         = $Reason
    }
    [Console]::WriteLine("##AUDIT_EVENT##" + ($evt | ConvertTo-Json -Compress -Depth 4))
}
