using System;
using System.Collections.Generic;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Runtime-хелперы для встроенных типов языка O.
    /// Реализует все встроенные методы с проверками во время выполнения.
    /// </summary>
    public static class BuiltinTypes
    {
        // ============================================
        // Integer - Целочисленный тип
        // ============================================
        public static class OInteger
        {
            /// <summary>Integer.Plus(x) - сложение</summary>
            public static int Plus(int self, int other) => self + other;

            /// <summary>Integer.Minus(x) - вычитание</summary>
            public static int Minus(int self, int other) => self - other;

            /// <summary>Integer.Mult(x) - умножение</summary>
            public static int Mult(int self, int other) => self * other;

            /// <summary>Integer.Div(x) - целочисленное деление</summary>
            public static int Div(int self, int divisor)
            {
                if (divisor == 0)
                    throw new DivideByZeroException("Division by zero in Integer.Div");
                return self / divisor;
            }

            /// <summary>Integer.Rem(x) - остаток от деления</summary>
            public static int Rem(int self, int divisor)
            {
                if (divisor == 0)
                    throw new DivideByZeroException("Remainder by zero in Integer.Rem");
                return self % divisor;
            }

            /// <summary>Integer.UnaryMinus() - унарный минус</summary>
            public static int UnaryMinus(int self) => -self;

            /// <summary>Integer.Less(x) - меньше</summary>
            public static bool Less(int self, int other) => self < other;

            /// <summary>Integer.LessEqual(x) - меньше или равно</summary>
            public static bool LessEqual(int self, int other) => self <= other;

            /// <summary>Integer.Greater(x) - больше</summary>
            public static bool Greater(int self, int other) => self > other;

            /// <summary>Integer.GreaterEqual(x) - больше или равно</summary>
            public static bool GreaterEqual(int self, int other) => self >= other;

            /// <summary>Integer.Equal(x) - равно</summary>
            public static bool Equal(int self, int other) => self == other;

            /// <summary>Integer.toReal() - конвертация в Real</summary>
            public static double ToReal(int self) => (double)self;

            /// <summary>Integer.Print() - вывести значение в консоль</summary>
            public static void Print(int self)
            {
                Console.WriteLine(self);
            }
        }

        // ============================================
        // Real - Вещественный тип (double)
        // ============================================
        public static class OReal
        {
            /// <summary>Real.Plus(x) - сложение</summary>
            public static double Plus(double self, double other) => self + other;

            /// <summary>Real.Minus(x) - вычитание</summary>
            public static double Minus(double self, double other) => self - other;

            /// <summary>Real.Mult(x) - умножение</summary>
            public static double Mult(double self, double other) => self * other;

            /// <summary>Real.Div(x) - деление</summary>
            public static double Div(double self, double divisor)
            {
                if (Math.Abs(divisor) < double.Epsilon)
                    throw new DivideByZeroException("Division by zero in Real.Div");
                return self / divisor;
            }

            /// <summary>Real.UnaryMinus() - унарный минус</summary>
            public static double UnaryMinus(double self) => -self;

            /// <summary>Real.Less(x) - меньше</summary>
            public static bool Less(double self, double other) => self < other;

            /// <summary>Real.LessEqual(x) - меньше или равно</summary>
            public static bool LessEqual(double self, double other) => self <= other;

            /// <summary>Real.Greater(x) - больше</summary>
            public static bool Greater(double self, double other) => self > other;

            /// <summary>Real.GreaterEqual(x) - больше или равно</summary>
            public static bool GreaterEqual(double self, double other) => self >= other;

            /// <summary>Real.Equal(x) - равно (с учётом погрешности)</summary>
            public static bool Equal(double self, double other) => Math.Abs(self - other) < double.Epsilon;

            /// <summary>Real.toInteger() - конвертация в Integer (округление вниз)</summary>
            public static int ToInteger(double self) => (int)self;
            /// <summary>Real.Print() - вывести значение в консоль</summary>
            public static void Print(double self)
            {
                Console.WriteLine(self);
            }
        }

        // ============================================
        // Boolean - Булев тип
        // ============================================
        public static class OBoolean
        {
            /// <summary>Boolean.And(x) - логическое И</summary>
            public static bool And(bool self, bool other) => self && other;

            /// <summary>Boolean.Or(x) - логическое ИЛИ</summary>
            public static bool Or(bool self, bool other) => self || other;

            /// <summary>Boolean.Xor(x) - исключающее ИЛИ</summary>
            public static bool Xor(bool self, bool other) => self ^ other;

            /// <summary>Boolean.Not() - логическое НЕ</summary>
            public static bool Not(bool self) => !self;

            /// <summary>Boolean.Equal(x) - равно</summary>
            public static bool Equal(bool self, bool other) => self == other;
            
            /// <summary>Boolean.toInteger() - преобразование в Integer</summary>
            public static int toInteger(bool self) => self ? 1 : 0;
            
            /// <summary>Boolean.Print() - вывести значение в консоль</summary>
            public static void Print(bool self)
            {
                Console.WriteLine(self);
            }
        }

        // ============================================
        // Array[T] - Массив
        // ============================================
        public static class OArray
        {
            /// <summary>Array.get(index) - получить элемент по индексу</summary>
            public static T Get<T>(T[] array, int index)
            {
                if (array == null)
                    throw new NullReferenceException("Array is null");
                
                if (index < 0 || index >= array.Length)
                    throw new IndexOutOfRangeException(
                        $"Array index {index} out of bounds [0, {array.Length})");
                
                return array[index];
            }

            /// <summary>Array.set(index, value) - установить элемент по индексу</summary>
            public static void Set<T>(T[] array, int index, T value)
            {
                if (array == null)
                    throw new NullReferenceException("Array is null");
                
                if (index < 0 || index >= array.Length)
                    throw new IndexOutOfRangeException(
                        $"Array index {index} out of bounds [0, {array.Length})");
                
                array[index] = value;
            }

            /// <summary>Array.Length - получить длину массива</summary>
            public static int GetLength<T>(T[] array)
            {
                if (array == null)
                    throw new NullReferenceException("Array is null");
                
                return array.Length;
            }

            /// <summary>Array.toList() - конвертация в List</summary>
            public static List<T> ToList<T>(T[] array)
            {
                if (array == null)
                    throw new NullReferenceException("Array is null");
                
                return new List<T>(array);
            }
        }

        // ============================================
        // List[T] - Список
        // ============================================
        public static class OList
        {
            /// <summary>List.append(value) - добавить элемент в конец и вернуть список</summary>
            public static List<T> Append<T>(List<T> list, T value)
            {
                if (list == null)
                    throw new NullReferenceException("List is null");
                
                list.Add(value);
                return list;
            }

            /// <summary>List.head() - получить первый элемент</summary>
            public static T Head<T>(List<T> list)
            {
                if (list == null)
                    throw new NullReferenceException("List is null");
                
                if (list.Count == 0)
                    throw new InvalidOperationException("Cannot get head of empty list");
                
                return list[0];
            }

            /// <summary>List.tail() - получить список без первого элемента</summary>
            public static List<T> Tail<T>(List<T> list)
            {
                if (list == null)
                    throw new NullReferenceException("List is null");
                
                if (list.Count == 0)
                    throw new InvalidOperationException("Cannot get tail of empty list");
                
                return list.GetRange(1, list.Count - 1);
            }

            /// <summary>List.isEmpty() - проверка на пустоту</summary>
            public static bool IsEmpty<T>(List<T> list)
            {
                if (list == null)
                    throw new NullReferenceException("List is null");
                
                return list.Count == 0;
            }

            /// <summary>List.Length - получить длину списка</summary>
            public static int GetLength<T>(List<T> list)
            {
                if (list == null)
                    throw new NullReferenceException("List is null");
                
                return list.Count;
            }
        }
    }
}
