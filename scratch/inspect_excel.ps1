$excel = New-Object -ComObject Excel.Application
try {
    $excel.Visible = $false
    $f = (Get-ChildItem -Path "d:\Yandex.Disk\BCC\BCC PlugIn" -Filter "*.xls" | Select-Object -First 1).FullName
    Write-Host "Opening: $f"
    $wb = $excel.Workbooks.Open($f)
    $ws = $wb.Sheets.Item(1)
    Write-Host "Sheet: $($ws.Name)"
    for ($r = 1; $r -le 15; $r++) {
        $rowVals = @()
        for ($c = 1; $c -le 18; $c++) {
            $formula = $ws.Cells.Item($r, $c).Formula
            $txt = $ws.Cells.Item($r, $c).Text
            if ($formula -and $formula.ToString().StartsWith("=")) {
                $rowVals += "[$formula]"
            } else {
                $rowVals += $txt
            }
        }
        Write-Host "Row $r : $($rowVals -join ' | ')"
    }
    $wb.Close($false)
} finally {
    $excel.Quit()
}
