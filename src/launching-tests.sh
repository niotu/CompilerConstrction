#!/bin/bash

# Script to compile and run all tests in the tests folder

# Path to the project
PROJECT_PATH="src/OCompiler"

# Path to the tests folder
TESTS_DIR="tests"

# Results directory
RESULTS_DIR="test-results"

# Check if tests folder exists
if [ ! -d "$TESTS_DIR" ]; then
    echo "Error: Folder '$TESTS_DIR' not found!"
    exit 1
fi

# Check if project exists
if [ ! -d "$PROJECT_PATH" ]; then
    echo "Error: Project '$PROJECT_PATH' not found!"
    exit 1
fi

# Create results directory
mkdir -p "$RESULTS_DIR"

# Build the compiler first
echo "Building compiler..."
if ! dotnet build "$PROJECT_PATH" > /dev/null 2>&1; then
    echo "❌ Failed to build compiler!"
    exit 1
fi
echo "✅ Compiler built successfully"
echo "=========================================="

echo "Running all tests from folder: $TESTS_DIR"
echo "=========================================="

# Counters
total_tests=0
passed_tests=0
failed_tests=0

# Summary file
SUMMARY_FILE="$RESULTS_DIR/summary.txt"
echo "Test Results - $(date)" > "$SUMMARY_FILE"
echo "========================================" >> "$SUMMARY_FILE"

# Iterate through all .o files in the tests folder
for test_file in "$TESTS_DIR"/*.o; do
    if [ -f "$test_file" ]; then
        total_tests=$((total_tests + 1))
        test_name=$(basename "$test_file" .o)
        result_file="$RESULTS_DIR/${test_name}.txt"
        
        echo "Test: $test_name"
        
        # Compile the test
        if dotnet run --project "$PROJECT_PATH" -- "$test_file" --emit-exe app.exe > "$result_file" 2>&1; then
            # Run the compiled executable
            echo "Output:" >> "$result_file"
            if dotnet build/output.dll >> "$result_file" 2>&1; then
                # Extract output (last line before potential errors)
                output=$(dotnet build/output.dll 2>&1)
                echo "✅ PASSED: $test_name → Output: $output" | tee -a "$SUMMARY_FILE"
                passed_tests=$((passed_tests + 1))
            else
                echo "❌ FAILED (runtime): $test_name" | tee -a "$SUMMARY_FILE"
                failed_tests=$((failed_tests + 1))
            fi
        else
            echo "❌ FAILED (compilation): $test_name" | tee -a "$SUMMARY_FILE"
            failed_tests=$((failed_tests + 1))
        fi
        
        echo "------------------------------------------"
    fi
done

# Print summary
echo "" | tee -a "$SUMMARY_FILE"
echo "========================================" | tee -a "$SUMMARY_FILE"
echo "RESULTS:" | tee -a "$SUMMARY_FILE"
echo "Total tests: $total_tests" | tee -a "$SUMMARY_FILE"
echo "Passed: $passed_tests" | tee -a "$SUMMARY_FILE"
echo "Failed: $failed_tests" | tee -a "$SUMMARY_FILE"
echo "========================================" | tee -a "$SUMMARY_FILE"

if [ $failed_tests -eq 0 ]; then
    echo "🎉 All tests passed successfully!" | tee -a "$SUMMARY_FILE"
    exit 0
else
    echo "💥 Some tests failed! Check $RESULTS_DIR for details." | tee -a "$SUMMARY_FILE"
    exit 1
fi