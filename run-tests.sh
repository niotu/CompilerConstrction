#!/bin/bash


# Параметры по умолчанию
TEST_PATTERN="*.o"
VERBOSE=0
STOP_ON_ERROR=0
ONLY_VALID=0
ONLY_INVALID=0

# Цвета для вывода
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Настройка путей
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
TESTS_DIR="$PROJECT_DIR/tests"
RESULTS_DIR="$PROJECT_DIR/test-results"
COMPILER_PROJECT="$PROJECT_DIR/src/OCompiler/OCompiler.csproj"
BUILD_OUTPUT="$PROJECT_DIR/src/OCompiler/bin/Debug/net8.0/OCompiler"

# Функция для вывода с цветом
print_color() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Функция для вывода информации
print_info() {
    print_color "$CYAN" "$1"
}

print_success() {
    print_color "$GREEN" "$1"
}

print_error() {
    print_color "$RED" "$1"
}

print_warning() {
    print_color "$YELLOW" "$1"
}

# Парсинг аргументов командной строки
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--pattern)
            TEST_PATTERN="$2"
            shift 2
            ;;
        -v|--verbose)
            VERBOSE=1
            shift
            ;;
        -s|--stop-on-error)
            STOP_ON_ERROR=1
            shift
            ;;
        --only-valid)
            ONLY_VALID=1
            shift
            ;;
        --only-invalid)
            ONLY_INVALID=1
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo "Options:"
            echo "  -p, --pattern PATTERN    Test file pattern (default: *.o)"
            echo "  -v, --verbose           Enable verbose output"
            echo "  -s, --stop-on-error     Stop on first test failure"
            echo "  --only-valid            Run only valid tests"
            echo "  --only-invalid          Run only invalid tests"
            echo "  -h, --help              Show this help message"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Создаём папку для результатов, если её нет
if [ ! -d "$RESULTS_DIR" ]; then
    mkdir -p "$RESULTS_DIR"
    print_success "OK Created results directory: $RESULTS_DIR"
fi

# Очищаем старые результаты
print_info "\n[INFO] Cleaning old test results..."
find "$RESULTS_DIR" -name "*.txt" -type f -delete
print_success "OK Old results cleaned"

# Проверяем, существует ли папка с тестами
if [ ! -d "$TESTS_DIR" ]; then
    print_error "[ERROR] Tests directory not found: $TESTS_DIR"
    exit 1
fi

# Функция для определения типа теста (валидный/невалидный)
get_test_type() {
    local filename="$1"
    local test_number=$(echo "$filename" | sed 's/[^0-9]*//g' | sed 's/^0*//')
    
    # Если test_number пустой, устанавливаем 0
    test_number=${test_number:-0}
    
    # Тесты 01-10, 19-28 - валидные
    # Тесты 11-18 - невалидные (ожидаем ошибки)
    if (( test_number >= 1 && test_number <= 10 )) || (( test_number >= 19 && test_number <= 28 )); then
        echo "Valid"
    else
        echo "Invalid"
    fi
}

# Функция для форматирования времени выполнения
format_duration() {
    local duration_ms=$1
    if (( $(echo "$duration_ms < 1000" | bc -l) )); then
        printf "%.0fms" "$duration_ms"
    else
        printf "%.2fs" "$(echo "$duration_ms / 1000" | bc -l)"
    fi
}

# Функция запуска одного теста
run_test() {
    local test_file="$1"
    local test_name="$2"
    local result_file="$3"
    
    local test_type=$(get_test_type "$test_name")
    local start_time=$(date +%s%3N)
    
    echo -e "\n${CYAN}========================================${NC}"
    echo -e "${CYAN}Running: $test_name ($test_type)${NC}"
    echo -e "${CYAN}========================================${NC}"
    
    # Запускаем компилятор и перехватываем вывод
    local output=""
    local exit_code=0
    
    # Проверяем существование компилятора
    if [ ! -f "$BUILD_OUTPUT" ]; then
        output="ERROR: Compiler not found at $BUILD_OUTPUT"
        exit_code=1
    else
        # Делаем компилятор исполняемым
        chmod +x "$BUILD_OUTPUT" 2>/dev/null
        
        # Запускаем компилятор
        output=$("$BUILD_OUTPUT" "$test_file" 2>&1)
        exit_code=$?
    fi
    
    local end_time=$(date +%s%3N)
    local duration=$((end_time - start_time))
    
    # Определяем успешность теста
    local success=0
    
    if [ "$test_type" = "Valid" ]; then
        # Для валидных тестов успех = выход с кодом 0 и наличие "OK" в выводе
        if [ $exit_code -eq 0 ] && echo "$output" | grep -q "\*\*\[\s*OK\s*\].*completed successfully"; then
            success=1
        fi
    else
        # Для невалидных тестов успех = выход с кодом 1 и наличие ошибки
        if [ $exit_code -ne 0 ] && echo "$output" | grep -q "\*\*\[\s*ERR\s*\]"; then
            success=1
        fi
    fi
    
    # Формируем результат для файла
    local result_content="========================================
O Language Compiler - Test Result
========================================
Test File:    $test_name
Test Type:    $test_type
Date:         $(date '+%Y-%m-%d %H:%M:%S')
Duration:     $(format_duration $duration)
Exit Code:    $exit_code
Status:       $(if [ $success -eq 1 ]; then echo "PASSED OK"; else echo "FAILED ERR"; fi)
========================================

COMPILER OUTPUT:
========================================
$output
========================================

TEST ANALYSIS:
========================================
Expected:     $(if [ "$test_type" = "Valid" ]; then echo "Successful compilation (exit 0)"; else echo "Compilation error (exit != 0)"; fi)
Actual:       $(if [ $exit_code -eq 0 ]; then echo "Exit code 0 (success)"; else echo "Exit code $exit_code (error)"; fi)
Result:       $(if [ $success -eq 1 ]; then echo "TEST PASSED OK"; else echo "TEST FAILED ERR"; fi)
========================================"
    
    # Сохраняем результат в файл
    echo "$result_content" > "$result_file"
    
    # Выводим краткий результат в консоль
    if [ $success -eq 1 ]; then
        echo -e "${GREEN}OK PASSED${NC} - $test_name ${GRAY}($(format_duration $duration))${NC}"
    else
        echo -e "${RED}ERR FAILED${NC} - $test_name ${GRAY}($(format_duration $duration))${NC}"
        
        if [ $VERBOSE -eq 1 ]; then
            print_warning "\nOutput preview:"
            echo -e "${GRAY}$(echo "$output" | head -n 10)${NC}"
        fi
    fi
    
    # Возвращаем результат через глобальные переменные (bash 4.3+)
    RUN_TEST_SUCCESS=$success
    RUN_TEST_DURATION=$duration
    RUN_TEST_TYPE=$test_type
    RUN_TEST_EXIT_CODE=$exit_code
}

# ============================================================
# ГЛАВНАЯ ЛОГИКА
# ============================================================

echo -e "\n${CYAN}     O Language Compiler - Test Runner        ${NC}"
echo -e "${CYAN}             Unix/Linux Edition               ${NC}"

# Собираем компилятор
print_info "\n[INFO] Building compiler..."
if dotnet build "$COMPILER_PROJECT" --configuration Debug > /dev/null 2>&1; then
    print_success "OK Compiler built successfully"
else
    print_error "\n[ERROR] Compilation failed!"
    exit 1
fi

# Проверяем, существует ли собранный компилятор
if [ ! -f "$BUILD_OUTPUT" ]; then
    print_error "[ERROR] Compiler executable not found: $BUILD_OUTPUT"
    exit 1
fi

# Получаем список тестов
test_files=($(find "$TESTS_DIR" -name "$TEST_PATTERN" -type f | sort))

if [ ${#test_files[@]} -eq 0 ]; then
    print_error "[ERROR] No test files found matching pattern: $TEST_PATTERN"
    exit 1
fi

print_info "\n[INFO] Found ${#test_files[@]} test files"

# Инициализация статистики
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
VALID_TESTS=0
INVALID_TESTS=0
TOTAL_DURATION=0

declare -a RESULTS

# Запускаем тесты
for test_file in "${test_files[@]}"; do
    test_name=$(basename "$test_file")
    test_number=$(echo "$test_name" | sed 's/[^0-9]*//g' | sed 's/^0*//')
    test_number=$(printf "%02d" "$test_number")
    result_file="$RESULTS_DIR/${test_number}.txt"
    
    test_type=$(get_test_type "$test_name")
    
    # Пропускаем тесты по фильтрам
    if [ $ONLY_VALID -eq 1 ] && [ "$test_type" != "Valid" ]; then
        continue
    fi
    if [ $ONLY_INVALID -eq 1 ] && [ "$test_type" != "Invalid" ]; then
        continue
    fi
    
    ((TOTAL_TESTS++))
    if [ "$test_type" = "Valid" ]; then
        ((VALID_TESTS++))
    else
        ((INVALID_TESTS++))
    fi
    
    run_test "$test_file" "$test_name" "$result_file"
    
    TOTAL_DURATION=$((TOTAL_DURATION + RUN_TEST_DURATION))
    
    if [ $RUN_TEST_SUCCESS -eq 1 ]; then
        ((PASSED_TESTS++))
    else
        ((FAILED_TESTS++))
        
        if [ $STOP_ON_ERROR -eq 1 ]; then
            print_error "\n[ERROR] Stopping on first failure (--stop-on-error)"
            break
        fi
    fi
    
    # Сохраняем результаты для итогового отчета
    RESULTS+=("$test_name:$test_type:$RUN_TEST_SUCCESS:$RUN_TEST_DURATION:$RUN_TEST_EXIT_CODE")
done

# ============================================================
# ИТОГОВАЯ СТАТИСТИКА
# ============================================================

echo -e "\n\n${CYAN}========================================${NC}"
echo -e "${CYAN}          TEST SUMMARY${NC}"
echo -e "${CYAN}========================================${NC}"

echo -e "\nTotal Tests:       $TOTAL_TESTS"
echo -e "  Valid Tests:     $VALID_TESTS"
echo -e "  Invalid Tests:   $INVALID_TESTS"

echo -e "\nPassed:            ${GREEN}$PASSED_TESTS${NC}"
echo -n "Failed:            "
if [ $FAILED_TESTS -eq 0 ]; then
    echo -e "${GREEN}$FAILED_TESTS${NC}"
else
    echo -e "${RED}$FAILED_TESTS${NC}"
fi

if [ $TOTAL_TESTS -gt 0 ]; then
    PASS_RATE=$(echo "scale=1; $PASSED_TESTS * 100 / $TOTAL_TESTS" | bc)
else
    PASS_RATE=0
fi

echo -n -e "\nPass Rate:         "
if (( $(echo "$PASS_RATE == 100" | bc -l) )); then
    echo -e "${GREEN}$PASS_RATE%${NC}"
else
    echo -e "${YELLOW}$PASS_RATE%${NC}"
fi

echo -e "\nTotal Duration:    $(format_duration $TOTAL_DURATION)"
if [ $TOTAL_TESTS -gt 0 ]; then
    AVG_DURATION=$((TOTAL_DURATION / TOTAL_TESTS))
    echo -e "Average Duration:  $(format_duration $AVG_DURATION)"
else
    echo -e "Average Duration:  0ms"
fi

echo -e "\nResults saved to:  $RESULTS_DIR"

# Таблица с результатами
echo -e "\n${CYAN}========================================${NC}"
echo -e "${CYAN}          DETAILED RESULTS${NC}"
echo -e "${CYAN}========================================${NC}"

for result in "${RESULTS[@]}"; do
    IFS=':' read -r name type success duration exit_code <<< "$result"
    
    if [ "$success" -eq 1 ]; then
        status_icon="OK"
        status_color="$GREEN"
    else
        status_icon="ERR"
        status_color="$RED"
    fi
    
    printf "\n${status_color}%s${NC} %-30s [%-7s] ${GRAY}%s${NC}\n" \
        "$status_icon" "$name" "$type" "$(format_duration $duration)"
done

echo -e "\n${CYAN}========================================${NC}"

# Создаём сводный отчёт
SUMMARY_FILE="$RESULTS_DIR/summary.txt"
SUMMARY_CONTENT="========================================
O Language Compiler - Test Summary
========================================
Date:              $(date '+%Y-%m-%d %H:%M:%S')
Total Tests:       $TOTAL_TESTS
  Valid Tests:     $VALID_TESTS
  Invalid Tests:   $INVALID_TESTS
Passed:            $PASSED_TESTS
Failed:            $FAILED_TESTS
Pass Rate:         ${PASS_RATE}%
Total Duration:    $(format_duration $TOTAL_DURATION)
Average Duration:  $(format_duration $AVG_DURATION)
========================================

DETAILED RESULTS:
========================================"

for result in "${RESULTS[@]}"; do
    IFS=':' read -r name type success duration exit_code <<< "$result"
    status=$(if [ "$success" -eq 1 ]; then echo "PASS"; else echo "FAIL"; fi)
    SUMMARY_CONTENT+="\n$status\t$name\t[$type]\t$(format_duration $duration)"
done

SUMMARY_CONTENT+="\n========================================"

echo -e "$SUMMARY_CONTENT" > "$SUMMARY_FILE"
print_info "Summary report saved to: $SUMMARY_FILE\n"

# Возвращаем код выхода
if [ $FAILED_TESTS -eq 0 ]; then
    exit 0
else
    exit 1
fi