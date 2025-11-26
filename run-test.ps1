# ============================================================
# O Language Compiler - Test Runner for Windows
# Автоматический запуск всех тестов и сохранение результатов
# ============================================================

param(
    [string]$TestPattern = "*.o",
    [switch]$Verbose,
    [switch]$StopOnError,
    [switch]$OnlyValid,
    [switch]$OnlyInvalid
)

# Цвета для вывода
$ErrorColor = "Red"
$SuccessColor = "Green"
$InfoColor = "Cyan"
$WarningColor = "Yellow"

# Настройка путей
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = $ScriptDir
$TestsDir = Join-Path $ProjectDir "tests"
$ResultsDir = Join-Path $ProjectDir "test-results"
$CompilerProject = Join-Path $ProjectDir "src\OCompiler\OCompiler.csproj"
$BuildOutput = Join-Path $ProjectDir "src\OCompiler\bin\Debug\net9.0\OCompiler.exe"

# Создаём папку для результатов, если её нет
if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir | Out-Null
    Write-Host "OK Created results directory: $ResultsDir" -ForegroundColor $SuccessColor
}

# Очищаем старые результаты
Write-Host "`n[INFO] Cleaning old test results..." -ForegroundColor $InfoColor
Get-ChildItem -Path $ResultsDir -Filter "*.txt" | Remove-Item -Force
Write-Host "OK Old results cleaned" -ForegroundColor $SuccessColor

# Проверяем, существует ли папка с тестами
if (-not (Test-Path $TestsDir)) {
    Write-Host "[ERROR] Tests directory not found: $TestsDir" -ForegroundColor $ErrorColor
    exit 1
}

# Функция для определения типа теста (валидный/невалидный)
function Get-TestType {
    param([string]$FileName)
    
    $TestNumber = [int]($FileName -replace '\D', '')
    
    # Тесты 01-10, 19-28 - валидные
    # Тесты 11-18 - невалидные (ожидаем ошибки)
    if (($TestNumber -ge 1 -and $TestNumber -le 10) -or ($TestNumber -ge 19 -and $TestNumber -le 28)) {
        return "Valid"
    } else {
        return "Invalid"
    }
}

# Функция для форматирования времени выполнения
function Format-Duration {
    param([TimeSpan]$Duration)
    
    if ($Duration.TotalSeconds -lt 1) {
        return "{0:N0}ms" -f $Duration.TotalMilliseconds
    } else {
        return "{0:N2}s" -f $Duration.TotalSeconds
    }
}

# Функция запуска одного теста
function Run-Test {
    param(
        [string]$TestFile,
        [string]$TestName,
        [string]$ResultFile
    )
    
    $TestType = Get-TestType $TestName
    $StartTime = Get-Date
    
    Write-Host "`n========================================" -ForegroundColor $InfoColor
    Write-Host "Running: $TestName ($TestType)" -ForegroundColor $InfoColor
    Write-Host "========================================" -ForegroundColor $InfoColor
    
    # Запускаем компилятор и перехватываем вывод
    $Output = ""
    $ExitCode = 0
    
    try {
        # Используем Start-Process для перехвата вывода
        $ProcessInfo = New-Object System.Diagnostics.ProcessStartInfo
        $ProcessInfo.FileName = $BuildOutput
        $ProcessInfo.Arguments = "`"$TestFile`""
        $ProcessInfo.RedirectStandardOutput = $true
        $ProcessInfo.RedirectStandardError = $true
        $ProcessInfo.UseShellExecute = $false
        $ProcessInfo.CreateNoWindow = $true
        
        $Process = New-Object System.Diagnostics.Process
        $Process.StartInfo = $ProcessInfo
        
        $Process.Start() | Out-Null
        
        $stdout = $Process.StandardOutput.ReadToEnd()
        $stderr = $Process.StandardError.ReadToEnd()
        
        $Process.WaitForExit()
        $ExitCode = $Process.ExitCode
        
        $Output = $stdout
        if ($stderr) {
            $Output += "`n`nSTDERR:`n$stderr"
        }
    }
    catch {
        $Output = "EXCEPTION: $($_.Exception.Message)"
        $ExitCode = -1
    }
    
    $EndTime = Get-Date
    $Duration = $EndTime - $StartTime
    
    # Определяем успешность теста
    $Success = $false
    
    if ($TestType -eq "Valid") {
        # Для валидных тестов успех = выход с кодом 0 и наличие "OK" в выводе
        $Success = ($ExitCode -eq 0) -and ($Output -match "\*\*\[\s*OK\s*\].*completed successfully")
    } else {
        # Для невалидных тестов успех = выход с кодом 1 и наличие ошибки
        $Success = ($ExitCode -ne 0) -and ($Output -match "\*\*\[\s*ERR\s*\]")
    }
    
    # Формируем результат для файла
    $ResultContent = @"
========================================
O Language Compiler - Test Result
========================================
Test File:    $TestName
Test Type:    $TestType
Date:         $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Duration:     $(Format-Duration $Duration)
Exit Code:    $ExitCode
Status:       $(if ($Success) { "PASSED OK" } else { "FAILED ERR" })
========================================

COMPILER OUTPUT:
========================================
$Output
========================================

TEST ANALYSIS:
========================================
Expected:     $(if ($TestType -eq "Valid") { "Successful compilation (exit 0)" } else { "Compilation error (exit != 0)" })
Actual:       $(if ($ExitCode -eq 0) { "Exit code 0 (success)" } else { "Exit code $ExitCode (error)" })
Result:       $(if ($Success) { "TEST PASSED OK" } else { "TEST FAILED ERR" })
========================================
"@
    
    # Сохраняем результат в файл
    Set-Content -Path $ResultFile -Value $ResultContent -Encoding UTF8
    
    # Выводим краткий результат в консоль
    if ($Success) {
        Write-Host "OK PASSED" -ForegroundColor $SuccessColor -NoNewline
        Write-Host " - $TestName" -ForegroundColor $SuccessColor -NoNewline
        Write-Host " ($(Format-Duration $Duration))" -ForegroundColor Gray
    } else {
        Write-Host "ERR FAILED" -ForegroundColor $ErrorColor -NoNewline
        Write-Host " - $TestName" -ForegroundColor $ErrorColor -NoNewline
        Write-Host " ($(Format-Duration $Duration))" -ForegroundColor Gray
        
        if ($Verbose) {
            Write-Host "`nOutput preview:" -ForegroundColor $WarningColor
            Write-Host ($Output -split "`n" | Select-Object -First 10 | Out-String) -ForegroundColor Gray
        }
    }
    
    return @{
        Success = $Success
        Duration = $Duration
        TestType = $TestType
        ExitCode = $ExitCode
    }
}

# ============================================================
# ГЛАВНАЯ ЛОГИКА
# ============================================================

Write-Host @"

     O Language Compiler - Test Runner        
              Windows Edition                 
"@ -ForegroundColor $InfoColor

# Собираем компилятор
Write-Host "`n[INFO] Building compiler..." -ForegroundColor $InfoColor
$BuildResult = dotnet build $CompilerProject --configuration Debug 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERROR] Compilation failed!" -ForegroundColor $ErrorColor
    Write-Host $BuildResult -ForegroundColor $ErrorColor
    exit 1
}

Write-Host "OK Compiler built successfully" -ForegroundColor $SuccessColor

# Проверяем, существует ли собранный компилятор
if (-not (Test-Path $BuildOutput)) {
    Write-Host "[ERROR] Compiler executable not found: $BuildOutput" -ForegroundColor $ErrorColor
    exit 1
}

# Получаем список тестов
$TestFiles = Get-ChildItem -Path $TestsDir -Filter $TestPattern | Sort-Object Name

if ($TestFiles.Count -eq 0) {
    Write-Host "[ERROR] No test files found matching pattern: $TestPattern" -ForegroundColor $ErrorColor
    exit 1
}

Write-Host "`n[INFO] Found $($TestFiles.Count) test files" -ForegroundColor $InfoColor

# Инициализация статистики
$TotalTests = 0
$PassedTests = 0
$FailedTests = 0
$ValidTests = 0
$InvalidTests = 0
$TotalDuration = [TimeSpan]::Zero

$Results = @()

# Запускаем тесты
foreach ($TestFile in $TestFiles) {
    $TestName = $TestFile.Name
    $TestNumber = ($TestName -replace '\D', '').PadLeft(2, '0')
    $ResultFile = Join-Path $ResultsDir "$TestNumber.txt"
    
    $TestType = Get-TestType $TestName
    
    # Пропускаем тесты по фильтрам
    if ($OnlyValid -and $TestType -ne "Valid") { continue }
    if ($OnlyInvalid -and $TestType -ne "Invalid") { continue }
    
    $TotalTests++
    if ($TestType -eq "Valid") { $ValidTests++ } else { $InvalidTests++ }
    
    $Result = Run-Test -TestFile $TestFile.FullName -TestName $TestName -ResultFile $ResultFile
    
    $TotalDuration += $Result.Duration
    
    if ($Result.Success) {
        $PassedTests++
    } else {
        $FailedTests++
        
        if ($StopOnError) {
            Write-Host "`n[ERROR] Stopping on first failure (--StopOnError)" -ForegroundColor $ErrorColor
            break
        }
    }
    
    $Results += @{
        Name = $TestName
        Type = $Result.TestType
        Success = $Result.Success
        Duration = $Result.Duration
        ExitCode = $Result.ExitCode
    }
}

# ============================================================
# ИТОГОВАЯ СТАТИСТИКА
# ============================================================

Write-Host "`n`n" -NoNewline
Write-Host "========================================" -ForegroundColor $InfoColor
Write-Host "          TEST SUMMARY" -ForegroundColor $InfoColor
Write-Host "========================================" -ForegroundColor $InfoColor

Write-Host "`nTotal Tests:       $TotalTests" -ForegroundColor White
Write-Host "  Valid Tests:     $ValidTests" -ForegroundColor White
Write-Host "  Invalid Tests:   $InvalidTests" -ForegroundColor White

Write-Host "`nPassed:            " -NoNewline -ForegroundColor White
Write-Host "$PassedTests" -ForegroundColor $SuccessColor

Write-Host "Failed:            " -NoNewline -ForegroundColor White
Write-Host "$FailedTests" -ForegroundColor $(if ($FailedTests -eq 0) { $SuccessColor } else { $ErrorColor })

$PassRate = if ($TotalTests -gt 0) { ($PassedTests / $TotalTests) * 100 } else { 0 }
Write-Host "`nPass Rate:         " -NoNewline -ForegroundColor White
Write-Host ("{0:N1}%" -f $PassRate) -ForegroundColor $(if ($PassRate -eq 100) { $SuccessColor } else { $WarningColor })

Write-Host "`nTotal Duration:    $(Format-Duration $TotalDuration)" -ForegroundColor White
$AvgDuration = if ($TotalTests -gt 0) { [TimeSpan]::FromTicks($TotalDuration.Ticks / $TotalTests) } else { [TimeSpan]::Zero }
Write-Host "Average Duration:  $(Format-Duration $AvgDuration)" -ForegroundColor White

Write-Host "`nResults saved to:  $ResultsDir" -ForegroundColor $InfoColor

# Таблица с результатами
Write-Host "`n" -NoNewline
Write-Host "========================================" -ForegroundColor $InfoColor
Write-Host "          DETAILED RESULTS" -ForegroundColor $InfoColor
Write-Host "========================================" -ForegroundColor $InfoColor

foreach ($Result in $Results) {
    $StatusIcon = if ($Result.Success) { "OK" } else { "ERR" }
    $StatusColor = if ($Result.Success) { $SuccessColor } else { $ErrorColor }
    
    Write-Host "`n$StatusIcon " -NoNewline -ForegroundColor $StatusColor
    Write-Host "$($Result.Name.PadRight(30))" -NoNewline -ForegroundColor White
    Write-Host "[$($Result.Type.PadRight(7))]" -NoNewline -ForegroundColor Gray
    Write-Host " $(Format-Duration $Result.Duration)" -ForegroundColor Gray
}

Write-Host "`n========================================`n" -ForegroundColor $InfoColor

# Создаём сводный отчёт
$SummaryFile = Join-Path $ResultsDir "summary.txt"
$SummaryContent = @"
========================================
O Language Compiler - Test Summary
========================================
Date:              $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Total Tests:       $TotalTests
  Valid Tests:     $ValidTests
  Invalid Tests:   $InvalidTests
Passed:            $PassedTests
Failed:            $FailedTests
Pass Rate:         $("{0:N1}%" -f $PassRate)
Total Duration:    $(Format-Duration $TotalDuration)
Average Duration:  $(Format-Duration $AvgDuration)
========================================

DETAILED RESULTS:
========================================
$($Results | ForEach-Object {
    $Status = if ($_.Success) { "PASS" } else { "FAIL" }
    "$Status`t$($_.Name)`t[$($_.Type)]`t$(Format-Duration $_.Duration)"
} | Out-String)
========================================
"@

Set-Content -Path $SummaryFile -Value $SummaryContent -Encoding UTF8
Write-Host "Summary report saved to: $SummaryFile`n" -ForegroundColor $InfoColor

# Возвращаем код выхода
if ($FailedTests -eq 0) {
    exit 0
} else {
    exit 1
}
