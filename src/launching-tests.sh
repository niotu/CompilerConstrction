#!/bin/bash

# Script to run all tests in the tests folder

# Path to the project
PROJECT_PATH="src/OCompiler"

# Path to the tests folder
TESTS_DIR="tests"

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

echo "Running all tests from folder: $TESTS_DIR"
echo "=========================================="

# Counters
total_tests=0
passed_tests=0
failed_tests=0

# Iterate through all .o files in the tests folder
for test_file in "$TESTS_DIR"/*.o; do
    if [ -f "$test_file" ]; then
        total_tests=$((total_tests + 1))
        echo "Running test: $(basename "$test_file")"
        
        # Run the test
        if dotnet run --project "$PROJECT_PATH" "$test_file"; then
            echo "✅ Test passed: $(basename "$test_file")"
            passed_tests=$((passed_tests + 1))
        else
            echo "❌ Test failed: $(basename "$test_file")"
            failed_tests=$((failed_tests + 1))
        fi
        
        echo "------------------------------------------"
    fi
done

# Print summary
echo "RESULTS:"
echo "Total tests: $total_tests"
echo "Passed: $passed_tests"
echo "Failed: $failed_tests"

if [ $failed_tests -eq 0 ]; then
    echo "🎉 All tests passed successfully!"
    exit 0
else
    echo "💥 Errors found in tests!"
    exit 1
fi