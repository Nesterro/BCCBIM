$asm = [System.Reflection.Assembly]::LoadFrom('C:\Program Files\Autodesk\Revit 2023\AdWindows.dll')
$tControl = $asm.GetType('Autodesk.Windows.RibbonControl')
Write-Host "=== RibbonControl Properties ==="
$tControl.GetProperties() | ForEach-Object {
    Write-Host "$($_.PropertyType.Name) $($_.Name)"
}
